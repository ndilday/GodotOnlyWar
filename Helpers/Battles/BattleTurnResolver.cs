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
        private readonly BattleMoraleService _moraleService;
        private readonly BattleRoundMetrics _roundMetrics;
        private readonly BattleWithdrawalService _withdrawalService;
        private readonly BattleActionPlanningCoordinator _planningCoordinator;
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
            if (execution.Aftermath.PlayerSink is IPlayerNarrativeEventSink narrativeSink)
            {
                narrativeSink.BeginBattle(_aftermathContext.BattleEventContext);
            }
            _aftermathPolicy = BattleAftermathPolicyFactory.Create(_aftermathContext);

            _currentState = new BattleState(
                attackerBattleSquads.ToDictionary(bs => bs.Id, bs => bs),
                opposingBattleSquads.ToDictionary(os => os.Id, os => os),
                attackerProfile,
                opposingProfile);
            BattleHistory.Turns.Add(new BattleTurn(_currentState, new List<IAction>()));
            _moraleService = new BattleMoraleService(
                _currentState,
                _grid,
                _execution,
                attackerBattleSquads.Concat(opposingBattleSquads));
            _roundMetrics = new BattleRoundMetrics(_currentState);
            _withdrawalService = new BattleWithdrawalService(
                _currentState,
                _grid,
                _execution.Rules,
                _roundMetrics,
                _moraleService);
            _planningCoordinator = new BattleActionPlanningCoordinator(
                _currentState,
                _grid,
                _execution);

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

            SeedPreparedAttackerAim();
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

        // An ambushing side and an assassination force open fire with weapons already trained on
        // the kill zone. Before the first turn is planned, pre-seed every ranged attacker to the
        // full aim bonus so the opening volley is fully aimed on turn one instead of the side
        // spending turn one lining up shots. Only sides flagged BattleRole.Ambusher or
        // BattleRole.AssassinationAttacker are seeded; ordinary engagements are unchanged. The
        // seeding scan draws no battle RNG, so it does not perturb the seeded action stream -- but
        // the seeded aim does change turn-one planning, so these battles diverge from pre-change
        // baselines by design.
        private void SeedPreparedAttackerAim()
        {
            BattleSide? preparedSide =
                IsPreparedAttacker(_currentState.AttackerSide.BattleRole) ? BattleSide.Attacker
                : IsPreparedAttacker(_currentState.OpposingSide.BattleRole) ? BattleSide.Opposing
                : (BattleSide?)null;
            if (preparedSide == null) return;

            // Materialize the lazy per-soldier/per-squad views the target scan reads, exactly as a
            // planning pass would, so the seeding scan sees the same consistent state.
            _planningCoordinator.PrepareParallelPlanningState();
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
            foreach (BattleSquad squad in GetActiveSquads(preparedSide.Value))
            {
                planner.SeedAmbushAim(squad);
            }
        }

        private static bool IsPreparedAttacker(BattleRole role) =>
            role is BattleRole.Ambusher or BattleRole.AssassinationAttacker;

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
            foreach (RangedWeapon weapon in _currentState.AllAttackerSquads.Values
                         .Concat(_currentState.AllOpposingSquads.Values)
                         .SelectMany(squad => squad.Soldiers)
                         .SelectMany(soldier => soldier.RangedWeapons)
                         .Distinct())
            {
                weapon.AdvanceRecovery();
            }

            Log(false, "Turn " + _currentState.TurnNumber.ToString());

            _moraleService.SnapshotTurnStart();

            List<IAction> moveSegmentActions = [];
            List<IAction> shootSegmentActions = [];
            List<IAction> meleeSegmentActions = [];
            _turnEvents.Clear();
            List<string> log = BattleLog.IsEnabled ? [] : null;
            Action<string> logSink = log == null ? null : log.Add;
            long planningStarted = Stopwatch.GetTimestamp();
            Plan(shootSegmentActions, moveSegmentActions, meleeSegmentActions, logSink);
            ApplyPendingMobLeaderSuppression(
                shootSegmentActions, moveSegmentActions, meleeSegmentActions, logSink);
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
            _roundMetrics.RecordRound(executedActions);
            // Terminal casualties take precedence over contact resolution. A force that was
            // already withdrawing can still kill the last opposing squad during this turn; if
            // the opponent is gone, that is an annihilation victory rather than a withdrawal.
            // Running escape/contact logic first would disengage the surviving force and make the
            // subsequent terminal check report Withdrawal (or Rout) instead.
            if (_currentState.ActiveAttackerSquads.Count > 0
                && _currentState.ActiveOpposingSquads.Count > 0)
            {
                ApplyWithdrawalTerminalRequest(
                    _withdrawalService.ResolveUnpursuedWithdrawalEscapes(events));
                if (BattleHistory.Outcome == null)
                {
                    ApplyWithdrawalTerminalRequest(
                        _withdrawalService.ResolveContactBreaks(events));
                }
            }
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
            float separation = _withdrawalService.CurrentMinimumSeparation(
                BattleSide.Attacker,
                BattleSide.Opposing);
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
            BattleForceMetrics attackerMetrics = _roundMetrics.BuildMetrics(BattleSide.Attacker);
            BattleForceMetrics opposingMetrics = _roundMetrics.BuildMetrics(BattleSide.Opposing);
            return $"Battle in {_region?.Name} was inert for {InertTurnThreshold} consecutive "
                + $"turns at turn {_currentState.TurnNumber}: no casualties on either side and no "
                + $"change in separation. "
                + $"{_aftermathContext.FirstSideFaction?.Name} "
                + $"({attackerMetrics.AbleSoldierCount} able, bv "
                + $"{attackerMetrics.CurrentBattleValue}) vs "
                + $"{_aftermathContext.SecondSideFaction?.Name} "
                + $"({opposingMetrics.AbleSoldierCount} able, bv "
                + $"{opposingMetrics.CurrentBattleValue}); separation "
                + $"{_withdrawalService.CurrentMinimumSeparation(
                    BattleSide.Attacker, BattleSide.Opposing):F1}. "
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
                BattleForceMetrics attackerMetrics = _roundMetrics.BuildMetrics(BattleSide.Attacker);
                BattleForceMetrics opposingMetrics = _roundMetrics.BuildMetrics(BattleSide.Opposing);
                float separation = _withdrawalService.CurrentMinimumSeparation(
                    BattleSide.Attacker,
                    BattleSide.Opposing);
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
            CommitMissionWeaponState();
            _aftermathPolicy.OnBattleCompleted(_currentState);
            // Closes the per-battle log stream before control returns to the campaign turn, so
            // whatever the caller does next is not filed under this battle. Anything the aftermath
            // policy logs above still belongs to it.
            BattleLog.EndBattle();
            OnBattleComplete?.Invoke(this, BattleHistory);
        }

        private void CommitMissionWeaponState()
        {
            foreach (BattleSquad missionSquad in _aftermathContext.ParticipatingSquads)
            {
                if (_currentState.AllAttackerSquads.TryGetValue(
                        missionSquad.Id, out BattleSquad attackerSnapshot))
                {
                    missionSquad.CommitEquipmentStateFrom(attackerSnapshot);
                }
                else if (_currentState.AllOpposingSquads.TryGetValue(
                             missionSquad.Id, out BattleSquad opposingSnapshot))
                {
                    missionSquad.CommitEquipmentStateFrom(opposingSnapshot);
                }
            }
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
            List<BattleSquad> attacker = GetActiveSquads(BattleSide.Attacker)
                .OrderBy(squad => squad.Id)
                .ToList();
            List<BattleSquad> opposing = GetActiveSquads(BattleSide.Opposing)
                .OrderBy(squad => squad.Id)
                .ToList();
            Dictionary<int, EngagementRoleConstraint> constraints = [];
            _withdrawalService.BuildRoleConstraints(
                BattleSide.Attacker, attacker, opposing, constraints, _turnEvents);
            _withdrawalService.BuildRoleConstraints(
                BattleSide.Opposing, opposing, attacker, constraints, _turnEvents);

            BattlePlanningPassResult pass = _planningCoordinator.PlanDecisions(
                attacker,
                opposing,
                constraints,
                shootSegmentActions,
                moveSegmentActions,
                meleeSegmentActions,
                log);
            _withdrawalService.ReplaceCurrentTurnPursuitPairings(pass.PursuitPairings);
            _planningCoordinator.DeclareAndBuildActions(pass);
        }

        private void ApplyPendingMobLeaderSuppression(
            List<IAction> shootActions,
            List<IAction> moveActions,
            List<IAction> meleeActions,
            Action<string> log)
        {
            foreach (BattleSquad squad in GetActiveSquads(BattleSide.Attacker)
                .Concat(GetActiveSquads(BattleSide.Opposing))
                .Where(squad => FactionCapabilities.HasMobMentality(squad?.Faction))
                .Where(candidate => candidate.MobSuppressionPending)
                .OrderBy(candidate => candidate.Id))
            {
                squad.MobSuppressionPending = false;
                squad.MobSuppressionCommitted = true;
                BattleSoldier leader = squad.SquadLeader;
                if (leader == null) continue;

                // The commitment consumes the leader's whole round. Remove any ordinary plan
                // before inserting the coercion strike; the strike is still a normal melee action,
                // so wounds, deaths, XP and replay state use the ordinary combat machinery.
                shootActions.RemoveAll(action => action.ActorId == leader.Soldier.Id);
                moveActions.RemoveAll(action => action.ActorId == leader.Soldier.Id);
                meleeActions.RemoveAll(action => action.ActorId == leader.Soldier.Id);

                BattleSoldier target = squad.AbleSoldiers
                    .Where(candidate => candidate != leader)
                    .Where(candidate => _grid.GetDistanceBetweenSoldiers(
                        leader.Soldier.Id, candidate.Soldier.Id) <= 1.5f)
                    .OrderBy(candidate => candidate.Soldier.Id)
                    .FirstOrDefault();
                MeleeWeapon weapon = leader.EquippedMeleeWeapons.FirstOrDefault()
                    ?? leader.MeleeWeapons.FirstOrDefault();
                if (target == null || weapon == null)
                {
                    log?.Invoke($"{squad.Name}'s leader spends the round suppressing the mob, but has no nearby target.");
                    continue;
                }

                meleeActions.Add(new MeleeAttackAction(
                    leader,
                    target,
                    weapon,
                    didMove: false,
                    log,
                    _execution.Random,
                    _execution.Rules.MeleeWeaponTemplates));
                _turnEvents.Add(new BattleEvent(
                    BattleEventType.MobLeaderSuppressionAttack,
                    _currentState.TurnNumber,
                    _currentState.AttackerSquads.ContainsKey(squad.Id)
                        ? BattleSide.Attacker : BattleSide.Opposing,
                    squad.Id,
                    new[] { target.BattleSquad.Id },
                    $"{leader.Soldier.Name} attacks {target.Soldier.Name} to suppress the mob."));
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

        // OnlyWar_TDD.md §6.6 Both sides are evaluated from the same post-round
        // physical state; propagation (§5.1) reads the turn-start routing snapshot, so iteration
        // order does not change results and a rout this turn only pressures neighbours next turn.
        private void EvaluateMorale(List<BattleEvent> events)
        {
            // Metrics for both sides are captured before either side's outcomes apply, and the
            // propagation term reads the turn-start routing snapshot (§5.1), so evaluating the
            // sides in sequence cannot change either side's results.
            BattleForceMetrics attackerMetrics = _roundMetrics.BuildMetrics(BattleSide.Attacker);
            BattleForceMetrics opposingMetrics = _roundMetrics.BuildMetrics(BattleSide.Opposing);
            BattleSideRoutedTransition attackerTransition = _moraleService.EvaluateSide(
                BattleSide.Attacker,
                attackerMetrics,
                opposingMetrics,
                events);
            if (attackerTransition != null)
            {
                ApplyWithdrawalTerminalRequest(
                    _withdrawalService.EvaluatePursuitResponse(attackerTransition.Side, events));
            }
            if (BattleHistory.Outcome == null)
            {
                BattleSideRoutedTransition opposingTransition = _moraleService.EvaluateSide(
                    BattleSide.Opposing,
                    opposingMetrics,
                    attackerMetrics,
                    events);
                if (opposingTransition != null)
                {
                    ApplyWithdrawalTerminalRequest(
                        _withdrawalService.EvaluatePursuitResponse(opposingTransition.Side, events));
                }
            }
        }

        private void EvaluateContinuation(List<BattleEvent> events)
        {
            BattleForceMetrics attacker = _roundMetrics.BuildMetrics(BattleSide.Attacker);
            BattleForceMetrics opposing = _roundMetrics.BuildMetrics(BattleSide.Opposing);
            ApplyWithdrawalTerminalRequest(
                _withdrawalService.EvaluateContinuation(attacker, opposing, events));
        }

        private void ApplyWithdrawalTerminalRequest(BattleTerminalRequest request)
        {
            if (request == null || BattleHistory.Outcome != null) return;
            BattleHistory.Outcome = BuildOutcome(request.Reason, request.SideHoldingField);
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
            bool attackerAnnihilated = !attackerActive
                && _currentState.AllAttackerSquads.Values.All(
                    squad => squad.Status == BattleSquadStatus.Eliminated);
            bool opposingAnnihilated = !opposingActive
                && _currentState.AllOpposingSquads.Values.All(
                    squad => squad.Status == BattleSquadStatus.Eliminated);
            BattleEndReason reason = attackerAnnihilated || opposingAnnihilated
                ? BattleEndReason.Annihilation
                : attackerDisengaged || opposingDisengaged
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
                    .Concat(_moraleService.EverRoutedSquadIds),
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
            foreach (BattleSquad squad in _currentState.AttackerSquads.Values
                .Concat(_currentState.OpposingSquads.Values))
            {
                squad.MobSuppressionCommitted = false;
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
