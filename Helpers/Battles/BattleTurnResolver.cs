using Godot;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    public class BattleTurnResolver
    {
        private BattleGridManager _grid;
        private readonly Dictionary<int, BattleSquad> _playerBattleSquads, _opposingBattleSquads;
        private readonly Faction _opposingFaction;
        private readonly Planet _planet;
        private int _startingEnemySoldierCount;
        private readonly List<BattleSoldier> _startingPlayerBattleSoldiers;
        private readonly WoundResolver _woundResolver;
        private readonly Dictionary<int, BattleSquad> _soldierBattleSquadMap = new Dictionary<int, BattleSquad>();
        private readonly Dictionary<int, BattleSoldier> _casualtyMap;
        private readonly bool _isVerbose;
        private BattleHistory _battleHistory;
        private BattleState _currentState;

        public event EventHandler<BattleHistory> OnBattleComplete;

        public BattleTurnResolver(BattleGridManager grid,
                                  IList<BattleSquad> playerBattleSquads,
                                  IList<BattleSquad> opposingBattleSquads,
                                  Planet planet,
                                  bool isVerbose)
        {
            _grid = grid;
            _planet = planet;
            _isVerbose = isVerbose;
            _playerBattleSquads = playerBattleSquads.ToDictionary(bs => bs.Id, bs => bs);
            _opposingBattleSquads = opposingBattleSquads.ToDictionary(os => os.Id, os => os);
            _opposingFaction = opposingBattleSquads.First().Squad.Faction;
            _woundResolver = new WoundResolver(isVerbose);
            _woundResolver.OnSoldierDeath += WoundResolver_OnSoldierDeath;
            _woundResolver.OnSoldierFall += WoundResolver_OnSoldierFall;
            _casualtyMap = new Dictionary<int, BattleSoldier>();
            _startingPlayerBattleSoldiers = playerBattleSquads.SelectMany(bs => bs.Soldiers).ToList();
            _startingEnemySoldierCount = opposingBattleSquads.SelectMany(s => s.Soldiers).Count();

            _currentState = new BattleState(_playerBattleSquads, _opposingBattleSquads);
            _battleHistory = new BattleHistory();
            foreach (BattleSquad squad in playerBattleSquads)
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _soldierBattleSquadMap[soldier.Soldier.Id] = squad;
                }
            }
            foreach (BattleSquad squad in opposingBattleSquads)
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _soldierBattleSquadMap[soldier.Soldier.Id] = squad;
                }
            }
        }

        private void WoundResolver_OnSoldierDeath(BattleSoldier casualty, BattleSoldier inflicter, WeaponTemplate weapon)
        {
            _casualtyMap[casualty.Soldier.Id] = casualty;
            if (casualty.BattleSquad.IsPlayerSquad)
            {
                // add death note to soldier history, though we currently just delete it 
                // we'll probably want it later
                PlayerSoldier playerSoldier = casualty.Soldier as PlayerSoldier;
                playerSoldier.AddEntryToHistory($"Killed in battle with the {_opposingFaction.Name} by a {weapon.Name}");
            }
            else
            {
                // give the inflicter credit for downing this enemy
                // WARNING: this will lead to multi-counting in some cases
                // I may later try to divide credit, but having multiple soldiers 
                // claim credit feels pseudo-realistic for now
                CreditSoldierForKill(inflicter, weapon);
            }
        }

        private void WoundResolver_OnSoldierFall(BattleSoldier fallenSoldier, BattleSoldier inflicter, WeaponTemplate weapon)
        {
            _casualtyMap[fallenSoldier.Soldier.Id] = fallenSoldier;
            if (!fallenSoldier.BattleSquad.IsPlayerSquad)
            {
                // give the inflicter credit for downing this enemy
                // WARNING: this will lead to multi-counting in some cases
                // I may later try to divide credit, but having multiple soldiers 
                // claim credit feels pseudo-realistic for now
                CreditSoldierForKill(inflicter, weapon);
            }
        }

        public void ProcessNextTurn()
        {

            _grid.ClearReservations();
            _casualtyMap.Clear();
            Log(false, "Turn " + _currentState.TurnNumber.ToString());
            // this is a three step process: plan, execute, and apply

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
            _currentState = new BattleState(_currentState);
            List<IAction> executedActions = new List<IAction>();
            HandleShooting(shootSegmentActions, executedActions);
            HandleMoving(moveSegmentActions, executedActions);
            HandleMelee(meleeSegmentActions, executedActions);

            CleanupAtEndOfTurn();

            _battleHistory.Turns.Add(new BattleTurn(_currentState, executedActions));
            if (_playerBattleSquads.Count() == 0 || _opposingBattleSquads.Count() == 0)
            {
                Log(false, "One side destroyed, battle over");
                ProcessEndOfBattle();
            }
        }

        private void ProcessEndOfBattle()
        {
            // we'll be nice to the Marines despite losing the battle... for now
            GD.Print("Battle completed");
            ProcessSoldierHistoryForBattle();
            ApplySoldierExperienceForBattle();
            List<PlayerSoldier> dead = RemoveSoldiersKilledInBattle();
            LogBattleToChapterHistory(dead);
            OnBattleComplete.Invoke(this, _battleHistory);
        }

        private void Plan(ConcurrentBag<IAction> shootSegmentActions,
                          ConcurrentBag<IAction> moveSegmentActions,
                          ConcurrentBag<IAction> meleeSegmentActions,
                          ConcurrentQueue<string> log)
        {
            // PLAN
            // use the thread pool to handle the BattleSquadPlanner classes;
            // these look at the current game state to figure out the actions each soldier should take
            // the planners populate the actionBag with what they want to do
            MeleeWeapon defaultWeapon = new MeleeWeapon(
                GameDataSingleton.Instance.GameRulesData.MeleeWeaponTemplates.Values
                    .First(mwt => mwt.Name == "Fist"));
            //Parallel.ForEach(_playerSquads.Values, (squad) =>
            foreach (BattleSquad squad in _playerBattleSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _soldierBattleSquadMap,
                                                                    shootSegmentActions, moveSegmentActions,
                                                                    meleeSegmentActions,
                                                                    _woundResolver.WoundQueue,
                                                                    log, defaultWeapon);
                planner.PrepareActions(squad);
            };
            //Parallel.ForEach(_opposingSquads.Values, (squad) =>
            foreach (BattleSquad squad in _opposingBattleSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _soldierBattleSquadMap,
                                                                    shootSegmentActions, moveSegmentActions,
                                                                    meleeSegmentActions,
                                                                    _woundResolver.WoundQueue,
                                                                    log, defaultWeapon);
                planner.PrepareActions(squad);
            };
        }

        private void HandleShooting(ConcurrentBag<IAction> shootActions, List<IAction> executedActions)
        {
            // EXECUTE
            // once the squads have all finished planning actions, we process the execution logic. 
            // These use the command pattern to allow the controller to execute each without 
            // having any knowledge of what the internal implementation is
            // this also allows us to separate the concerns of the planner and the executor
            // we take the results/side effects of each execution that impact the outside world 
            // and put those results into queues
            // (movement and wounding are the only things that fit this category, today, 
            // but there will be others in the future)
            //Parallel.ForEach(actionBag, (action) => action.Execute());
            foreach (IAction action in shootActions)
            {
                action.Execute(_currentState);
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
                executedActions.Add(action);
            }
        }

        private void CleanupAtEndOfTurn()
        {
            // handle casualties
            foreach (BattleSoldier soldier in _casualtyMap.Values)
            {
                RemoveSoldier(soldier, _soldierBattleSquadMap[soldier.Soldier.Id]);
            }

            // update who's in melee
            foreach (BattleSquad squad in _playerBattleSquads.Values)
            {
                UpdateSquadMeleeStatus(squad);
            }
            foreach (BattleSquad squad in _opposingBattleSquads.Values)
            {
                UpdateSquadMeleeStatus(squad);
            }
        }

        private void UpdateSquadMeleeStatus(BattleSquad squad)
        {
            bool atLeastOneSoldierInMelee = false;
            foreach (BattleSoldier soldier in squad.Soldiers)
            {
                soldier.IsInMelee = _grid.IsAdjacentToEnemy(soldier.Soldier.Id);
                if (soldier.IsInMelee) atLeastOneSoldierInMelee = true;
            }
            squad.IsInMelee = atLeastOneSoldierInMelee;
        }

        /*private void ResetBattleValues(BattleConfiguration config)
        {
            _grid = config.Grid;
            _opposingFaction = config.OpposingSquads[0].Squad.ParentUnit.UnitTemplate.Faction;

            _playerBattleSquads.Clear();
            _soldierBattleSquadMap.Clear();
            _opposingBattleSquads.Clear();
            _startingPlayerBattleSoldiers.Clear();
            _startingEnemySoldierCount = 0;

            foreach (BattleSquad squad in config.PlayerSquads)
            {
                _playerBattleSquads[squad.Id] = squad;
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _soldierBattleSquadMap[soldier.Soldier.Id] = squad;
                }
                _startingPlayerBattleSoldiers.AddRange(squad.Soldiers);
            }

            foreach (BattleSquad squad in config.OpposingSquads)
            {
                _opposingBattleSquads[squad.Id] = squad;
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    _soldierBattleSquadMap[soldier.Soldier.Id] = squad;
                }
                _startingEnemySoldierCount += squad.Soldiers.Count;
            }
        }*/

        private string GetSquadDetails(BattleSquad squad)
        {
            string report = "\n" + squad.Name + "\n" + squad.Soldiers.Count.ToString() + " soldiers standing\n\n";
            foreach (BattleSoldier soldier in squad.Soldiers)
            {
                report += GetSoldierDetails(soldier);
            }
            return report;
        }

        private static string GetSoldierDetails(BattleSoldier soldier)
        {
            string report = soldier.Soldier.Name + "\n";
            foreach (RangedWeapon weapon in soldier.RangedWeapons)
            {
                report += weapon.Template.Name + "\n";
            }
            report += soldier.Armor.Template.Name + "\n";
            foreach (HitLocation hl in soldier.Soldier.Body.HitLocations)
            {
                if (hl.Wounds.WoundTotal != 0)
                {
                    report += hl.ToString() + "\n";
                }
            }
            report += "\n";
            return report;
        }

        private string GetSquadSummary(BattleSquad squad)
        {
            return "\n" + squad.Name + "\n" + squad.Soldiers.Count.ToString() + " soldiers standing\n\n";
        }

        private void Log(bool isMessageVerbose, string text)
        {
            if (_isVerbose || !isMessageVerbose)
            {
                GD.Print(text);
            }
        }

        private void RemoveSoldier(BattleSoldier soldier, BattleSquad squad)
        {
            squad.RemoveSoldier(soldier);
            _grid.RemoveSoldier(soldier.Soldier.Id);
            _soldierBattleSquadMap.Remove(soldier.Soldier.Id);
            if (squad.Soldiers.Count == 0)
            {
                RemoveSquad(squad);
            }
        }

        private void RemoveSquad(BattleSquad squad)
        {
            Log(false, "<b>" + squad.Name + " wiped out</b>");

            if (squad.IsPlayerSquad)
            {
                _playerBattleSquads.Remove(squad.Id);
            }
            else
            {
                _opposingBattleSquads.Remove(squad.Id);
            }
        }

        private void ProcessSoldierHistoryForBattle()
        {
            foreach (BattleSoldier soldier in _startingPlayerBattleSoldiers)
            {
                string historyEntry = GameDataSingleton.Instance.Date.ToString()
                    + ": Fought in a skirmish on " + _planet.Name + ".";
                if (soldier.EnemiesTakenDown > 0)
                {
                    historyEntry += $" Felled {soldier.EnemiesTakenDown} {_opposingFaction.Name}.";
                }
                if (soldier.WoundsTaken > 0)
                {
                    bool badWound = false;
                    bool sever = false;
                    foreach (HitLocation hl in soldier.Soldier.Body.HitLocations)
                    {
                        if (hl.Template.IsVital && hl.IsCrippled)
                        {
                            badWound = true;
                        }
                        if (hl.IsSevered)
                        {
                            sever = true;
                            historyEntry += $" Lost his {hl.Template.Name} in the fighting.";
                        }
                    }
                    if (badWound && !sever)
                    {
                        historyEntry += $"Was greviously wounded.";
                    }
                }
                PlayerSoldier playerSolider = soldier.Soldier as PlayerSoldier;
                playerSolider.AddEntryToHistory(historyEntry);
            }

        }

        private void ApplySoldierExperienceForBattle()
        {
            // each wound is .005 CON, for now
            // each turn shooting is .0005 for both DEX and the gun skill
            // each turn aiming is .0005 for the gun skill
            // each turn swinging is .0005 for ST and the melee skill
            foreach (BattleSquad squad in _playerBattleSquads.Values)
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    if (soldier.RangedWeapons.Count > 0)
                    {
                        if (soldier.TurnsAiming > 0)
                        {
                            soldier.Soldier.AddSkillPoints(soldier.RangedWeapons[0].Template.RelatedSkill, soldier.TurnsAiming * 0.0005f);
                        }
                        if (soldier.TurnsShooting > 0)
                        {
                            soldier.Soldier.AddSkillPoints(soldier.RangedWeapons[0].Template.RelatedSkill, soldier.TurnsShooting * 0.0005f);
                            soldier.Soldier.AddAttributePoints(Models.Soldiers.Attribute.Dexterity, soldier.TurnsShooting * 0.0005f);
                        }
                    }
                    if (soldier.WoundsTaken > 0)
                    {
                        soldier.Soldier.AddAttributePoints(Models.Soldiers.Attribute.Constitution, soldier.WoundsTaken * 0.0005f);
                    }
                    if (soldier.TurnsSwinging > 0)
                    {
                        if (soldier.MeleeWeapons.Count > 0)
                        {
                            soldier.Soldier.AddSkillPoints(soldier.MeleeWeapons[0].Template.RelatedSkill, soldier.TurnsSwinging * 0.0005f);
                        }
                        else
                        {
                            BaseSkill baseMeleeSkill = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(bs => bs.Name == "Fist");
                            soldier.Soldier.AddSkillPoints(baseMeleeSkill, soldier.TurnsSwinging * 0.0005f);
                        }
                        soldier.Soldier.AddAttributePoints(Models.Soldiers.Attribute.Strength, soldier.TurnsSwinging * 0.0005f);
                    }
                }
            }
        }

        private List<PlayerSoldier> RemoveSoldiersKilledInBattle()
        {
            List<PlayerSoldier> dead = new List<PlayerSoldier>();
            foreach (BattleSoldier soldier in _startingPlayerBattleSoldiers)
            {
                foreach (HitLocation hl in soldier.Soldier.Body.HitLocations)
                {
                    if (hl.Template.IsVital && hl.IsSevered)
                    {
                        // if a vital part is severed, they're dead
                        PlayerSoldier playerSoldier = soldier.Soldier as PlayerSoldier;
                        dead.Add(playerSoldier);
                        playerSoldier.AssignedSquad.RemoveSquadMember(playerSoldier);
                        GameDataSingleton.Instance.Sector.PlayerForce.Army.PlayerSoldierMap.Remove(soldier.Soldier.Id);
                        break;
                    }
                }
            }
            return dead;
        }

        private void LogBattleToChapterHistory(List<PlayerSoldier> killedInBattle)
        {
            var subEvents = GetBattleLog(killedInBattle);
            GameDataSingleton.Instance.Sector.PlayerForce.AddToBattleHistory(GameDataSingleton.Instance.Date,
                                                                             $"A skirmish on {_planet.Name}",
                                                                             subEvents);
        }

        private List<string> GetBattleLog(List<PlayerSoldier> killedInBattle)
        {
            List<string> battleEvents = new List<string>();
            int marineCount = _startingPlayerBattleSoldiers.Count;
            battleEvents.Add(marineCount.ToString() + " stood against " + _startingEnemySoldierCount.ToString() + " enemies");
            foreach (PlayerSoldier soldier in killedInBattle)
            {
                string geneseedStatus = GetGeneseedStatusDescription(soldier);
                battleEvents.Add(
                    $"{soldier.Template.Name} {soldier.Name} died in the service of the emperor. Geneseed: {geneseedStatus}.");
            }
            return battleEvents;
        }

        private string GetGeneseedStatusDescription(PlayerSoldier soldier)
        {
            if (soldier.Body.HitLocations.First(hl => hl.Template.Name == "Face").IsSevered)
            {
                return "Destroyed";
            }
            else if (soldier.Body.HitLocations.First(hl => hl.Template.Name == "Torso").IsSevered)
            {
                return "Destroyed";
            }
            else if (GameDataSingleton.Instance.Date.GetWeeksDifference(soldier.ProgenoidImplantDate) < 260)
            {
                return "Immature, Unrecoverable";
            }
            else
            {
                GameDataSingleton.Instance.Sector.PlayerForce.GeneseedStockpile++;
                return "Recovered";
            }
        }

        private void CreditSoldierForKill(BattleSoldier inflicter, WeaponTemplate weapon)
        {
            inflicter.EnemiesTakenDown++;
            PlayerSoldier playerSoldier = inflicter.Soldier as PlayerSoldier;
            if (weapon.RelatedSkill.Category == SkillCategory.Melee)
            {
                playerSoldier.AddMeleeKill(_opposingFaction.Id, weapon.Id);
            }
            else
            {
                playerSoldier.AddRangedKill(_opposingFaction.Id, weapon.Id);
            }
        }
    }
}