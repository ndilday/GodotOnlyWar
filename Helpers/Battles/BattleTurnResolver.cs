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
                opposingBattleSquads.ToDictionary(os => os.Id, os => os));
            BattleHistory.Turns.Add(new BattleTurn(_currentState, new List<IAction>()));

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

            BattleHistory.Turns.Add(new BattleTurn(_currentState, executedActions));
            foreach (int casualtyId in _casualtyMap.Keys)
            {
                _currentState.RemoveSoldier(casualtyId);
            }
            if (_currentState.AttackerSquads.Count == 0 || _currentState.OpposingSquads.Count == 0)
            {
                Log(false, "One side destroyed, battle over");
                ProcessEndOfBattle(false);
            }
            else if (_currentState.TurnNumber >= MaxBattleTurns)
            {
                Log(false, $"Battle unresolved after {MaxBattleTurns} turns; forcing disengagement");
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
            int firstSideRemaining = _currentState.AttackerSquads.Values.Sum(s => s.AbleSoldiers.Count);
            int secondSideRemaining = _currentState.OpposingSquads.Values.Sum(s => s.AbleSoldiers.Count);
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
            foreach (BattleSquad squad in _currentState.AttackerSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _currentState.Soldiers,
                                                                    shootSegmentActions, moveSegmentActions,
                                                                    meleeSegmentActions,
                                                                    log,
                                                                    _execution.Rules.MeleeWeaponTemplates,
                                                                    _execution.Random);
                planner.PrepareActions(squad);
            }

            foreach (BattleSquad squad in _currentState.OpposingSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _currentState.Soldiers,
                                                                    shootSegmentActions, moveSegmentActions,
                                                                    meleeSegmentActions,
                                                                    log,
                                                                    _execution.Rules.MeleeWeaponTemplates,
                                                                    _execution.Random);
                planner.PrepareActions(squad);
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
