using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    public class BattleTurnResolver
    {
        private BattleGridManager _grid;
        private readonly Region _region;
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

        public BattleTurnResolver(BattleGridManager grid,
                                  IList<BattleSquad> attackerBattleSquads,
                                  IList<BattleSquad> opposingBattleSquads,
                                  Region region)
        {
            _grid = grid;
            _region = region;
            _woundResolver = new WoundResolver();
            _woundResolver.OnSoldierDeath += WoundResolver_OnSoldierDeath;
            _woundResolver.OnSoldierFall += WoundResolver_OnSoldierFall;
            _casualtyMap = new Dictionary<int, BattleSoldier>();
            BattleHistory = new BattleHistory();
            _aftermathContext = new BattleAftermathContext(
                attackerBattleSquads.ToList(),
                opposingBattleSquads.ToList(),
                region,
                BattleHistory);
            _aftermathPolicy = BattleAftermathPolicyFactory.Create(_aftermathContext);

            _currentState = new BattleState(
                attackerBattleSquads.ToDictionary(bs => bs.Id, bs => bs),
                opposingBattleSquads.ToDictionary(os => os.Id, os => os));
            BattleHistory.Turns.Add(new BattleTurn(_currentState, new List<IAction>()));

            GameLog.Debug(() =>
                $"Battle start in {_region?.Name}: {_aftermathContext.FirstSideStartingSoldierCount} vs "
                + $"{_aftermathContext.SecondSideStartingSoldierCount} soldiers "
                + $"({attackerBattleSquads.Count}+{opposingBattleSquads.Count} squads)");
        }

        private void WoundResolver_OnSoldierDeath(WoundResolution wound, WoundLevel woundLevel)
        {
            _casualtyMap[wound.Suffererer.Soldier.Id] = wound.Suffererer;
            BattleHistory.KilledSoldierIds.Add(wound.Suffererer.Soldier.Id);
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
            _currentState = new BattleState(_currentState);

            Log(false, "Turn " + _currentState.TurnNumber.ToString());

            ConcurrentBag<IAction> moveSegmentActions = new ConcurrentBag<IAction>();
            ConcurrentBag<IAction> shootSegmentActions = new ConcurrentBag<IAction>();
            ConcurrentBag<IAction> meleeSegmentActions = new ConcurrentBag<IAction>();
            ConcurrentQueue<string> log = new ConcurrentQueue<string>();
            Plan(shootSegmentActions, moveSegmentActions, meleeSegmentActions, log);
            while (!log.IsEmpty)
            {
                log.TryDequeue(out string line);
                Log(false, line);
            }

            List<IAction> executedActions = new List<IAction>();
            HandleShooting(shootSegmentActions, executedActions);
            HandleMoving(moveSegmentActions, executedActions);
            HandleMelee(meleeSegmentActions, executedActions);
            _woundResolver.Resolve();

            CleanupAtEndOfTurn();

            BattleHistory.Turns.Add(new BattleTurn(_currentState, executedActions));
            if (_currentState.AttackerSquads.Count == 0 || _currentState.OpposingSquads.Count == 0)
            {
                Log(false, "One side destroyed, battle over");
                ProcessEndOfBattle();
            }
            else if (_currentState.TurnNumber >= MaxBattleTurns)
            {
                Log(false, $"Battle unresolved after {MaxBattleTurns} turns; forcing disengagement");
                ProcessEndOfBattle();
            }
        }

        private void ProcessEndOfBattle()
        {
            _stopwatch.Stop();
            GameLog.Debug(() =>
                $"Battle end in {_region?.Name}: {_currentState.TurnNumber} turns, "
                + $"{_stopwatch.ElapsedMilliseconds}ms, started "
                + $"{_aftermathContext.FirstSideStartingSoldierCount} vs "
                + $"{_aftermathContext.SecondSideStartingSoldierCount} soldiers");
            BattleLog.Write("Battle completed");
            _aftermathPolicy.OnBattleCompleted(_currentState);
            OnBattleComplete?.Invoke(this, BattleHistory);
        }

        private void Plan(ConcurrentBag<IAction> shootSegmentActions,
                          ConcurrentBag<IAction> moveSegmentActions,
                          ConcurrentBag<IAction> meleeSegmentActions,
                          ConcurrentQueue<string> log)
        {
            BattleDefaults battleDefaults = GameDataSingleton.Instance.GameRulesData.BattleDefaults;
            MeleeWeapon attackerDefaultWeapon = new MeleeWeapon(battleDefaults.ImperialUnarmedWeapon);
            MeleeWeapon opposingDefaultWeapon = new MeleeWeapon(battleDefaults.GenericUnarmedWeapon);

            foreach (BattleSquad squad in _currentState.AttackerSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _currentState.Soldiers,
                                                                    shootSegmentActions, moveSegmentActions,
                                                                    meleeSegmentActions,
                                                                    log, attackerDefaultWeapon);
                planner.PrepareActions(squad);
            }

            foreach (BattleSquad squad in _currentState.OpposingSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _currentState.Soldiers,
                                                                    shootSegmentActions, moveSegmentActions,
                                                                    meleeSegmentActions,
                                                                    log, opposingDefaultWeapon);
                planner.PrepareActions(squad);
            }
        }

        private void HandleShooting(ConcurrentBag<IAction> shootActions, List<IAction> executedActions)
        {
            foreach (IAction action in shootActions)
            {
                action.Execute(_currentState);
                if (action is ShootAction shootAction)
                {
                    foreach (WoundResolution wound in shootAction.WoundResolutions)
                    {
                        _woundResolver.WoundQueue.Add(wound);
                    }
                }
                executedActions.Add(action);
            }
        }

        private void HandleMoving(ConcurrentBag<IAction> moveActions, List<IAction> executedActions)
        {
            foreach (IAction action in moveActions)
            {
                action.Execute(_currentState);
                executedActions.Add(action);
            }
        }

        private void HandleMelee(ConcurrentBag<IAction> meleeActions, List<IAction> executedActions)
        {
            foreach (IAction action in meleeActions)
            {
                action.Execute(_currentState);
                if (action is MeleeAttackAction meleeAction)
                {
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
