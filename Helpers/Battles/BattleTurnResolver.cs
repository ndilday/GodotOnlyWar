using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OnlyWar.Helpers.Battles
{
    public class BattleTurnResolver
    {
        private BattleGridManager _grid;
        private readonly Region _region;
        private readonly BattleExecutionContext _execution;
        private readonly BattleAftermathContext _aftermathContext;
        private readonly IBattleAftermathPolicy _aftermathPolicy;
        private readonly WoundResolver _woundResolver;
        private readonly Dictionary<int, BattleSoldier> _casualtyMap;
        // Battle-scoped record of everyone who went down without dying -- a severed leg, a mangled
        // weapon hand. _casualtyMap is cleared every turn, but these soldiers stay where they fell
        // for the rest of the fight, so their fate can only be settled once the field has an owner.
        // See FinishOffAbandonedWounded.
        private readonly Dictionary<int, BattleSoldier> _incapacitatedSoldiers = [];
        public BattleHistory BattleHistory { get; private set; }
        private BattleState _currentState;
        private readonly Dictionary<BattleSide, PursuitPosture> _pursuitPostures = [];
        // Frozen current-turn pursuit pairings. Individual withdrawal escape must distinguish a
        // squad the enemy deliberately chose as its quarry from another member of the same force;
        // force-wide min/max speeds cannot answer that question.
        private readonly Dictionary<int, int> _pursuitTargetsBySquad = [];
        private readonly Dictionary<BattleSide, Queue<int>> _battleValueHistory = [];
        private readonly Dictionary<BattleSide, Queue<bool>> _damageActionHistory = [];
        private readonly Dictionary<int, float> _rearGuardStartingSeparation = [];
        // Morale (OnlyWar_TDD.md §6.6). Starting able strength per squad drives the
        // cumulative-casualty term; the turn-start able counts drive the this-turn casualty term;
        // the turn-start routing snapshot drives the propagation term (§5.1 — read prior-turn
        // results, never routs rolled this turn); the leader set flags squads that began with a
        // squad leader so a later leader loss reads as a command-loss shock.
        private readonly Dictionary<int, int> _startingAbleCount = [];
        private readonly Dictionary<int, int> _ableCountAtTurnStart = [];
        private readonly HashSet<int> _routingAtTurnStart = [];
        private readonly HashSet<int> _squadStartedWithLeader = [];
        // Every squad that ever routed this battle. BattleOutcome.RoutingSquadIds must report
        // them even after DisengageSquad/RemoveSquad clears the live WithdrawalRole.
        private readonly HashSet<int> _everRoutedSquadIds = [];
        private readonly List<BattleEvent> _turnEvents = [];
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private TimeSpan _planningElapsed;

        public event EventHandler<BattleHistory> OnBattleComplete;

        // A battle's only natural end is one side's annihilation. Two forces that cannot resolve
        // would otherwise spin the caller's while loop forever, so this caps the fight.
        private const int MaxBattleTurns = 1000;

        /// <summary>
        /// Consecutive turns in which NOTHING measurable happens -- no change in either side's able
        /// count, no change in the separation between them -- before the battle is declared inert.
        ///
        /// <para>WHY THIS SIGNAL. The turn cap catches a runaway battle 1000 turns after it stopped
        /// being a battle, and cannot distinguish "ground down slowly" from "did not start". Both
        /// halves together can: a fight that is actually happening either produces casualties or
        /// manoeuvres, and one that does neither for a hundred turns is not a slow fight, it is a
        /// stopped one. Requiring BOTH is what keeps a long-range duel with rare hits from tripping
        /// it -- any casualty at all resets the count, as does any repositioning.</para>
        ///
        /// <para>100 rather than something tighter because the cost of a false positive (a real
        /// battle aborted, or a spurious test failure) is much higher than the cost of 100 wasted
        /// turns. It is still a tenth of the turn cap, so an inert battle is caught an order of
        /// magnitude sooner and with a far more specific diagnosis.</para>
        /// </summary>
        private const int InertTurnThreshold = 100;

        // Separation is a float distance; quantize before comparing so sub-yard drift in a centroid
        // does not read as manoeuvre and reset the counter forever.
        private const float InertSeparationTolerance = 1f;

        private int _inertTurns;
        private int _lastAttackerAbleCount = -1;
        private int _lastOpposingAbleCount = -1;
        private float _lastSeparation = float.NaN;
        // What the soldiers were actually DOING on the last turn, by action type and count. An
        // inert battle's most useful single fact: "aiming and shooting but never hitting" and
        // "issuing no actions at all" are completely different bugs, and the counters above cannot
        // tell them apart.
        private string _lastTurnActionSummary = "none";

        // Disambiguates battles that share a date and a region. Process-wide and never persisted:
        // it exists to name a log stream, not to identify a battle across sessions.
        private static int _nextBattleId;

        internal BattleTurnResolver(BattleGridManager grid,
                                    IList<BattleSquad> attackerBattleSquads,
                                    IList<BattleSquad> opposingBattleSquads,
                                    Region region,
                                    BattleExecutionContext execution)
            : this(
                grid,
                attackerBattleSquads,
                opposingBattleSquads,
                region,
                execution,
                new BattleSideProfile(Models.Orders.Aggression.Normal, BattleRole.Attacker),
                new BattleSideProfile(Models.Orders.Aggression.Normal, BattleRole.Defender))
        {
        }

        internal BattleTurnResolver(BattleGridManager grid,
                                    IList<BattleSquad> attackerBattleSquads,
                                    IList<BattleSquad> opposingBattleSquads,
                                    Region region,
                                    BattleExecutionContext execution,
                                    BattleSideProfile attackerProfile,
                                    BattleSideProfile opposingProfile)
        {
            _grid = grid;
            _region = region;
            _execution = execution ?? throw new ArgumentNullException(nameof(execution));
            _woundResolver = new WoundResolver();
            _woundResolver.OnSoldierDeath += WoundResolver_OnSoldierDeath;
            _woundResolver.OnSoldierWounded += WoundResolver_OnSoldierWounded;
            _woundResolver.OnSoldierFall += WoundResolver_OnSoldierFall;
            _casualtyMap = new Dictionary<int, BattleSoldier>();
            BattleHistory = new BattleHistory();
            _aftermathContext = new BattleAftermathContext(
                attackerBattleSquads.ToList(),
                opposingBattleSquads.ToList(),
                region,
                BattleHistory,
                execution.Aftermath);
            _aftermathPolicy = BattleAftermathPolicyFactory.Create(_aftermathContext);

            _currentState = new BattleState(
                attackerBattleSquads.ToDictionary(bs => bs.Id, bs => bs),
                opposingBattleSquads.ToDictionary(os => os.Id, os => os),
                attackerProfile,
                opposingProfile);
            BattleHistory.Turns.Add(new BattleTurn(_currentState, new List<IAction>()));
            _battleValueHistory[BattleSide.Attacker] = new Queue<int>([_currentState.AttackerSide.StartingBattleValue]);
            _battleValueHistory[BattleSide.Opposing] = new Queue<int>([_currentState.OpposingSide.StartingBattleValue]);
            _damageActionHistory[BattleSide.Attacker] = new Queue<bool>();
            _damageActionHistory[BattleSide.Opposing] = new Queue<bool>();

            foreach (BattleSquad squad in attackerBattleSquads.Concat(opposingBattleSquads))
            {
                _startingAbleCount[squad.Id] = squad.AbleSoldiers.Count;
                if (squad.Soldiers.Any(soldier => soldier.Soldier.Template.IsSquadLeader))
                {
                    _squadStartedWithLeader.Add(squad.Id);
                }
            }

            BattleLog.BeginBattle(BuildBattleLogName());

            // Null-guarded like _region above, and like every other faction read in this file
            // (ProcessEndOfBattle, LogTurnCapWarning). A side's Faction resolves through
            // SquadTemplate.Faction and is genuinely absent for fixture-built and modded squads, so
            // an unguarded read here meant that merely RAISING THE LOG LEVEL to Debug threw an NRE
            // out of the resolver's constructor and took the whole battle down -- exactly the
            // hazard AmbushedMissionStep guards its own log string against, and invisible until
            // someone turned Debug on (2026-08-09).
            GameLog.Debug(() =>
                $"Battle start in {_region?.Name}: {_aftermathContext.FirstSideStartingSoldierCount} "
                + $"{_aftermathContext.FirstSideFaction?.Name} vs "
                + $"{_aftermathContext.SecondSideStartingSoldierCount}  "
                + $"{_aftermathContext.SecondSideFaction?.Name}");

            SeedAmbushAim();
        }

        /// <summary>
        /// Identifies this battle's log stream: <c>{gamedate}-{region}-{battleId}</c>. The id is a
        /// process-wide counter rather than anything persisted, because date and region alone do not
        /// separate two battles fought in the same region in the same week -- exactly the case a
        /// contested region produces. Announced to <see cref="BattleLog"/>, which leaves it to the
        /// host to decide whether that means a separate file.
        /// </summary>
        private string BuildBattleLogName()
        {
            int battleId = Interlocked.Increment(ref _nextBattleId);
            string date = SanitizeForFileName(_execution.Aftermath?.Date?.ToString()) ?? "unknown-date";
            string region = SanitizeForFileName(_region?.Name) ?? "unknown-region";
            return $"{date}-{region}-{battleId}";
        }

        /// <summary>
        /// Reduces a campaign name to something safe to use as a path component: invalid characters
        /// and whitespace both collapse to '_', since the point of per-battle files is that they are
        /// easy to glob and split on. Returns null for a name with nothing usable in it, so the
        /// caller's fallback applies.
        /// </summary>
        private static string SanitizeForFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new(value.Length);
            foreach (char character in value)
            {
                sb.Append(
                    char.IsWhiteSpace(character) || Array.IndexOf(invalid, character) >= 0
                        ? '_'
                        : character);
            }
            return sb.ToString();
        }

        // An ambushing side opens fire from concealment with weapons already trained on the kill
        // zone. Before the first turn is planned, pre-seed every ranged ambusher to the full aim
        // bonus so the trap is sprung with a fully-aimed opening volley on turn one instead of the
        // side spending turn one lining up shots. Only the side flagged BattleRole.Ambusher is
        // seeded; if neither side is an ambusher (a normal engagement) this is a no-op. The seeding
        // scan draws no battle RNG, so it does not perturb the seeded action stream -- but the
        // seeded aim does change turn-one planning, so ambush battles diverge from pre-change
        // baselines by design.
        private void SeedAmbushAim()
        {
            BattleSide? ambushSide =
                _currentState.AttackerSide.BattleRole == BattleRole.Ambusher ? BattleSide.Attacker
                : _currentState.OpposingSide.BattleRole == BattleRole.Ambusher ? BattleSide.Opposing
                : (BattleSide?)null;
            if (ambushSide == null) return;

            // Materialize the lazy per-soldier/per-squad views the target scan reads, exactly as a
            // planning pass would, so the seeding scan sees the same consistent state.
            PrepareParallelPlanningState();
            BattleSquadPlanner planner = new(
                _grid,
                _currentState.Soldiers,
                // Seeding writes only soldier aim state, never actions; give it throwaway sinks.
                new List<IAction>(),
                new List<IAction>(),
                new List<IAction>(),
                null,
                _execution.Rules.MeleeWeaponTemplates,
                _execution.Random,
                null,
                _execution.Rules.Skills.Tactics);
            foreach (BattleSquad squad in GetActiveSquads(ambushSide.Value))
            {
                planner.SeedAmbushAim(squad);
            }
        }

        private void WoundResolver_OnSoldierDeath(WoundResolution wound, WoundLevel woundLevel)
        {
            _casualtyMap[wound.Suffererer.Soldier.Id] = wound.Suffererer;
            bool isFirstSideEnemy = _aftermathContext.IsFirstSide(wound.Inflicter)
                && _aftermathContext.IsSecondSide(wound.Suffererer);
            bool isFirstDeath = BattleHistory.KilledSoldierIds.Add(wound.Suffererer.Soldier.Id);
            if (isFirstSideEnemy)
            {
                // Keep per-hit credit separate from the unique body count. A target can receive
                // multiple fatal wounds before the wound queue is drained, and each can be a valid
                // simultaneous kill credit even though only one enemy died.
                BattleHistory.FirstSideEnemiesKilled++;
                if (isFirstDeath)
                {
                    BattleHistory.FirstSideEnemyDeaths++;
                }
            }
            _aftermathPolicy.OnSoldierKilled(wound, woundLevel);
        }

        private void WoundResolver_OnSoldierWounded(WoundResolution wound, WoundLevel woundLevel)
        {
            BattleHistory.DamagedSoldierIds.Add(wound.Suffererer.Soldier.Id);
        }

        private void WoundResolver_OnSoldierFall(WoundResolution wound, WoundLevel woundLevel)
        {
            _casualtyMap[wound.Suffererer.Soldier.Id] = wound.Suffererer;
            _incapacitatedSoldiers[wound.Suffererer.Soldier.Id] = wound.Suffererer;
            // Down but not dead, and reportable as such. The set is provisional until the field
            // has an owner: FinishOffAbandonedWounded removes the losing side's wounded, and the
            // player policy settles battle-brothers at battle end.
            BattleHistory.IncapacitatedSoldierIds.Add(wound.Suffererer.Soldier.Id);
            _aftermathPolicy.OnSoldierDowned(wound, woundLevel);
        }

        public void ProcessNextTurn()
        {
            _grid.ClearReservations();
            _casualtyMap.Clear();
            _currentState.AdvanceTurn();

            Log(false, "Turn " + _currentState.TurnNumber.ToString());

            SnapshotTurnStartMoraleState();

            List<IAction> moveSegmentActions = [];
            List<IAction> shootSegmentActions = [];
            List<IAction> meleeSegmentActions = [];
            _turnEvents.Clear();
            List<string> log = BattleLog.IsEnabled ? [] : null;
            Action<string> logSink = log == null ? null : log.Add;
            long planningStarted = Stopwatch.GetTimestamp();
            Plan(shootSegmentActions, moveSegmentActions, meleeSegmentActions, logSink);
            _planningElapsed += Stopwatch.GetElapsedTime(planningStarted);
            if (log != null)
            {
                foreach (string line in log)
                {
                    Log(false, line);
                }
            }

            List<IAction> executedActions = new List<IAction>();
            List<BattleEvent> events = _turnEvents;
            HashSet<int> defendingSoldierIds = [];
            HandleShooting(shootSegmentActions, executedActions);
            HandleMoving(moveSegmentActions, executedActions);
            HandleMelee(meleeSegmentActions, executedActions, defendingSoldierIds);
            foreach (int soldierId in defendingSoldierIds)
            {
                if (_currentState.Soldiers.TryGetValue(soldierId, out BattleSoldier soldier))
                {
                    soldier.TurnsDefending++;
                }
            }
            _woundResolver.Resolve();

            CleanupAtEndOfTurn();
            foreach (int casualtyId in _casualtyMap.Keys)
            {
                _currentState.RemoveSoldier(casualtyId);
            }
            RecordRoundMetrics(executedActions);
            ResolveUnpursuedWithdrawalEscapes(events);
            ResolveContactBreaks(events);
            if (_currentState.ActiveAttackerSquads.Count > 0
                && _currentState.ActiveOpposingSquads.Count > 0)
            {
                // Stage 6 (OnlyWar_TDD.md §6.6): a rout preempts the plan,
                // so the morale check runs BEFORE the continuation/rear-guard decision.
                EvaluateMorale(events);
                if (BattleHistory.Outcome == null
                    && _currentState.ActiveAttackerSquads.Count > 0
                    && _currentState.ActiveOpposingSquads.Count > 0)
                {
                    EvaluateContinuation(events);
                }
            }

            BattleHistory.Turns.Add(new BattleTurn(
                _currentState,
                executedActions,
                events,
                _casualtyMap.Values));
            _lastTurnActionSummary = executedActions.Count == 0
                ? "none"
                : string.Join(", ", executedActions
                    .GroupBy(action => action.GetType().Name)
                    .OrderByDescending(group => group.Count())
                    .Select(group => $"{group.Key} x{group.Count()}"));
            if (_currentState.ActiveAttackerSquads.Count == 0 || _currentState.ActiveOpposingSquads.Count == 0)
            {
                EnsureTerminalOutcome();
                Log(false, "One side no longer active, battle over");
                ProcessEndOfBattle(false);
            }
            else if (UpdateInertTurnCount())
            {
                Log(false,
                    $"Battle inert for {InertTurnThreshold} turns; forcing disengagement");
                LogInertBattleWarning();
                BattleHistory.Outcome = BuildOutcome(BattleEndReason.TurnCap, null);
                ProcessEndOfBattle(true);
                if (_execution.ThrowOnInertBattle)
                {
                    // AFTER the graceful shutdown above, deliberately. The battle is already
                    // properly ended and its history recorded, so a caller that catches this (or a
                    // test that asserts on it) sees consistent state rather than a half-resolved
                    // battle. See BattleExecutionContext.ThrowOnInertBattle for why this is a
                    // test-only escalation of a condition the game itself survives.
                    throw new InertBattleException(DescribeInertBattle());
                }
            }
            else if (_currentState.TurnNumber >= MaxBattleTurns)
            {
                Log(false, $"Battle unresolved after {MaxBattleTurns} turns; forcing disengagement");
                LogTurnCapWarning();
                BattleHistory.Outcome = BuildOutcome(BattleEndReason.TurnCap, null);
                ProcessEndOfBattle(true);
            }
        }

        /// <summary>
        /// Reaching <see cref="MaxBattleTurns"/> is always a bug, not an outcome: the cap exists to
        /// stop a runaway loop, and every battle that hits it spent most of its turns doing nothing
        /// resolvable. The per-battle log already records the forced disengagement, but that stream
        /// is opt-in and megabytes long, so the fact never reaches anyone reading the game trace.
        /// This raises it to a warning carrying the numbers that identify the usual cause — a stern
        /// chase at matched speed, where the pursuer sits one move behind its quarry forever
        /// (separation converges on the pursuer's move, so both "cannot close" escape hatches see a
        /// catch as permanently imminent and never fire).
        /// </summary>
        /// <summary>
        /// Advances the inert-turn counter and reports whether the battle has now been doing
        /// nothing for <see cref="InertTurnThreshold"/> consecutive turns. Any casualty on either
        /// side, or any real change in separation, resets it.
        /// </summary>
        private bool UpdateInertTurnCount()
        {
            int attackerAble = _currentState.AllAttackerSquads.Values
                .Sum(squad => squad.AbleSoldiers.Count);
            int opposingAble = _currentState.AllOpposingSquads.Values
                .Sum(squad => squad.AbleSoldiers.Count);
            float separation = MinimumSeparation(BattleSide.Attacker, BattleSide.Opposing);
            bool unchanged = attackerAble == _lastAttackerAbleCount
                && opposingAble == _lastOpposingAbleCount
                && !float.IsNaN(_lastSeparation)
                && Math.Abs(separation - _lastSeparation) < InertSeparationTolerance;
            // Only the SEPARATION baseline is left alone while inert. Refreshing it every turn
            // would let a slow, steady drift stay under the tolerance indefinitely and never
            // accumulate into a reset -- the counter would run while the squads were genuinely, if
            // slowly, closing.
            if (unchanged)
            {
                _inertTurns++;
            }
            else
            {
                _inertTurns = 0;
                _lastSeparation = separation;
            }
            _lastAttackerAbleCount = attackerAble;
            _lastOpposingAbleCount = opposingAble;
            return _inertTurns >= InertTurnThreshold;
        }

        private string DescribeInertBattle()
        {
            BattleForceMetrics attackerMetrics = BuildMetrics(BattleSide.Attacker);
            BattleForceMetrics opposingMetrics = BuildMetrics(BattleSide.Opposing);
            return $"Battle in {_region?.Name} was inert for {InertTurnThreshold} consecutive "
                + $"turns at turn {_currentState.TurnNumber}: no casualties on either side and no "
                + $"change in separation. "
                + $"{_aftermathContext.FirstSideFaction?.Name} "
                + $"({attackerMetrics.AbleSoldierCount} able, bv "
                + $"{attackerMetrics.CurrentBattleValue}) vs "
                + $"{_aftermathContext.SecondSideFaction?.Name} "
                + $"({opposingMetrics.AbleSoldierCount} able, bv "
                + $"{opposingMetrics.CurrentBattleValue}); separation "
                + $"{MinimumSeparation(BattleSide.Attacker, BattleSide.Opposing):F1}. "
                + $"Last turn's actions: {_lastTurnActionSummary}. "
                + "Neither side can damage the other from where it stands, and neither is closing.";
        }

        private void LogInertBattleWarning()
        {
            GameLog.Warn(DescribeInertBattle);
        }

        private void LogTurnCapWarning()
        {
            GameLog.Warn(() =>
            {
                BattleSideState attacker = GetSideState(BattleSide.Attacker);
                BattleSideState opposing = GetSideState(BattleSide.Opposing);
                BattleForceMetrics attackerMetrics = BuildMetrics(BattleSide.Attacker);
                BattleForceMetrics opposingMetrics = BuildMetrics(BattleSide.Opposing);
                float separation = MinimumSeparation(BattleSide.Attacker, BattleSide.Opposing);
                return $"Battle in {_region?.Name} hit the {MaxBattleTurns}-turn cap without "
                    + "resolving; forcing disengagement. "
                    + $"{_aftermathContext.FirstSideFaction?.Name} ({attacker.Intent}, "
                    + $"{attackerMetrics.AbleSoldierCount} able, bv {attackerMetrics.CurrentBattleValue}, "
                    + $"speed {attackerMetrics.FastestPursuitSquadSpeed:F2}-"
                    + $"{attackerMetrics.SlowestMainBodySquadSpeed:F2}) vs "
                    + $"{_aftermathContext.SecondSideFaction?.Name} ({opposing.Intent}, "
                    + $"{opposingMetrics.AbleSoldierCount} able, bv {opposingMetrics.CurrentBattleValue}, "
                    + $"speed {opposingMetrics.FastestPursuitSquadSpeed:F2}-"
                    + $"{opposingMetrics.SlowestMainBodySquadSpeed:F2}); "
                    + $"separation {separation:F1}";
            });
        }

        private void ProcessEndOfBattle(bool hitTurnCap)
        {
            FinishOffAbandonedWounded();
            _stopwatch.Stop();
            GameLog.Debug(() =>
                $"Battle end in {_region?.Name}: {_currentState.TurnNumber} turns, "
                + $"{_stopwatch.ElapsedMilliseconds}ms "
                + $"({_planningElapsed.TotalMilliseconds:F0}ms planning, "
                + $"{_execution.MaxPlanningDegreeOfParallelism} planning workers), started "
                + $"{_aftermathContext.FirstSideStartingSoldierCount} vs "
                + $"{_aftermathContext.SecondSideStartingSoldierCount} soldiers");
            int firstSideRemaining = _currentState.AllAttackerSquads.Values.Sum(s => s.AbleSoldiers.Count);
            int secondSideRemaining = _currentState.AllOpposingSquads.Values.Sum(s => s.AbleSoldiers.Count);
            BattleHistory.ClosingSummary.AddRange(BattleSummaryBuilder.Build(
                _aftermathContext.FirstSideFaction?.Name,
                _aftermathContext.SecondSideFaction?.Name,
                _aftermathContext.FirstSideStartingSoldierCount,
                firstSideRemaining,
                _aftermathContext.SecondSideStartingSoldierCount,
                secondSideRemaining,
                _currentState.TurnNumber,
                hitTurnCap));
            // A soldier can fall (a leg gone) and later be killed outright by another wound in the
            // same battle. Death wins; the two sets must be disjoint before anything reports on
            // them. The player policy below settles battle-brothers itself and keeps them disjoint.
            BattleHistory.IncapacitatedSoldierIds.ExceptWith(BattleHistory.KilledSoldierIds);
            _aftermathPolicy.OnBattleCompleted(_currentState);
            // Closes the per-battle log stream before control returns to the campaign turn, so
            // whatever the caller does next is not filed under this battle. Anything the aftermath
            // policy logs above still belongs to it.
            BattleLog.EndBattle();
            OnBattleComplete?.Invoke(this, BattleHistory);
        }

        // A side that quits the field abandons everyone it could not carry off with it. Soldiers
        // who were taken out of the fight without a mortal wound -- shot through the leg, weapon
        // hand ruined -- are lying where they fell, at the mercy of whoever is still standing on
        // that ground when the shooting stops, and they get none. Counting them as dead is what
        // makes the debrief body count agree with the number of enemies the force actually took
        // out of the fight; without it a battle that removed all 19 cultists reported 15 dead.
        //
        // Only the losing side's wounded are finished off, and only when a side actually held the
        // field: a mutual disengagement or a turn-cap break-off leaves both sides free to recover
        // their own. Player soldiers are exempt in either direction -- a fallen battle-brother's
        // fate belongs to PlayerChapterBattleAftermathPolicy (severed vital -> dead, plus geneseed
        // recovery and the death event), and this must not kill him behind that policy's back.
        private void FinishOffAbandonedWounded()
        {
            if (BattleHistory.Outcome?.SideHoldingField is not BattleSide holder) return;
            BattleSide abandoningSide = Opposite(holder);

            foreach (BattleSoldier soldier in _incapacitatedSoldiers.Values)
            {
                if (soldier.Soldier is PlayerSoldier) continue;
                if (GetSoldierSide(soldier) != abandoningSide) continue;
                if (!BattleHistory.KilledSoldierIds.Add(soldier.Soldier.Id)) continue;
                BattleHistory.IncapacitatedSoldierIds.Remove(soldier.Soldier.Id);

                if (abandoningSide == BattleSide.Opposing)
                {
                    // The first side is the mission force, so these are its kills. The credit
                    // total tracks the body count here: the finishing blow belongs to the side as
                    // a whole rather than to any one soldier, and the soldier who put the enemy
                    // down was already credited individually when he fell.
                    BattleHistory.FirstSideEnemiesKilled++;
                    BattleHistory.FirstSideEnemyDeaths++;
                }
            }
        }

        // First side == attacker squads (see the constructor's BattleAftermathContext), so the
        // aftermath context's side membership answers this without a second squad-id index.
        private BattleSide? GetSoldierSide(BattleSoldier soldier) =>
            _aftermathContext.IsFirstSide(soldier) ? BattleSide.Attacker
            : _aftermathContext.IsSecondSide(soldier) ? BattleSide.Opposing
            : null;

        private void Plan(List<IAction> shootSegmentActions,
                          List<IAction> moveSegmentActions,
                          List<IAction> meleeSegmentActions,
                          Action<string> log)
        {
            PrepareParallelPlanningState();
            // One shared memo for the whole planning pass. The grid layout is frozen until
            // execution begins (below), so both sides' planners can safely reuse each other's
            // pure targeting computations. Rebuilt every turn, so nothing survives a layout change.
            BattlePlanningContext planningContext = new();
            PlanSimultaneously(
                shootSegmentActions,
                moveSegmentActions,
                meleeSegmentActions,
                log,
                planningContext);
        }

        private void PlanSimultaneously(
            ICollection<IAction> shootActions,
            ICollection<IAction> moveActions,
            ICollection<IAction> meleeActions,
            Action<string> log,
            BattlePlanningContext planningContext)
        {
            List<BattleSquad> attacker = GetActiveSquads(BattleSide.Attacker)
                .OrderBy(squad => squad.Id)
                .ToList();
            List<BattleSquad> opposing = GetActiveSquads(BattleSide.Opposing)
                .OrderBy(squad => squad.Id)
                .ToList();
            Dictionary<BattleSide, BattleSquadPlanner> planners = new()
            {
                [BattleSide.Attacker] = CreateSquadPlanner(
                    BattleSide.Attacker, shootActions, moveActions, meleeActions, log,
                    planningContext),
                [BattleSide.Opposing] = CreateSquadPlanner(
                    BattleSide.Opposing, shootActions, moveActions, meleeActions, log,
                    planningContext)
            };

            Dictionary<int, EngagementRoleConstraint> constraints = [];
            BuildRoleConstraints(BattleSide.Attacker, attacker, opposing, constraints);
            BuildRoleConstraints(BattleSide.Opposing, opposing, attacker, constraints);
            BattleEngagementFrameBuilder.PairedFrame paired =
                BattleEngagementFrameBuilder.Build(attacker, opposing, constraints);
            LogScreenEvaluations(BattleSide.Attacker, attacker, opposing, paired);
            LogScreenEvaluations(BattleSide.Opposing, opposing, attacker, paired);

            // Before the workers start, not from inside them. ChooseEngagementOption's first act is
            // to ensure this horizon exists, so leaving it to the workers made every one of them
            // contend for BattlePlanningContext.EngagementHorizonGate while one did the work --
            // 23% of all thread time in an instrumented profile, spent blocked. See
            // BattleSquadPlanner.InitializeEngagementHorizon.
            planners[BattleSide.Attacker].InitializeEngagementHorizon(
                paired.Profiles,
                paired.Frames,
                _execution.MaxPlanningDegreeOfParallelism);

            List<(BattleSide Side, List<BattleSquad> Friendly, List<BattleSquad> Enemy,
                BattleSquad Squad)> jobs = [];
            foreach ((BattleSide side, List<BattleSquad> friendly, List<BattleSquad> enemy) in new[]
            {
                (BattleSide.Attacker, attacker, opposing),
                (BattleSide.Opposing, opposing, attacker)
            })
            {
                foreach (BattleSquad squad in friendly.OrderBy(candidate => candidate.Id))
                {
                    jobs.Add((side, friendly, enemy, squad));
                }
            }

            // The complete squad decision is the bounded global planning job.  Every job reads the
            // same frozen frame/grid and writes one indexed result; there is no nested soldier
            // parallelism during this stage.  The declaration/action barriers below remain serial.
            (BattleSide Side, SquadEngagementDecision Decision)[] decisionResults =
                new (BattleSide, SquadEngagementDecision)[jobs.Count];
            void ChooseAt(int index)
            {
                var job = jobs[index];
                EngagementRoleConstraint constraint = constraints.GetValueOrDefault(job.Squad.Id)
                    ?? new EngagementRoleConstraint(EngagementSquadRole.Normal);
                SquadEngagementDecision decision = planners[job.Side].ChooseEngagementOption(
                    job.Squad,
                    paired.Frames[job.Squad.Id],
                    paired.Profiles,
                    paired.Frames,
                    job.Enemy,
                    constraint.RoleTargets);
                decisionResults[index] = (job.Side, decision);
            }
            if (_execution.MaxPlanningDegreeOfParallelism <= 1 || jobs.Count <= 1)
            {
                for (int index = 0; index < jobs.Count; index++) ChooseAt(index);
            }
            else
            {
                Parallel.For(
                    0,
                    jobs.Count,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _execution.MaxPlanningDegreeOfParallelism
                    },
                    ChooseAt);
            }
            List<(BattleSide Side, SquadEngagementDecision Decision)> decisions =
                decisionResults.ToList();

            _pursuitTargetsBySquad.Clear();
            foreach ((_, SquadEngagementDecision decision) in decisions)
            {
                if ((decision.Frame.Role is EngagementSquadRole.Pursuit
                        or EngagementSquadRole.Standoff)
                    && decision.Frame.PrimaryCounterpartSquadId is int targetSquadId)
                {
                    _pursuitTargetsBySquad[decision.Squad.Id] = targetSquadId;
                }
            }

            // Layer 2.5: reveal every posture before any soldier's ranged action is constructed.
            // Side and squad order are deterministic, but no choice can observe an earlier reveal.
            foreach ((BattleSide side, SquadEngagementDecision decision) in decisions
                .OrderBy(entry => entry.Side)
                .ThenBy(entry => entry.Decision.Squad.Id))
            {
                planners[side].DeclareEngagementDecision(decision);
            }
            foreach ((BattleSide side, SquadEngagementDecision decision) in decisions
                .OrderBy(entry => entry.Side)
                .ThenBy(entry => entry.Decision.Squad.Id))
            {
                planners[side].BuildEngagementActions(decision);
            }
        }

        private BattleSquadPlanner CreateSquadPlanner(
            BattleSide side,
            ICollection<IAction> shootActions,
            ICollection<IAction> moveActions,
            ICollection<IAction> meleeActions,
            Action<string> log,
            BattlePlanningContext planningContext)
        {
            BattleSquadPlanner planner = new(
                _grid,
                _currentState.Soldiers,
                shootActions,
                moveActions,
                meleeActions,
                log,
                _execution.Rules.MeleeWeaponTemplates,
                _execution.Random,
                planningContext,
                _execution.Rules.Skills.Tactics)
            {
                TraceTurnNumber = _currentState.TurnNumber,
                TraceSideLabel = side == BattleSide.Attacker ? "first" : "second"
            };
            return planner;
        }

        private void LogScreenEvaluations(
            BattleSide side,
            IReadOnlyCollection<BattleSquad> friendly,
            IReadOnlyCollection<BattleSquad> enemy,
            BattleEngagementFrameBuilder.PairedFrame paired)
        {
            if (!BattleLog.IsEnabled) return;
            float forceBv = friendly.Sum(squad =>
                paired.Profiles.GetValueOrDefault(squad.Id)?.TotalAbleBattleValue ?? 0);
            float committed = friendly
                .Where(squad => paired.Frames.GetValueOrDefault(squad.Id)?.ScreenThreatSquadId != null)
                .Sum(squad => paired.Profiles[squad.Id].TotalAbleBattleValue);
            foreach (BattleSquad screener in friendly.OrderBy(squad => squad.Id))
            {
                SquadEngagementFrame frame = paired.Frames[screener.Id];
                BattleSquadCapabilityProfile profile = paired.Profiles[screener.Id];
                foreach (BattleSquad threat in enemy
                    .Where(squad => paired.Profiles[squad.Id].IsContactSeeking)
                    .OrderBy(squad => squad.Id))
                {
                    bool selected = frame.ScreenThreatSquadId == threat.Id;
                    int? protectedId = selected ? frame.ProtectedSquadId : null;
                    float noScreenLoss = Math.Min(
                        paired.Profiles[threat.Id].UsableMeleeBattleValue,
                        protectedId.HasValue
                            ? paired.Profiles[protectedId.Value].TotalAbleBattleValue
                            : 0);
                    float holding = selected
                        ? Math.Min(1f,
                            (profile.UsableMeleeBattleValue + profile.TotalAbleBattleValue * 0.25f)
                            / Math.Max(1, paired.Profiles[threat.Id].UsableMeleeBattleValue))
                        : 0;
                    BattleLog.Write(new BattleDecisionTrace("SCREEN_EVAL", new List<KeyValuePair<string, string>>
                    {
                        BattleDecisionTrace.Field("turn", _currentState.TurnNumber),
                        BattleDecisionTrace.Field("side", side),
                        BattleDecisionTrace.Field("threat", threat.Id),
                        BattleDecisionTrace.Field("screener", screener.Id),
                        BattleDecisionTrace.Field("protected", protectedId?.ToString() ?? "none"),
                        BattleDecisionTrace.Field("intercept_point", frame.InterposePoint?.ToString() ?? "none"),
                        BattleDecisionTrace.Field("no_screen_loss", noScreenLoss),
                        BattleDecisionTrace.Field("screened_loss", noScreenLoss * (1 - holding)),
                        BattleDecisionTrace.Field("capacity_consumed", selected ? profile.ContactCapacity : 0),
                        BattleDecisionTrace.Field("force_commitment", committed),
                        BattleDecisionTrace.Field("force_cap", forceBv * 0.4f),
                        BattleDecisionTrace.Field("incumbent", selected
                            && screener.LastScreenThreatSquadId == threat.Id),
                        BattleDecisionTrace.Field("selected", selected),
                        BattleDecisionTrace.Field("rejection", selected ? "none" : "not_selected")
                    }).Render());
                }
            }
        }

        private void BuildRoleConstraints(
            BattleSide side,
            IReadOnlyCollection<BattleSquad> friendlySquads,
            IReadOnlyCollection<BattleSquad> enemySquads,
            IDictionary<int, EngagementRoleConstraint> constraints)
        {
            BattleSideState sideState = GetSideState(side);
            List<BattleSquad> active = friendlySquads
                .Where(squad => squad.Status == BattleSquadStatus.Active
                    && squad.AbleSoldiers.Count > 0)
                .OrderBy(squad => squad.Id)
                .ToList();
            foreach (BattleSquad squad in active.Where(squad =>
                squad.WithdrawalRole == WithdrawalRole.Routing
                || squad.MoraleState == MoraleState.Routing))
            {
                constraints[squad.Id] = new EngagementRoleConstraint(
                    EngagementSquadRole.Routing);
            }
            List<BattleSquad> disciplined = active
                .Where(squad => !constraints.ContainsKey(squad.Id))
                .ToList();
            if (disciplined.Count == 0) return;

            if (sideState.Intent == BattleSideIntent.FightingWithdrawal)
            {
                sideState.WithdrawalHeading ??= BattleForcePlanner.SelectWithdrawalHeading(
                    disciplined, enemySquads);
                if (disciplined.Count < 2)
                {
                    sideState.CoveringSquadId = null;
                    foreach (BattleSquad squad in disciplined)
                    {
                        constraints[squad.Id] = new EngagementRoleConstraint(
                            EngagementSquadRole.Bound,
                            sideState.WithdrawalHeading);
                    }
                    return;
                }
                BattleForcePlanner.CoverAssignment cover = BattleForcePlanner.SelectCover(
                    BattleForcePlanner.BuildCoverCandidates(disciplined, enemySquads),
                    sideState.CoveringSquadId);
                sideState.CoveringSquadId = cover.SquadId;
                LogCoverAssignment(side, sideState, cover);
                foreach (BattleSquad squad in disciplined)
                {
                    constraints[squad.Id] = new EngagementRoleConstraint(
                        cover.SquadId == squad.Id
                            ? EngagementSquadRole.Cover
                            : EngagementSquadRole.Bound,
                        sideState.WithdrawalHeading);
                }
                return;
            }
            if (sideState.Intent == BattleSideIntent.RearGuardWithdrawal)
            {
                sideState.WithdrawalHeading ??= BattleForcePlanner.SelectWithdrawalHeading(
                    disciplined, enemySquads);
                sideState.CoveringSquadId = null;
                foreach (BattleSquad squad in disciplined)
                {
                    constraints[squad.Id] = new EngagementRoleConstraint(
                        sideState.RearGuardSquadId == squad.Id
                            ? EngagementSquadRole.RearGuard
                            : EngagementSquadRole.Bound,
                        sideState.WithdrawalHeading);
                }
                return;
            }
            if (sideState.Intent == BattleSideIntent.Pursuing)
            {
                PursuitPosture forcePosture = _pursuitPostures.GetValueOrDefault(
                    side, PursuitPosture.BreakOff);
                float quarryRunSpeed = enemySquads.Count == 0
                    ? 0
                    : enemySquads.Min(squad => squad.GetSquadMove());
                foreach (BattleSquad squad in disciplined)
                {
                    constraints[squad.Id] = new EngagementRoleConstraint(
                        forcePosture switch
                        {
                            PursuitPosture.BreakOff => EngagementSquadRole.BreakOff,
                            PursuitPosture.Standoff => EngagementSquadRole.Standoff,
                            _ => EngagementSquadRole.Pursuit
                        },
                        QuarryRunSpeed: quarryRunSpeed,
                        RoleTargets: enemySquads);
                }
                return;
            }
            foreach (BattleSquad squad in disciplined)
            {
                constraints[squad.Id] = new EngagementRoleConstraint(
                    EngagementSquadRole.Normal);
            }
        }

        private void PrepareParallelPlanningState()
        {
            // Planning workers may inspect the same target and squad concurrently. Materialize all
            // lazy injury, equipment, roster, and aggregate views before the parallel sections so
            // worker reads cannot trigger mutations of shared cache state.
            foreach (BattleSoldier soldier in _currentState.Soldiers.Values)
            {
                soldier.PrepareForParallelPlanning();
            }
            foreach (BattleSquad squad in GetActiveSquads(BattleSide.Attacker)
                .Concat(GetActiveSquads(BattleSide.Opposing)))
            {
                squad.PrepareForParallelPlanning();
            }
        }

        private void HandleShooting(List<IAction> shootActions, List<IAction> executedActions)
        {
            // ConcurrentBag enumerated the actions in LIFO order in this single-threaded path.
            // Walk the list backwards to retain identical seeded execution and RNG consumption.
            for (int actionIndex = shootActions.Count - 1; actionIndex >= 0; actionIndex--)
            {
                IAction action = shootActions[actionIndex];
                action.Execute(_currentState);
                if (action is ShootAction shootAction)
                {
                    foreach (WoundResolution wound in shootAction.WoundResolutions)
                    {
                        _woundResolver.WoundQueue.Add(wound);
                    }
                }
                else if (action is AreaAttackAction areaAttackAction)
                {
                    foreach (WoundResolution wound in areaAttackAction.WoundResolutions)
                    {
                        _woundResolver.WoundQueue.Add(wound);
                    }
                }
                else if (action is BlastAttackAction blastAttackAction)
                {
                    foreach (WoundResolution wound in blastAttackAction.WoundResolutions)
                    {
                        _woundResolver.WoundQueue.Add(wound);
                    }
                }
                executedActions.Add(action);
            }
        }

        private void HandleMoving(List<IAction> moveActions, List<IAction> executedActions)
        {
            for (int actionIndex = moveActions.Count - 1; actionIndex >= 0; actionIndex--)
            {
                IAction action = moveActions[actionIndex];
                if (action is SquadChargeIntentAction) continue;
                action.Execute(_currentState);
                // Planning uses a frozen layout, so an earlier move can occupy this action's
                // destination before execution. Bank its budget, but don't report it as movement.
                if (action is not MoveAction moveAction || moveAction.Succeeded)
                {
                    executedActions.Add(action);
                }
            }

            // Charge destinations are deliberately absent from the frozen planning layout. Once
            // every ordinary move has resolved, discard those now-stale reservations and let each
            // charging squad coordinate against the target squad's actual positions.
            _grid.ClearReservations();
            foreach (SquadChargeIntentAction charge in moveActions
                .OfType<SquadChargeIntentAction>()
                .OrderBy(action => action.ActorId))
            {
                charge.Execute(_currentState);
                executedActions.AddRange(charge.ResolvedMovementActions);
            }
        }

        private void HandleMelee(List<IAction> meleeActions, List<IAction> executedActions, ISet<int> defendingSoldierIds)
        {
            MeleeAttackAction.ApplyChargeParryForfeitures(
                meleeActions.OfType<MeleeAttackAction>());
            // Reverse before the stable sort so the old bag order is retained if an actor ever
            // contributes more than one melee action in a segment.
            foreach (IAction action in meleeActions.AsEnumerable().Reverse().OrderBy(action => action.ActorId))
            {
                action.Execute(_currentState);
                if (action is MeleeAttackAction meleeAction)
                {
                    foreach (int targetId in meleeAction.TargetedDefenderIds)
                    {
                        defendingSoldierIds.Add(targetId);
                    }

                    foreach (WoundResolution wound in meleeAction.WoundResolutions)
                    {
                        _woundResolver.WoundQueue.Add(wound);
                    }
                }
                executedActions.Add(action);
            }
        }

        private void RecordRoundMetrics(IReadOnlyCollection<IAction> executedActions)
        {
            foreach (BattleSide side in Enum.GetValues<BattleSide>())
            {
                Queue<int> values = _battleValueHistory[side];
                values.Enqueue(CurrentBattleValue(side));
                while (values.Count > 3) values.Dequeue();

                bool usedDamagingAction = executedActions.Any(action =>
                    IsSoldierOnSide(action.ActorId, side)
                    && action is ShootAction or AreaAttackAction or BlastAttackAction or MeleeAttackAction);
                Queue<bool> damage = _damageActionHistory[side];
                damage.Enqueue(usedDamagingAction);
                while (damage.Count > 2) damage.Dequeue();
            }
        }

        private void SnapshotTurnStartMoraleState()
        {
            _ableCountAtTurnStart.Clear();
            _routingAtTurnStart.Clear();
            foreach (BattleSquad squad in GetActiveSquads(BattleSide.Attacker)
                .Concat(GetActiveSquads(BattleSide.Opposing)))
            {
                _ableCountAtTurnStart[squad.Id] = squad.AbleSoldiers.Count;
                if (squad.WithdrawalRole == WithdrawalRole.Routing)
                {
                    _routingAtTurnStart.Add(squad.Id);
                }
            }
        }

        // OnlyWar_TDD.md §6.6 Both sides are evaluated from the same post-round
        // physical state; propagation (§5.1) reads the turn-start routing snapshot, so iteration
        // order does not change results and a rout this turn only pressures neighbours next turn.
        private void EvaluateMorale(List<BattleEvent> events)
        {
            // Metrics for both sides are captured before either side's outcomes apply, and the
            // propagation term reads the turn-start routing snapshot (§5.1), so evaluating the
            // sides in sequence cannot change either side's results.
            BattleForceMetrics attackerMetrics = BuildMetrics(BattleSide.Attacker);
            BattleForceMetrics opposingMetrics = BuildMetrics(BattleSide.Opposing);
            EvaluateMoraleForSide(BattleSide.Attacker, attackerMetrics, opposingMetrics, events);
            if (BattleHistory.Outcome == null)
            {
                EvaluateMoraleForSide(BattleSide.Opposing, opposingMetrics, attackerMetrics, events);
            }
        }

        private void EvaluateMoraleForSide(
            BattleSide side,
            BattleForceMetrics friendlyMetrics,
            BattleForceMetrics enemyMetrics,
            List<BattleEvent> events)
        {
            List<BattleSquad> friendly = GetActiveSquads(side).OrderBy(squad => squad.Id).ToList();
            List<BattleSquad> enemy = GetActiveSquads(Opposite(side)).ToList();
            float forceDisadvantage = BattleMoraleEvaluator.ComputeForceDisadvantage(
                friendlyMetrics.CurrentBattleValue,
                enemyMetrics.CurrentBattleValue,
                friendlyMetrics.BattleValueLostPreviousTwoRounds,
                enemyMetrics.BattleValueLostPreviousTwoRounds);

            foreach (BattleSquad squad in friendly)
            {
                BattleMoraleEvaluator.MoraleSkipReason skip =
                    BattleMoraleEvaluator.ShouldCheckMorale(squad, friendly, _grid);
                if (skip != BattleMoraleEvaluator.MoraleSkipReason.Check)
                {
                    // A synapse provider or covered squad is not shaken — hold it Steady. An
                    // already-routing squad is sticky (§6): leave its state untouched.
                    if (skip != BattleMoraleEvaluator.MoraleSkipReason.AlreadyRouting)
                    {
                        squad.MoraleState = MoraleState.Steady;
                    }
                    LogMoraleSkip(side, squad, skip);
                    continue;
                }

                int startingAble = _startingAbleCount.GetValueOrDefault(
                    squad.Id, squad.AbleSoldiers.Count);
                int turnStartAble = _ableCountAtTurnStart.GetValueOrDefault(
                    squad.Id, squad.AbleSoldiers.Count);
                int currentAble = squad.AbleSoldiers.Count;
                float casualtyThisTurn = turnStartAble > 0
                    ? Math.Clamp((float)(turnStartAble - currentAble) / turnStartAble, 0f, 1f)
                    : 0f;
                float cumulativeCasualty = startingAble > 0
                    ? Math.Clamp((float)(startingAble - currentAble) / startingAble, 0f, 1f)
                    : 0f;
                bool leaderDead = _squadStartedWithLeader.Contains(squad.Id)
                    && squad.SquadLeader == null;
                float routingVisible = BattleMoraleEvaluator.ComputeRoutingVisibleFriendlyFraction(
                    squad, friendly, _routingAtTurnStart, _grid, MoraleConstants.VisualRange);
                float localOutnumber = BattleMoraleEvaluator.ComputeLocalOutnumberRatio(
                    squad, friendly, enemy, _grid, MoraleConstants.VisualRange);
                float commandAura = CommandAuraSupport(squad, side);

                BattleSoldier leader = squad.SquadLeader;
                List<BattleMoraleEvaluator.SoldierMoraleInput> soldiers = squad.AbleSoldiers
                    .OrderBy(soldier => soldier.Soldier.Id)
                    .Select(soldier => new BattleMoraleEvaluator.SoldierMoraleInput(
                        soldier.Soldier.Id,
                        soldier.Soldier.Ego,
                        leader != null && soldier.Soldier.Id == leader.Soldier.Id))
                    .ToList();

                BattleMoraleEvaluator.MoraleCheckResult result = BattleMoraleEvaluator.Evaluate(
                    new BattleMoraleEvaluator.MoraleCheckInput(
                        soldiers,
                        casualtyThisTurn,
                        cumulativeCasualty,
                        leaderDead,
                        routingVisible,
                        localOutnumber,
                        commandAura,
                        forceDisadvantage),
                    _execution.Random);

                squad.MoraleState = result.Outcome;
                if (result.Outcome == MoraleState.Routing)
                {
                    squad.WithdrawalRole = WithdrawalRole.Routing;
                    _everRoutedSquadIds.Add(squad.Id);
                    events.Add(new BattleEvent(
                        BattleEventType.SquadRouted,
                        _currentState.TurnNumber,
                        side,
                        squad.Id,
                        null,
                        $"{squad.Name} broke and routed."));
                }
                LogMoraleEval(
                    side,
                    squad,
                    result,
                    casualtyThisTurn,
                    cumulativeCasualty,
                    leaderDead,
                    routingVisible,
                    localOutnumber,
                    commandAura,
                    forceDisadvantage);
            }

            // If every remaining active squad on the side is Routing, the side reflects Rout
            // intent so the existing contact-break machinery (which treats Rout as a withdrawal
            // intent) disengages it. Individual routs on an otherwise-fighting side keep the
            // side's intent; those squads flee via the planner's routing path.
            List<BattleSquad> active = GetActiveSquads(side).ToList();
            BattleSideState state = GetSideState(side);
            if (active.Count > 0
                && active.All(squad => squad.WithdrawalRole == WithdrawalRole.Routing)
                && state.Intent != BattleSideIntent.Rout
                && state.Intent != BattleSideIntent.Disengaged)
            {
                state.Intent = BattleSideIntent.Rout;
                state.WithdrawalStartedTurn ??= _currentState.TurnNumber;
                state.CoveringSquadId = null;
                state.RearGuardSquadId = null;
                events.Add(new BattleEvent(
                    BattleEventType.WithdrawalOrdered,
                    _currentState.TurnNumber,
                    side,
                    null,
                    active.Select(squad => squad.Id),
                    $"{SideName(side)} broke and routed."));
                // Without a posture the contact-break default (BreakOff) would let the routed
                // side escape instantly; the enemy decides whether to run it down.
                EvaluatePursuitResponse(side, events);
            }
        }

        // §4.3 command aura (Phase 6): signed, stateless, recomputed per turn like every other
        // morale input. Reads the side's FULL roster (not just active squads) because the
        // HQ-loss reading — every fielded HQ squad destroyed applies +CommandLossStress of
        // stress — needs destroyed HQ squads to stay visible. See CommandAuraEvaluator.
        private float CommandAuraSupport(BattleSquad squad, BattleSide side) =>
            CommandAuraEvaluator.ComputeCommandAuraModifier(
                squad, GetAllSquads(side), _grid, _execution.Rules.Skills.Tactics);

        // Squads are species-homogeneous (§3.1), so squad Ego is the shared per-soldier Ego;
        // averaging over able soldiers is robust to a future mixed template. Used by the §7
        // rear-guard predicate and the §9 coverage-needing gate.
        private static float SquadEgo(BattleSquad squad)
        {
            List<BattleSoldier> able = squad.AbleSoldiers;
            return able.Count > 0 ? able.Average(soldier => soldier.Soldier.Ego) : 0f;
        }

        // §8.2 closed-form command-collapse estimate: would <paramref name="squad"/> rout at
        // ordinary morale — synapse immunity ignored, the §4.3 command-aura term set to
        // <paramref name="commandAuraSupport"/>? Builds the same MoraleCheckInput the live check
        // builds (EvaluateMoraleForSide) but resolves it deterministically via
        // BattleMoraleEvaluator's RNG-free estimator — no battle RNG is consumed, so it is safe
        // inside the forecast. Callers pick the aura term per branch: the squad's live value for
        // the synapse-severance verdict, or -CommandLossStress for the every-HQ-lost verdict.
        // Every other stress input is the squad's real current state.
        private bool EstimateRoutsAtOrdinaryMorale(
            BattleSquad squad,
            IReadOnlyList<BattleSquad> friendly,
            IReadOnlyList<BattleSquad> enemy,
            float forceDisadvantage,
            float commandAuraSupport)
        {
            int startingAble = _startingAbleCount.GetValueOrDefault(squad.Id, squad.AbleSoldiers.Count);
            int turnStartAble = _ableCountAtTurnStart.GetValueOrDefault(squad.Id, squad.AbleSoldiers.Count);
            int currentAble = squad.AbleSoldiers.Count;
            float casualtyThisTurn = turnStartAble > 0
                ? Math.Clamp((float)(turnStartAble - currentAble) / turnStartAble, 0f, 1f)
                : 0f;
            float cumulativeCasualty = startingAble > 0
                ? Math.Clamp((float)(startingAble - currentAble) / startingAble, 0f, 1f)
                : 0f;
            bool leaderDead = _squadStartedWithLeader.Contains(squad.Id) && squad.SquadLeader == null;
            float routingVisible = BattleMoraleEvaluator.ComputeRoutingVisibleFriendlyFraction(
                squad, friendly, _routingAtTurnStart, _grid, MoraleConstants.VisualRange);
            float localOutnumber = BattleMoraleEvaluator.ComputeLocalOutnumberRatio(
                squad, friendly, enemy, _grid, MoraleConstants.VisualRange);
            BattleSoldier leader = squad.SquadLeader;
            List<BattleMoraleEvaluator.SoldierMoraleInput> soldiers = squad.AbleSoldiers
                .OrderBy(soldier => soldier.Soldier.Id)
                .Select(soldier => new BattleMoraleEvaluator.SoldierMoraleInput(
                    soldier.Soldier.Id,
                    soldier.Soldier.Ego,
                    leader != null && soldier.Soldier.Id == leader.Soldier.Id))
                .ToList();

            return BattleMoraleEvaluator.EstimateOutcome(
                new BattleMoraleEvaluator.MoraleCheckInput(
                    soldiers,
                    casualtyThisTurn,
                    cumulativeCasualty,
                    leaderDead,
                    routingVisible,
                    localOutnumber,
                    commandAuraSupport,
                    forceDisadvantage)) == MoraleState.Routing;
        }

        private void LogMoraleSkip(
            BattleSide side,
            BattleSquad squad,
            BattleMoraleEvaluator.MoraleSkipReason skip)
        {
            if (!BattleLog.IsEnabled) return;
            BattleDecisionTrace trace = new("MORALE_EVAL", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("turn", _currentState.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("squad", squad.Id),
                BattleDecisionTrace.Field("skip", RenderSkip(skip)),
                BattleDecisionTrace.Field("outcome", squad.MoraleState)
            });
            BattleLog.Write(trace.Render());
        }

        private void LogMoraleEval(
            BattleSide side,
            BattleSquad squad,
            BattleMoraleEvaluator.MoraleCheckResult result,
            float casualtyThisTurn,
            float cumulativeCasualty,
            bool leaderDead,
            float routingVisible,
            float localOutnumber,
            float commandAura,
            float forceDisadvantage)
        {
            if (!BattleLog.IsEnabled) return;
            BattleDecisionTrace trace = new("MORALE_EVAL", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("turn", _currentState.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("squad", squad.Id),
                BattleDecisionTrace.Field("skip", "none"),
                BattleDecisionTrace.Field("casualty_this_turn", casualtyThisTurn),
                BattleDecisionTrace.Field("cumulative_casualty", cumulativeCasualty),
                BattleDecisionTrace.Field("leader_dead", leaderDead),
                BattleDecisionTrace.Field("routing_visible", routingVisible),
                BattleDecisionTrace.Field("local_outnumber", localOutnumber),
                // Signed §4.3 aura contribution: positive = support from a living HQ in
                // radius; negative = command-loss stress (every fielded HQ destroyed).
                BattleDecisionTrace.Field("command_aura", commandAura),
                BattleDecisionTrace.Field("force_disadvantage", forceDisadvantage),
                BattleDecisionTrace.Field("shock", result.Shock),
                BattleDecisionTrace.Field("context", result.Context),
                BattleDecisionTrace.Field("stress", result.Stress),
                BattleDecisionTrace.Field("able", result.AbleSoldiers),
                BattleDecisionTrace.Field("fails", result.Fails),
                BattleDecisionTrace.Field("fail_fraction", result.FailFraction),
                BattleDecisionTrace.Field("leader_held", result.LeaderHeld),
                BattleDecisionTrace.Field("rout_threshold", result.RoutThreshold),
                BattleDecisionTrace.Field("shaken_threshold", result.ShakenThreshold),
                BattleDecisionTrace.Field("outcome", result.Outcome)
            });
            BattleLog.Write(trace.Render());
        }

        private static string RenderSkip(BattleMoraleEvaluator.MoraleSkipReason skip) => skip switch
        {
            BattleMoraleEvaluator.MoraleSkipReason.NoAbleSoldiers => "no_able_soldiers",
            BattleMoraleEvaluator.MoraleSkipReason.AlreadyRouting => "already_routing",
            BattleMoraleEvaluator.MoraleSkipReason.ProvidesSynapse => "provides_synapse",
            BattleMoraleEvaluator.MoraleSkipReason.SynapseCovered => "synapse_covered",
            _ => "none"
        };

        private void EvaluateContinuation(List<BattleEvent> events)
        {
            BattleForceMetrics attacker = BuildMetrics(BattleSide.Attacker);
            BattleForceMetrics opposing = BuildMetrics(BattleSide.Opposing);
            BattleForceEvaluationResult attackerResult = BattleForceEvaluator.Evaluate(new(
                _currentState.TurnNumber,
                "first",
                _currentState.AttackerSide.Aggression,
                attacker,
                opposing,
                IsWithdrawalIntent(_currentState.AttackerSide.Intent)));
            BattleForceEvaluationResult opposingResult = BattleForceEvaluator.Evaluate(new(
                _currentState.TurnNumber,
                "second",
                _currentState.OpposingSide.Aggression,
                opposing,
                attacker,
                IsWithdrawalIntent(_currentState.OpposingSide.Intent)));

            bool attackerStarts = attackerResult.ShouldWithdraw
                && !IsWithdrawalIntent(_currentState.AttackerSide.Intent);
            bool opposingStarts = opposingResult.ShouldWithdraw
                && !IsWithdrawalIntent(_currentState.OpposingSide.Intent);
            if (attackerStarts && opposingStarts)
            {
                DisengageForce(BattleSide.Attacker, events, "Both forces elected to withdraw.");
                DisengageForce(BattleSide.Opposing, events, "Both forces elected to withdraw.");
                BattleHistory.Outcome = BuildOutcome(BattleEndReason.MutualDisengagement, null);
                return;
            }

            if (attackerStarts) BeginWithdrawal(BattleSide.Attacker, events);
            if (opposingStarts && BattleHistory.Outcome == null) BeginWithdrawal(BattleSide.Opposing, events);

            TryAssignRearGuard(BattleSide.Attacker, events);
            TryAssignRearGuard(BattleSide.Opposing, events);
        }

        private void BeginWithdrawal(BattleSide withdrawingSide, List<BattleEvent> events)
        {
            BattleSide pursuingSide = Opposite(withdrawingSide);
            BattleSideState withdrawing = GetSideState(withdrawingSide);
            withdrawing.Intent = BattleSideIntent.FightingWithdrawal;
            withdrawing.WithdrawalStartedTurn ??= _currentState.TurnNumber;
            withdrawing.WithdrawalHeading ??= BattleForcePlanner.SelectWithdrawalHeading(
                GetActiveSquads(withdrawingSide),
                GetActiveSquads(pursuingSide));
            events.Add(new BattleEvent(
                BattleEventType.WithdrawalOrdered,
                _currentState.TurnNumber,
                withdrawingSide,
                null,
                GetActiveSquads(withdrawingSide).Select(squad => squad.Id),
                $"{SideName(withdrawingSide)} ordered a fighting withdrawal."));

            DisengageBurrowers(withdrawingSide, events);
            if (GetActiveSquads(withdrawingSide).Count == 0)
            {
                CompleteWithdrawal(withdrawingSide, events);
                return;
            }

            EvaluatePursuitResponse(withdrawingSide, events);
        }

        // Decides the enemy's pursuit posture toward a side that is withdrawing or routing.
        // BreakOff completes the withdrawal immediately; otherwise the enemy's intent becomes
        // Pursuing. Shared by voluntary withdrawal, the morale rout path, and the per-round
        // refresh in ResolveContactBreak, so it emits its event only when the posture actually
        // changes rather than once per round.
        private void EvaluatePursuitResponse(BattleSide withdrawingSide, List<BattleEvent> events)
        {
            BattleSide pursuingSide = Opposite(withdrawingSide);
            BattleSideState pursuing = GetSideState(pursuingSide);
            BattleForceMetrics pursuitMetrics = BuildMetrics(pursuingSide);
            BattleForceMetrics withdrawalMetrics = BuildMetrics(withdrawingSide);
            float separation = MinimumSeparation(pursuingSide, withdrawingSide);
            float speedAdvantage = Math.Max(
                0.01f,
                pursuitMetrics.FastestPursuitSquadSpeed - withdrawalMetrics.SlowestMainBodySquadSpeed);
            float pressTurns = separation / speedAdvantage;
            // The withdrawer "returns fire" only if some non-routing element still carries a
            // ranged weapon: a fully routed side shoots at no one, and a melee-only force
            // never could. Routing roles are already set when a rout triggers this
            // evaluation, so the flag reads the current turn's reality.
            bool withdrawerReturnsFire = GetActiveSquads(withdrawingSide).Any(
                squad => squad.WithdrawalRole != WithdrawalRole.Routing
                    && squad.AbleSoldiers.Any(
                        soldier => soldier.EquippedRangedWeapons.Count > 0));
            // Whether the pursuit can put someone in melee THIS turn, independent of whether it
            // can close over time. A pursuer already in contact, or one Run-and-charge away, has
            // a catch available right now, so the "cannot close" override must not talk it out of
            // taking it. The reach test is net of the quarry's own withdrawal — see
            // BattleContactRules.CanReachMeleeThisTurn for why measuring it against the pursuer's
            // raw move instead pinned this flag true for the whole of a stern chase.
            bool pursuerCanReachMeleeThisTurn =
                GetActiveSquads(pursuingSide).Any(squad => squad.IsInMelee)
                || BattleContactRules.CanReachMeleeThisTurn(
                    separation,
                    pursuitMetrics.FastestPursuitSquadSpeed,
                    withdrawalMetrics.SlowestMainBodySquadSpeed);
            float? projectedFollowShotTurns = ProjectedFollowShotTurns(
                pursuingSide,
                withdrawingSide,
                separation,
                withdrawalMetrics.SlowestMainBodySquadSpeed);
            BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(new(
                _currentState.TurnNumber,
                pursuingSide == BattleSide.Attacker,
                pursuing.Aggression,
                pursuitMetrics.AbleSoldierCount,
                withdrawalMetrics.AbleSoldierCount,
                pursuitMetrics.FastestPursuitSquadSpeed,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                pressTurns,
                projectedFollowShotTurns,
                withdrawerReturnsFire,
                pursuerCanReachMeleeThisTurn));
            PursuitPosture? previous = _pursuitPostures.TryGetValue(
                pursuingSide,
                out PursuitPosture stored) ? stored : null;
            _pursuitPostures[pursuingSide] = result.Posture;
            if (result.Posture == PursuitPosture.BreakOff)
            {
                events.Add(new BattleEvent(
                    BattleEventType.PursuitEnded,
                    _currentState.TurnNumber,
                    pursuingSide,
                    null,
                    null,
                    $"{SideName(pursuingSide)} declined pursuit."));
                CompleteWithdrawal(withdrawingSide, events);
                return;
            }

            pursuing.Intent = BattleSideIntent.Pursuing;
            if (previous == result.Posture) return;
            events.Add(new BattleEvent(
                BattleEventType.PursuitStarted,
                _currentState.TurnNumber,
                pursuingSide,
                null,
                GetActiveSquads(withdrawingSide).Select(squad => squad.Id),
                previous.HasValue
                    ? $"{SideName(pursuingSide)} switched to a {result.Posture} pursuit."
                    : $"{SideName(pursuingSide)} began a {result.Posture} pursuit."));
        }

        private void ResolveContactBreaks(List<BattleEvent> events)
        {
            ResolveContactBreak(BattleSide.Attacker, events);
            if (BattleHistory.Outcome == null)
            {
                ResolveContactBreak(BattleSide.Opposing, events);
            }
        }

        private void ResolveUnpursuedWithdrawalEscapes(List<BattleEvent> events)
        {
            ResolveUnpursuedWithdrawalEscapes(BattleSide.Attacker, events);
            ResolveUnpursuedWithdrawalEscapes(BattleSide.Opposing, events);
        }

        private void ResolveUnpursuedWithdrawalEscapes(
            BattleSide withdrawingSide,
            List<BattleEvent> events)
        {
            if (!IsWithdrawalIntent(GetSideState(withdrawingSide).Intent)) return;

            BattleSide pursuingSide = Opposite(withdrawingSide);
            List<BattleSquad> pursuers = GetActiveSquads(pursuingSide).ToList();
            HashSet<int> pursuedSquadIds = _pursuitTargetsBySquad
                .Where(pair => pursuers.Any(squad => squad.Id == pair.Key))
                .Select(pair => pair.Value)
                .ToHashSet();
            foreach (BattleSquad withdrawing in GetActiveSquads(withdrawingSide).ToList())
            {
                List<BattleEscapeRules.Threat> threats = pursuers.Select(pursuer =>
                    new BattleEscapeRules.Threat(
                        pursuer.Id,
                        MinimumSquadSeparation(pursuer, withdrawing),
                        MaximumUsefulAttackRange(pursuer, withdrawing),
                        SafeSquadMove(pursuer),
                        SafeSquadMove(withdrawing)))
                    .ToList();
                BattleEscapeRules.Result result = BattleEscapeRules.Evaluate(new(
                    _currentState.TurnNumber,
                    withdrawingSide == BattleSide.Attacker,
                    withdrawing.Id,
                    pursuedSquadIds.Contains(withdrawing.Id),
                    threats));
                if (!result.Escapes) continue;

                DisengageSquad(
                    withdrawingSide,
                    withdrawing,
                    events,
                    "escaped beyond any timely enemy interception");
            }
        }

        private void ResolveContactBreak(BattleSide withdrawingSide, List<BattleEvent> events)
        {
            BattleSideState state = GetSideState(withdrawingSide);
            if (!IsWithdrawalIntent(state.Intent)) return;

            DisengageBurrowers(withdrawingSide, events);
            List<BattleSquad> withdrawing = GetActiveSquads(withdrawingSide).ToList();
            if (withdrawing.Count == 0)
            {
                CompleteWithdrawal(withdrawingSide, events);
                return;
            }

            BattleSide pursuerSide = Opposite(withdrawingSide);
            // Posture is not fixed at declaration (§7: the pursuer re-evaluates every round).
            // Casualties and wounds move both sides' speeds, and the gap the withdrawal opens
            // changes whether pressing or shooting is the better use of the turn — a pursuer
            // that started faster than its quarry and is now not, in particular, needs to stop
            // chasing and start shooting rather than trail it to the turn cap.
            if (GetSideState(pursuerSide).Intent == BattleSideIntent.Pursuing)
            {
                EvaluatePursuitResponse(withdrawingSide, events);
                // BreakOff completed the withdrawal and recorded the outcome.
                if (BattleHistory.Outcome != null) return;
            }

            List<BattleSquad> pursuers = GetActiveSquads(pursuerSide).ToList();
            PursuitPosture posture = _pursuitPostures.GetValueOrDefault(
                pursuerSide,
                PursuitPosture.BreakOff);
            BattleForceMetrics pursuerMetrics = BuildMetrics(pursuerSide);
            BattleForceMetrics withdrawalMetrics = BuildMetrics(withdrawingSide);
            float separation = MinimumSeparation(pursuerSide, withdrawingSide);
            float attackReach = MaximumOneTurnAttackReach(pursuerSide, withdrawingSide);
            float declaredPursuitSpeed = FastestDeclaredPursuitSpeed(pursuers);
            // Keep contact alive while the same projection used by BattlePursuitPlanner says a
            // worthwhile shot is available now. The squad planner may spend several stationary
            // turns converting that opportunity into a fully aimed ShootAction.
            bool pursuersHaveReasonableShot = ProjectedFollowShotTurns(
                pursuerSide,
                withdrawingSide,
                separation,
                withdrawalMetrics.SlowestMainBodySquadSpeed) == 0f;
            BattleContactRules.Result forceResult = BattleContactRules.Evaluate(new(
                _currentState.TurnNumber,
                withdrawingSide == BattleSide.Attacker,
                pursuers.Count,
                posture == PursuitPosture.BreakOff,
                IsWithdrawalIntent(GetSideState(pursuerSide).Intent),
                separation,
                attackReach,
                declaredPursuitSpeed,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                state.RearGuardSquadId.HasValue,
                0,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                PursuersAttackedRecently: pursuerMetrics.HasViableDamagingActionRecently,
                PursuersHaveReasonableShot: pursuersHaveReasonableShot));
            if (forceResult.Decision == ContactBreakResult.OrganizedForceDisengages)
            {
                CompleteWithdrawal(withdrawingSide, events);
                return;
            }

            if (state.RearGuardSquadId is int rearGuardId
                && _currentState.AllAttackerSquads.Values
                    .Concat(_currentState.AllOpposingSquads.Values)
                    .FirstOrDefault(squad => squad.Id == rearGuardId) is BattleSquad rearGuard
                && rearGuard.Status == BattleSquadStatus.Active)
            {
                foreach (BattleSquad squad in withdrawing.Where(squad => squad.Id != rearGuardId).ToList())
                {
                    float current = MinimumSquadSeparation(squad, rearGuard);
                    float start = _rearGuardStartingSeparation.GetValueOrDefault(squad.Id, current);
                    BattleContactRules.Result masked = BattleContactRules.Evaluate(new(
                        _currentState.TurnNumber,
                        withdrawingSide == BattleSide.Attacker,
                        pursuers.Count,
                        false,
                        false,
                        separation,
                        attackReach,
                        declaredPursuitSpeed,
                        squad.GetSquadMove(),
                        true,
                        Math.Max(0, current - start),
                        squad.GetSquadMove(),
                        PursuersAttackedRecently: pursuerMetrics.HasViableDamagingActionRecently,
                        PursuersHaveReasonableShot: pursuersHaveReasonableShot));
                    if (masked.Decision == ContactBreakResult.SquadDisengages)
                    {
                        DisengageSquad(withdrawingSide, squad, events, "departed behind the rear guard");
                    }
                }
            }

            if (state.RearGuardSquadId.HasValue
                && !GetActiveSquads(withdrawingSide).Any(squad => squad.Id == state.RearGuardSquadId.Value))
            {
                state.RearGuardSquadId = null;
                state.Intent = BattleSideIntent.FightingWithdrawal;
                _rearGuardStartingSeparation.Clear();
            }
        }

        private void TryAssignRearGuard(BattleSide withdrawingSide, List<BattleEvent> events)
        {
            BattleSideState state = GetSideState(withdrawingSide);
            BattleSide pursuerSide = Opposite(withdrawingSide);
            if (state.Intent != BattleSideIntent.FightingWithdrawal
                || state.RearGuardSquadId.HasValue
                || _pursuitPostures.GetValueOrDefault(pursuerSide) != PursuitPosture.Press)
            {
                return;
            }

            // Routing squads are removed from the rear-guard candidate set
            // (OnlyWar_TDD.md §6.6).
            List<BattleSquad> squads = GetActiveSquads(withdrawingSide)
                .Where(squad => squad.WithdrawalRole != WithdrawalRole.Routing)
                .OrderBy(squad => squad.Id)
                .ToList();
            if (squads.Count < 2) return;
            // All active friendly squads (including any routing ones) — the propagation and
            // local-outnumber morale terms read the full local picture, not just candidates.
            List<BattleSquad> friendly = GetActiveSquads(withdrawingSide).ToList();
            List<BattleSquad> enemy = GetActiveSquads(pursuerSide).ToList();
            BattleForceMetrics friendlyMetrics = BuildMetrics(withdrawingSide);
            BattleForceMetrics enemyMetrics = BuildMetrics(pursuerSide);
            float fastestPursuer = enemyMetrics.FastestPursuitSquadSpeed;
            float attackReach = MaximumOneTurnAttackReach(pursuerSide, withdrawingSide);
            // §8.2 command collapse: force disadvantage feeds the closed-form rout estimate used
            // to price a severed dependent's collapse (see EstimateRoutsIfUncovered).
            float forceDisadvantage = BattleMoraleEvaluator.ComputeForceDisadvantage(
                friendlyMetrics.CurrentBattleValue,
                enemyMetrics.CurrentBattleValue,
                friendlyMetrics.BattleValueLostPreviousTwoRounds,
                enemyMetrics.BattleValueLostPreviousTwoRounds);
            List<WithdrawalForecast.SquadGeometry> geometry = squads.Select(squad =>
            {
                float squadEgo = SquadEgo(squad);
                bool provides = squad.SquadProvidesSynapse;
                // A squad needs coverage iff it neither provides synapse nor clears the Ego gate —
                // the same "independent-willed" definition force generation uses (§9).
                bool depends = !provides && squadEgo < MoraleConstants.RearGuardEgoThreshold;
                bool providesCommand = squad.SquadProvidesCommandAura;
                float commandAura = CommandAuraSupport(squad, withdrawingSide);
                // §4.3/§8.2 second consumer: only a squad CURRENTLY steadied by a living HQ has
                // support to lose in a branch. Synapse dependents are priced by the synapse path
                // (what the branch strips from them is the check skip, not a stress modifier), so
                // the two dependent sets stay disjoint; cross-aura coupling (a Hive Tyrant that
                // is both synapse provider and HQ) is not chased — the §8.2 one-level cap applies
                // to aura interactions too, and each verdict reads the squad's live state for the
                // other aura.
                bool dependsOnCommand = !provides && !depends && !providesCommand && commandAura > 0f;
                return new WithdrawalForecast.SquadGeometry(
                    squad.Id,
                    squad.AbleSoldiers.Count,
                    CurrentBattleValue(squad),
                    MinimumSquadToForceSeparation(squad, enemy),
                    squad.GetSquadMove(),
                    provides,
                    depends,
                    // Precompute the RNG-free rout verdict once per dependent: what §4.2 severance
                    // produces if this squad loses its provider this turn (command aura at its
                    // live value).
                    depends && EstimateRoutsAtOrdinaryMorale(
                        squad, friendly, enemy, forceDisadvantage, commandAura),
                    providesCommand,
                    dependsOnCommand,
                    // The every-HQ-lost branch verdict: support replaced by the loss term, per
                    // the stateless reading in CommandAuraEvaluator.
                    dependsOnCommand && EstimateRoutsAtOrdinaryMorale(
                        squad, friendly, enemy, forceDisadvantage,
                        -MoraleConstants.CommandLossStress));
            }).ToList();
            WithdrawalForecast.Projection baseline = WithdrawalForecast.ProjectOpenGround(
                geometry, fastestPursuer, attackReach);
            float closest = geometry.Min(item => item.CurrentEnemySeparation);
            List<WithdrawalForecast.Candidate> candidates = squads.Select(squad =>
            {
                WithdrawalForecast.SquadGeometry item = geometry.First(value => value.SquadId == squad.Id);
                WithdrawalForecast.Projection projection = WithdrawalForecast.ProjectOpenGround(
                    geometry,
                    fastestPursuer,
                    attackReach,
                    rearGuardSquadId: squad.Id,
                    rearGuardDelayTurns: 1);
                bool exposed = item.CurrentEnemySeparation <= closest + 0.001f;
                bool intercept = item.CurrentEnemySeparation <= fastestPursuer + attackReach;
                float delay = squad.AbleSoldiers.Count + squad.GetAverageArmor()
                    + squad.AbleSoldiers.Sum(soldier => soldier.EquippedRangedWeapons.Count);
                return new WithdrawalForecast.Candidate(
                    squad.Id,
                    exposed,
                    squad.IsInMelee,
                    intercept,
                    !exposed && !intercept,
                    squads.Count - 1,
                    item.CurrentEnemySeparation,
                    delay,
                    projection,
                    SquadEgo: SquadEgo(squad),
                    IsShaken: squad.MoraleState == MoraleState.Shaken,
                    // The live planner holds one squad while its providers withdraw with the main
                    // body, so a covered dependent's coverage always lapses mid-hold (§7). This
                    // arm stays false until composite rear guards (§12); Warriors pass on Ego.
                    WillRemainSynapseCoveredWhileHolding: false);
            }).ToList();
            WithdrawalForecast.Result result = WithdrawalForecast.Evaluate(new(
                _currentState.TurnNumber,
                withdrawingSide == BattleSide.Attacker,
                baseline,
                candidates));
            if (result.SelectedSquadId is not int selectedId) return;

            state.Intent = BattleSideIntent.RearGuardWithdrawal;
            state.RearGuardSquadId = selectedId;
            state.CoveringSquadId = null;
            BattleSquad guard = squads.First(squad => squad.Id == selectedId);
            guard.WithdrawalRole = WithdrawalRole.RearGuard;
            foreach (BattleSquad squad in squads.Where(squad => squad.Id != selectedId))
            {
                _rearGuardStartingSeparation[squad.Id] = MinimumSquadSeparation(squad, guard);
            }
            events.Add(new BattleEvent(
                BattleEventType.RearGuardAssigned,
                _currentState.TurnNumber,
                withdrawingSide,
                selectedId,
                squads.Where(squad => squad.Id != selectedId).Select(squad => squad.Id),
                $"{guard.Name} was assigned as rear guard."));
        }

        private BattleForceMetrics BuildMetrics(BattleSide side)
        {
            List<BattleSquad> squads = GetActiveSquads(side).ToList();
            List<BattleSoldier> soldiers = squads.SelectMany(squad => squad.AbleSoldiers).ToList();
            int current = soldiers.Sum(soldier => soldier.Soldier.Template.BattleValue);
            Queue<int> history = _battleValueHistory[side];
            int prior = history.Count > 0 ? history.Peek() : current;
            float fastest = squads.Select(SafeSquadMove).DefaultIfEmpty(0).Max();
            float slowest = squads.Select(SafeSquadMove).DefaultIfEmpty(0).Min();
            int cover = squads.Count(squad => !squad.IsInMelee
                && squad.AbleSoldiers.Any(soldier => soldier.EquippedRangedWeapons.Count > 0));
            return new BattleForceMetrics(
                GetSideState(side).StartingBattleValue,
                current,
                Math.Max(0, prior - current),
                soldiers.Count,
                fastest,
                slowest,
                cover,
                squads.Any(squad => squad.IsInMelee),
                _damageActionHistory[side].Any(value => value),
                soldiers.Count > 0);
        }

        private void DisengageBurrowers(BattleSide side, List<BattleEvent> events)
        {
            foreach (BattleSquad squad in GetActiveSquads(side).Where(squad => squad.CanBurrow).ToList())
            {
                DisengageSquad(side, squad, events, "used its burrowing capability to disengage");
            }
        }

        private void CompleteWithdrawal(BattleSide side, List<BattleEvent> events)
        {
            BattleSide holder = Opposite(side);
            // Read intent before DisengageForce overwrites it: a side whose every squad broke
            // (BattleSideIntent.Rout) records the typed Rout end reason, not Withdrawal.
            bool wasRouting = GetSideState(side).Intent == BattleSideIntent.Rout;
            DisengageForce(side, events, $"{SideName(side)} broke contact.");
            GetSideState(holder).Intent = BattleSideIntent.Engaged;
            _pursuitPostures.Remove(holder);
            BattleHistory.Outcome = BuildOutcome(
                wasRouting ? BattleEndReason.Rout : BattleEndReason.Withdrawal,
                holder);
        }

        private static float FastestDeclaredPursuitSpeed(
            IReadOnlyCollection<BattleSquad> pursuers)
        {
            return pursuers
                .Where(squad => squad.LastEngagementOptionKind is
                    EngagementOptionKind.StepForward
                    or EngagementOptionKind.JogToward
                    or EngagementOptionKind.RunToward
                    or EngagementOptionKind.CloseToContact)
                .Select(squad => squad.AbleSoldiers
                    .Select(soldier => soldier.CurrentSpeed)
                    .DefaultIfEmpty(0)
                    .Min())
                .DefaultIfEmpty(0)
                .Max();
        }

        private void DisengageForce(BattleSide side, List<BattleEvent> events, string description)
        {
            foreach (BattleSquad squad in GetActiveSquads(side).ToList())
            {
                DisengageSquad(side, squad, events, description);
            }
            GetSideState(side).Intent = BattleSideIntent.Disengaged;
            events.Add(new BattleEvent(
                BattleEventType.ForceDisengaged,
                _currentState.TurnNumber,
                side,
                null,
                GetAllSquads(side).Where(squad => squad.Status == BattleSquadStatus.Disengaged)
                    .Select(squad => squad.Id),
                description));
        }

        private void DisengageSquad(
            BattleSide side,
            BattleSquad squad,
            List<BattleEvent> events,
            string reason)
        {
            if (squad.Status != BattleSquadStatus.Active) return;
            foreach (BattleSoldier soldier in squad.AbleSoldiers.ToList())
            {
                _grid.RemoveSoldier(soldier.Soldier.Id);
            }
            _currentState.DisengageSquad(squad);
            events.Add(new BattleEvent(
                BattleEventType.SquadDisengaged,
                _currentState.TurnNumber,
                side,
                squad.Id,
                null,
                $"{squad.Name} {reason}."));
        }

        private void EnsureTerminalOutcome()
        {
            if (BattleHistory.Outcome != null) return;
            bool attackerActive = _currentState.ActiveAttackerSquads.Count > 0;
            bool opposingActive = _currentState.ActiveOpposingSquads.Count > 0;
            bool attackerDisengaged = _currentState.AllAttackerSquads.Values
                .Any(squad => squad.Status == BattleSquadStatus.Disengaged);
            bool opposingDisengaged = _currentState.AllOpposingSquads.Values
                .Any(squad => squad.Status == BattleSquadStatus.Disengaged);
            if (!attackerActive && !opposingActive && attackerDisengaged && opposingDisengaged)
            {
                BattleHistory.Outcome = BuildOutcome(BattleEndReason.MutualDisengagement, null);
                return;
            }

            BattleSide? holder = attackerActive
                ? BattleSide.Attacker
                : opposingActive ? BattleSide.Opposing : null;
            BattleEndReason reason = attackerDisengaged || opposingDisengaged
                ? BattleEndReason.Withdrawal
                : BattleEndReason.Annihilation;
            BattleHistory.Outcome = BuildOutcome(reason, holder);
        }

        private BattleOutcome BuildOutcome(BattleEndReason reason, BattleSide? holder)
        {
            List<BattleSquad> squads = _currentState.AllAttackerSquads.Values
                .Concat(_currentState.AllOpposingSquads.Values)
                .ToList();
            return new BattleOutcome(
                reason,
                holder,
                squads.Where(squad => squad.Status == BattleSquadStatus.Disengaged).Select(squad => squad.Id),
                squads.Where(squad => squad.Status == BattleSquadStatus.Eliminated).Select(squad => squad.Id),
                squads.Where(squad => squad.WithdrawalRole == WithdrawalRole.Routing)
                    .Select(squad => squad.Id)
                    .Concat(_everRoutedSquadIds),
                squads.Where(squad => squad.WithdrawalRole == WithdrawalRole.RearGuard
                    || GetSideState(BattleSide.Attacker).RearGuardSquadId == squad.Id
                    || GetSideState(BattleSide.Opposing).RearGuardSquadId == squad.Id)
                    .Select(squad => squad.Id));
        }

        private IReadOnlyCollection<BattleSquad> GetActiveSquads(BattleSide side) =>
            side == BattleSide.Attacker
                ? _currentState.ActiveAttackerSquads.Values.ToList()
                : _currentState.ActiveOpposingSquads.Values.ToList();

        private IReadOnlyCollection<BattleSquad> GetAllSquads(BattleSide side) =>
            side == BattleSide.Attacker
                ? _currentState.AllAttackerSquads.Values.ToList()
                : _currentState.AllOpposingSquads.Values.ToList();

        private BattleSideState GetSideState(BattleSide side) =>
            side == BattleSide.Attacker ? _currentState.AttackerSide : _currentState.OpposingSide;

        private static BattleSide Opposite(BattleSide side) =>
            side == BattleSide.Attacker ? BattleSide.Opposing : BattleSide.Attacker;

        private static bool IsWithdrawalIntent(BattleSideIntent intent) =>
            intent is BattleSideIntent.FightingWithdrawal
                or BattleSideIntent.RearGuardWithdrawal
                or BattleSideIntent.Rout;

        private bool IsSoldierOnSide(int soldierId, BattleSide side) => GetAllSquads(side)
            .SelectMany(squad => squad.Soldiers)
            .Any(soldier => soldier.Soldier.Id == soldierId);

        private int CurrentBattleValue(BattleSide side) => GetActiveSquads(side)
            .Sum(CurrentBattleValue);

        private static int CurrentBattleValue(BattleSquad squad) => squad.AbleSoldiers
            .Sum(soldier => soldier.Soldier.Template.BattleValue);

        private static float SafeSquadMove(BattleSquad squad) =>
            squad.AbleSoldiers.Count == 0 ? 0 : squad.GetSquadMove();

        private float MinimumSeparation(BattleSide first, BattleSide second)
        {
            List<BattleSquad> secondSquads = GetActiveSquads(second).ToList();
            return GetActiveSquads(first)
                .Select(squad => MinimumSquadToForceSeparation(squad, secondSquads))
                .DefaultIfEmpty(float.MaxValue)
                .Min();
        }

        private static float MinimumSquadToForceSeparation(
            BattleSquad squad,
            IReadOnlyCollection<BattleSquad> force)
        {
            return force.Select(other => MinimumSquadSeparation(squad, other))
                .DefaultIfEmpty(float.MaxValue)
                .Min();
        }

        private static float MinimumSquadSeparation(BattleSquad first, BattleSquad second)
        {
            return first.AbleSoldiers.SelectMany(a => second.AbleSoldiers.Select(b =>
            {
                float dx = a.TopLeft.Value.Item1 - b.TopLeft.Value.Item1;
                float dy = a.TopLeft.Value.Item2 - b.TopLeft.Value.Item2;
                return MathF.Sqrt((dx * dx) + (dy * dy));
            })).DefaultIfEmpty(float.MaxValue).Min();
        }

        /// <summary>
        /// The average defensive profile of a side, used to ask how far the other side can
        /// usefully shoot at it. Soldier-weighted across squads so a big weak formation does not
        /// count for the same as a single elite one.
        /// </summary>
        private (float Size, float Armor, float Constitution, float Evasion) ForceTargetProfile(
            BattleSide side)
        {
            List<BattleSoldier> soldiers = GetActiveSquads(side)
                .SelectMany(squad => squad.AbleSoldiers)
                .ToList();
            return TargetProfile(soldiers);
        }

        private static (float Size, float Armor, float Constitution, float Evasion) TargetProfile(
            IReadOnlyCollection<BattleSoldier> soldiers)
        {
            if (soldiers.Count == 0) return (0, 0, 0, 0);
            return (
                (float)soldiers.Average(soldier => soldier.Soldier.Size),
                (float)soldiers.Average(soldier => soldier.Armor?.Template.ArmorProvided ?? 0),
                (float)soldiers.Average(soldier => soldier.Soldier.Constitution),
                (float)soldiers.Average(soldier => soldier.Soldier.Template.Species.RangedEvasion));
        }

        private static float MaximumUsefulAttackRange(
            BattleSquad pursuer,
            BattleSquad target)
        {
            (float size, float armor, float constitution, float evasion) =
                TargetProfile(target.AbleSoldiers);
            if (size <= 0) return 0;
            float ranged = pursuer.AbleSoldiers
                .Select(soldier => BattleModifiersUtil.CalculateOptimalDistance(
                    soldier, size, armor, constitution, evasion))
                .DefaultIfEmpty(0)
                .Max();
            // A melee-only pursuer still has an attack envelope once relative movement brings it
            // beside the quarry. The projected turns consume movement; this is the final contact
            // allowance, not an extra turn of free movement.
            return Math.Max(BattleContactRules.MeleeContactAllowance, ranged);
        }

        /// <summary>
        /// How far <paramref name="side"/> can actually hurt <paramref name="targetSide"/>: the
        /// best of its soldiers' optimal engagement distances, which is the lesser of "can I hit at
        /// this range" and "can I wound it at this range".
        ///
        /// Deliberately NOT the longest weapon's maximum range. One Heavy Bolter or Sniper Rifle in
        /// a force sets that to 1600 yards, which made every force look able to shoot from
        /// anywhere: CONTACT_EVAL logged attack_reach=1600 every turn so §8's mobility break could
        /// never fire, and ProjectedFollowShotTurns reported a shot was available at ranges where
        /// nothing was worth firing. Between them they let the Xibarrus Nu ambush run 257 turns
        /// with the pursuers landing nothing on 167 of them. Reusing CalculateOptimalDistance also
        /// keeps these force-level gates agreeing with the per-soldier engagement model that
        /// decides what the squads actually do.
        /// </summary>
        private float WorthwhileRangedReach(BattleSide side, BattleSide targetSide)
        {
            (float size, float armor, float constitution, float evasion) =
                ForceTargetProfile(targetSide);
            if (size <= 0) return 0;
            return GetActiveSquads(side).SelectMany(squad => squad.AbleSoldiers)
                .Select(soldier => BattleModifiersUtil.CalculateOptimalDistance(
                    soldier, size, armor, constitution, evasion))
                .DefaultIfEmpty(0)
                .Max();
        }

        private float MaximumOneTurnAttackReach(BattleSide side, BattleSide targetSide)
        {
            float meleeReach = GetActiveSquads(side).SelectMany(squad => squad.AbleSoldiers)
                .Select(soldier => soldier.GetMoveSpeed() + 1)
                .DefaultIfEmpty(0)
                .Max();
            return Math.Max(meleeReach, WorthwhileRangedReach(side, targetSide));
        }

        /// <summary>
        /// Turns of FOLLOWING before this side has a shot worth taking at a quarry that is running
        /// away, or null when following will never produce one.
        ///
        /// <para>The quarry's own speed is netted out, exactly as <c>pressTurns</c> nets it out of
        /// the intercept projection. Following jogs at <see cref="SoldierMovementPlanner.JogSpeedMultiplier"/>
        /// of a run so the guns stay usable, so a quarry running flat out opens the range at half a
        /// run against a follower and no number of turns closes it. Dividing by the raw jog speed
        /// instead reported a finite arrival for a chase that never arrives, and
        /// <c>BattlePursuitPlanner.SelectNormal</c> compares this number DIRECTLY against a press
        /// projection that does net the quarry out -- so a stern chase read as "following shoots
        /// first" on a comparison where one side had paid for the quarry's flight and the other had
        /// not.</para>
        /// </summary>
        private float? ProjectedFollowShotTurns(
            BattleSide side,
            BattleSide targetSide,
            float separation,
            float quarrySpeed)
        {
            float reach = WorthwhileRangedReach(side, targetSide);
            // No range at which this force can both hit and hurt that one: there is no follow shot
            // to wait for, however long its guns nominally are.
            if (reach <= 0) return null;
            if (separation <= reach) return 0;
            float jogSpeed = BuildMetrics(side).FastestPursuitSquadSpeed
                * SoldierMovementPlanner.JogSpeedMultiplier;
            float closingRate = jogSpeed - Math.Max(0, quarrySpeed);
            // A follower that cannot out-pace the withdrawal has no shot to wait for either: the
            // range is opening, not closing. Null rather than infinity, which is the same answer
            // this method already gives for "there is no follow shot to wait for" and which routes
            // a Normal pursuer to Press -- running is the only posture that can still make contact.
            return closingRate <= 0 ? null : (separation - reach) / closingRate;
        }

        private string SideName(BattleSide side) =>
            GetAllSquads(side).Any(squad => squad.IsPlayerAligned)
                ? "Player force"
                : "Opposing force";

        private void LogCoverAssignment(
            BattleSide side,
            BattleSideState state,
            BattleForcePlanner.CoverAssignment assignment)
        {
            BattleDecisionTrace trace = new("COVER_ASSIGN",
            [
                BattleDecisionTrace.Field("turn", _currentState.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("heading", state.WithdrawalHeading),
                BattleDecisionTrace.Field("selected_squad", assignment.SquadId),
                BattleDecisionTrace.Field("rotated", assignment.Rotated),
                BattleDecisionTrace.Field("reason", assignment.Reason),
                BattleDecisionTrace.Field("candidates", string.Join(",", assignment.Candidates
                    .OrderBy(candidate => candidate.SquadId)
                    .Select(candidate => $"{candidate.SquadId}:{candidate.NearestEnemyDistance:0.###}:{candidate.RangedCoverEligible}")))
            ]);
            BattleLog.Write(trace.Render());
            if (assignment.SquadId.HasValue
                && (assignment.Rotated || assignment.Reason == "farthest_eligible"))
            {
                _turnEvents.Add(new BattleEvent(
                    BattleEventType.CoverAssigned,
                    _currentState.TurnNumber,
                    side,
                    assignment.SquadId,
                    null,
                    $"{SideName(side)} assigned a covering squad."));
            }
        }

        private void CleanupAtEndOfTurn()
        {
            foreach (BattleSoldier soldier in _casualtyMap.Values)
            {
                RemoveSoldier(soldier);
            }

            foreach (BattleSquad squad in _currentState.AttackerSquads.Values)
            {
                UpdateSquadMeleeStatus(squad);
            }
            foreach (BattleSquad squad in _currentState.OpposingSquads.Values)
            {
                UpdateSquadMeleeStatus(squad);
            }
        }

        private void UpdateSquadMeleeStatus(BattleSquad squad)
        {
            bool atLeastOneSoldierInMelee = false;
            foreach (BattleSoldier soldier in squad.AbleSoldiers)
            {
                soldier.IsInMelee = _grid.IsAdjacentToEnemy(soldier.Soldier.Id);
                if (soldier.IsInMelee) atLeastOneSoldierInMelee = true;
            }
            squad.IsInMelee = atLeastOneSoldierInMelee;
        }

        private static void Log(bool isMessageVerbose, string text)
        {
            BattleLog.Write(text);
        }

        private void RemoveSoldier(BattleSoldier soldier)
        {
            BattleSquad squad = soldier.BattleSquad;
            soldier.BattleSquad.RemoveSoldier(soldier);
            _grid.RemoveSoldier(soldier.Soldier.Id);

            if (squad.AbleSoldiers.Count == 0)
            {
                _currentState.RemoveSquad(squad);
            }
        }
    }
}
