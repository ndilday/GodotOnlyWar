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
        private readonly List<BattleEvent> _turnEvents = [];
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

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

            List<IAction> moveSegmentActions = [];
            List<IAction> shootSegmentActions = [];
            List<IAction> meleeSegmentActions = [];
            _turnEvents.Clear();
            List<string> log = BattleLog.IsEnabled ? [] : null;
            Action<string> logSink = log == null ? null : log.Add;
            Plan(shootSegmentActions, moveSegmentActions, meleeSegmentActions, logSink);
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
                EvaluateContinuation(events);
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
                + $"{_stopwatch.ElapsedMilliseconds}ms, started "
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
            PlanSide(BattleSide.Attacker, shootSegmentActions, moveSegmentActions, meleeSegmentActions, log);
            PlanSide(BattleSide.Opposing, shootSegmentActions, moveSegmentActions, meleeSegmentActions, log);
        }

        private void PlanSide(
            BattleSide side,
            ICollection<IAction> shootActions,
            ICollection<IAction> moveActions,
            ICollection<IAction> meleeActions,
            Action<string> log)
        {
            IReadOnlyCollection<BattleSquad> friendly = GetActiveSquads(side);
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
                _execution.Random);
            BattleForcePlanner forcePlanner = new(squadPlanner);

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

            foreach (BattleSquad squad in friendly.OrderBy(squad => squad.Id))
            {
                squad.WithdrawalRole = WithdrawalRole.None;
                squadPlanner.PrepareActions(squad);
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
            BattleSideState pursuing = GetSideState(pursuingSide);
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

            BattleForceMetrics pursuitMetrics = BuildMetrics(pursuingSide);
            BattleForceMetrics withdrawalMetrics = BuildMetrics(withdrawingSide);
            float separation = MinimumSeparation(pursuingSide, withdrawingSide);
            float speedAdvantage = Math.Max(
                0.01f,
                pursuitMetrics.FastestPursuitSquadSpeed - withdrawalMetrics.SlowestMainBodySquadSpeed);
            float pressTurns = separation / speedAdvantage;
            BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(new(
                _currentState.TurnNumber,
                pursuingSide == BattleSide.Attacker,
                pursuing.Aggression,
                pursuitMetrics.AbleSoldierCount,
                withdrawalMetrics.AbleSoldierCount,
                pursuitMetrics.FastestPursuitSquadSpeed,
                withdrawalMetrics.SlowestMainBodySquadSpeed,
                pressTurns,
                ProjectedFollowShotTurns(pursuingSide, separation)));
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

            List<BattleSquad> squads = GetActiveSquads(withdrawingSide).OrderBy(squad => squad.Id).ToList();
            if (squads.Count < 2) return;
            float fastestPursuer = BuildMetrics(pursuerSide).FastestPursuitSquadSpeed;
            float attackReach = MaximumOneTurnAttackReach(pursuerSide);
            List<WithdrawalForecast.SquadGeometry> geometry = squads.Select(squad => new WithdrawalForecast.SquadGeometry(
                squad.Id,
                squad.AbleSoldiers.Count,
                CurrentBattleValue(squad),
                MinimumSquadToForceSeparation(squad, GetActiveSquads(pursuerSide)),
                squad.GetSquadMove())).ToList();
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
                    projection);
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
            DisengageForce(side, events, $"{SideName(side)} broke contact.");
            GetSideState(holder).Intent = BattleSideIntent.Engaged;
            _pursuitPostures.Remove(holder);
            BattleHistory.Outcome = BuildOutcome(BattleEndReason.Withdrawal, holder);
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
                squads.Where(squad => squad.WithdrawalRole == WithdrawalRole.Routing).Select(squad => squad.Id),
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
                float dx = a.TopLeft.Item1 - b.TopLeft.Item1;
                float dy = a.TopLeft.Item2 - b.TopLeft.Item2;
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
