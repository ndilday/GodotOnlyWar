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
        public BattleHistory BattleHistory { get; private set; }
        private BattleState _currentState;
        private readonly Dictionary<BattleSide, PursuitPosture> _pursuitPostures = [];
        private readonly Dictionary<BattleSide, Queue<int>> _battleValueHistory = [];
        private readonly Dictionary<BattleSide, Queue<bool>> _damageActionHistory = [];
        private readonly Dictionary<int, float> _rearGuardStartingSeparation = [];
        // Morale (Design/Active/MoraleAndRout.md §5). Starting able strength per squad drives the
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

        internal TimeSpan PlanningElapsed => _planningElapsed;

        public event EventHandler<BattleHistory> OnBattleComplete;

        // A battle's only natural end is one side's annihilation. Two forces that cannot resolve
        // would otherwise spin the caller's while loop forever, so this caps the fight.
        private const int MaxBattleTurns = 1000;

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

            GameLog.Debug(() =>
                $"Battle start in {_region?.Name}: {_aftermathContext.FirstSideStartingSoldierCount} "
                + $"{_aftermathContext.FirstSideFaction.Name} vs "
                + $"{_aftermathContext.SecondSideStartingSoldierCount}  "
                + $"{_aftermathContext.SecondSideFaction.Name}");
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
            ResolveContactBreaks(events);
            if (_currentState.ActiveAttackerSquads.Count > 0
                && _currentState.ActiveOpposingSquads.Count > 0)
            {
                // Stage 6 (Design/Active/WithdrawalAndPursuit.md §11): a rout preempts the plan,
                // so the morale check runs BEFORE the continuation/rear-guard decision.
                EvaluateMorale(events);
                if (BattleHistory.Outcome == null
                    && _currentState.ActiveAttackerSquads.Count > 0
                    && _currentState.ActiveOpposingSquads.Count > 0)
                {
                    EvaluateContinuation(events);
                }
            }

            BattleHistory.Turns.Add(new BattleTurn(_currentState, executedActions, events));
            if (_currentState.ActiveAttackerSquads.Count == 0 || _currentState.ActiveOpposingSquads.Count == 0)
            {
                EnsureTerminalOutcome();
                Log(false, "One side no longer active, battle over");
                ProcessEndOfBattle(false);
            }
            else if (_currentState.TurnNumber >= MaxBattleTurns)
            {
                Log(false, $"Battle unresolved after {MaxBattleTurns} turns; forcing disengagement");
                BattleHistory.Outcome = BuildOutcome(BattleEndReason.TurnCap, null);
                ProcessEndOfBattle(true);
            }
        }

        private void ProcessEndOfBattle(bool hitTurnCap)
        {
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
            _aftermathPolicy.OnBattleCompleted(_currentState);
            OnBattleComplete?.Invoke(this, BattleHistory);
        }

        private void Plan(List<IAction> shootSegmentActions,
                          List<IAction> moveSegmentActions,
                          List<IAction> meleeSegmentActions,
                          Action<string> log)
        {
            PrepareParallelPlanningState();
            PlanSide(BattleSide.Attacker, shootSegmentActions, moveSegmentActions, meleeSegmentActions, log);
            PlanSide(BattleSide.Opposing, shootSegmentActions, moveSegmentActions, meleeSegmentActions, log);
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

        private void PlanSide(
            BattleSide side,
            ICollection<IAction> shootActions,
            ICollection<IAction> moveActions,
            ICollection<IAction> meleeActions,
            Action<string> log)
        {
            IReadOnlyCollection<BattleSquad> allFriendly = GetActiveSquads(side);
            IReadOnlyCollection<BattleSquad> enemy = GetActiveSquads(Opposite(side));
            BattleSideState sideState = GetSideState(side);
            BattleSquadPlanner squadPlanner = new(
                _grid,
                _currentState.Soldiers,
                shootActions,
                moveActions,
                meleeActions,
                log,
                _execution.Rules.MeleeWeaponTemplates,
                _execution.Random,
                _execution.MaxPlanningDegreeOfParallelism);
            BattleForcePlanner forcePlanner = new(squadPlanner);

            // Routing squads flee through their own planner path regardless of side intent
            // (Design/Active/WithdrawalAndPursuit.md §10) and are removed from the cover
            // rotation and rear-guard candidate set — hence excluded from the force planner.
            List<BattleSquad> friendly = [];
            foreach (BattleSquad squad in allFriendly.OrderBy(squad => squad.Id))
            {
                if (squad.WithdrawalRole == WithdrawalRole.Routing)
                {
                    squadPlanner.PrepareRoutingActions(squad);
                }
                else
                {
                    friendly.Add(squad);
                }
            }
            if (friendly.Count == 0) return;

            if (sideState.Intent == BattleSideIntent.FightingWithdrawal)
            {
                BattleForcePlanner.CoverAssignment cover = forcePlanner.PrepareFightingWithdrawal(
                    sideState, friendly, enemy);
                LogCoverAssignment(side, sideState, cover);
                return;
            }
            if (sideState.Intent == BattleSideIntent.RearGuardWithdrawal)
            {
                forcePlanner.PrepareRearGuardWithdrawal(sideState, friendly, enemy);
                return;
            }
            if (sideState.Intent == BattleSideIntent.Pursuing)
            {
                forcePlanner.PreparePursuit(
                    friendly,
                    enemy,
                    _pursuitPostures.GetValueOrDefault(side, PursuitPosture.BreakOff));
                return;
            }

            foreach (BattleSquad squad in friendly)
            {
                squad.WithdrawalRole = WithdrawalRole.None;
                squadPlanner.PrepareActions(squad, friendly);
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
                action.Execute(_currentState);
                executedActions.Add(action);
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

        // Design/Active/MoraleAndRout.md §5-6. Both sides are evaluated from the same post-round
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

        // Decides the enemy's pursuit posture toward a side that has just started withdrawing
        // or routing. BreakOff completes the withdrawal immediately; otherwise the enemy's
        // intent becomes Pursuing. Shared by voluntary withdrawal and the morale rout path.
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
            BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(new(
                _currentState.TurnNumber,
                pursuingSide == BattleSide.Attacker,
                pursuing.Aggression,
                pursuitMetrics.AbleSoldierCount,
                withdrawalMetrics.AbleSoldierCount,
                pursuitMetrics.FastestPursuitSquadSpeed,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                pressTurns,
                ProjectedFollowShotTurns(pursuingSide, separation),
                withdrawerReturnsFire));
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
            events.Add(new BattleEvent(
                BattleEventType.PursuitStarted,
                _currentState.TurnNumber,
                pursuingSide,
                null,
                GetActiveSquads(withdrawingSide).Select(squad => squad.Id),
                $"{SideName(pursuingSide)} began a {result.Posture} pursuit."));
        }

        private void ResolveContactBreaks(List<BattleEvent> events)
        {
            ResolveContactBreak(BattleSide.Attacker, events);
            if (BattleHistory.Outcome == null)
            {
                ResolveContactBreak(BattleSide.Opposing, events);
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
            List<BattleSquad> pursuers = GetActiveSquads(pursuerSide).ToList();
            PursuitPosture posture = _pursuitPostures.GetValueOrDefault(
                pursuerSide,
                PursuitPosture.BreakOff);
            BattleForceMetrics pursuerMetrics = BuildMetrics(pursuerSide);
            BattleForceMetrics withdrawalMetrics = BuildMetrics(withdrawingSide);
            float separation = MinimumSeparation(pursuerSide, withdrawingSide);
            float attackReach = MaximumOneTurnAttackReach(pursuerSide);
            BattleContactRules.Result forceResult = BattleContactRules.Evaluate(new(
                _currentState.TurnNumber,
                withdrawingSide == BattleSide.Attacker,
                pursuers.Count,
                posture == PursuitPosture.BreakOff,
                IsWithdrawalIntent(GetSideState(pursuerSide).Intent),
                separation,
                attackReach,
                pursuerMetrics.FastestPursuitSquadSpeed,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                state.RearGuardSquadId.HasValue,
                0,
                withdrawalMetrics.SlowestMainBodySquadSpeed));
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
                        pursuerMetrics.FastestPursuitSquadSpeed,
                        squad.GetSquadMove(),
                        true,
                        Math.Max(0, current - start),
                        squad.GetSquadMove()));
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
            // (Design/Active/WithdrawalAndPursuit.md §10).
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
            float attackReach = MaximumOneTurnAttackReach(pursuerSide);
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

        private float MaximumOneTurnAttackReach(BattleSide side)
        {
            return GetActiveSquads(side).SelectMany(squad => squad.AbleSoldiers)
                .Select(soldier => Math.Max(
                    soldier.GetMoveSpeed() + 1,
                    soldier.EquippedRangedWeapons
                        .Select(weapon => (float)weapon.Template.MaximumRange)
                        .DefaultIfEmpty(0)
                        .Max()))
                .DefaultIfEmpty(0)
                .Max();
        }

        private float? ProjectedFollowShotTurns(BattleSide side, float separation)
        {
            float maximumRange = GetActiveSquads(side).SelectMany(squad => squad.AbleSoldiers)
                .SelectMany(soldier => soldier.EquippedRangedWeapons)
                .Select(weapon => (float)weapon.Template.MaximumRange)
                .DefaultIfEmpty(0)
                .Max();
            if (maximumRange <= 0) return null;
            if (separation <= maximumRange) return 0;
            float jogSpeed = BuildMetrics(side).FastestPursuitSquadSpeed * 0.66f;
            return jogSpeed <= 0 ? null : (separation - maximumRange) / jogSpeed;
        }

        private static string SideName(BattleSide side) =>
            side == BattleSide.Attacker ? "First side" : "Second side";

        private void LogCoverAssignment(
            BattleSide side,
            BattleSideState state,
            BattleForcePlanner.CoverAssignment assignment)
        {
            BattleDecisionTrace trace = new("COVER_ASSIGN", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("turn", _currentState.TurnNumber),
                BattleDecisionTrace.Field("side", side == BattleSide.Attacker ? "first" : "second"),
                BattleDecisionTrace.Field("heading", state.WithdrawalHeading),
                BattleDecisionTrace.Field("selected_squad", assignment.SquadId),
                BattleDecisionTrace.Field("rotated", assignment.Rotated),
                BattleDecisionTrace.Field("reason", assignment.Reason),
                BattleDecisionTrace.Field("candidates", string.Join(",", assignment.Candidates
                    .OrderBy(candidate => candidate.SquadId)
                    .Select(candidate => $"{candidate.SquadId}:{candidate.NearestEnemyDistance:0.###}:{candidate.RangedCoverEligible}")))
            });
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

        private void Log(bool isMessageVerbose, string text)
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
