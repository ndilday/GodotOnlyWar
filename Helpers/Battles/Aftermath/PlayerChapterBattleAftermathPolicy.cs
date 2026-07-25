using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Aftermath
{
    internal sealed class PlayerChapterBattleAftermathPolicy : IBattleAftermathPolicy
    {
        private readonly BattleAftermathContext _context;
        private readonly BattleAftermathDependencies _dependencies;
        private readonly Dictionary<int, BattleSoldier> _latestPlayerSoldierSnapshots = new();
        private readonly Dictionary<int, GeneseedRecoveryResult> _geneseedResults = new();
        // Dev-facing skill/attribute growth log: snapshot before the fight, diffed once it ends.
        private readonly SoldierProgressLog.ProgressSnapshot _progressBefore;

        public PlayerChapterBattleAftermathPolicy(
            BattleAftermathContext context,
            BattleAftermathDependencies dependencies)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            foreach (BattleSoldier soldier in context.StartingPlayerSoldiers)
            {
                RememberPlayerSnapshot(soldier);
            }
            _progressBefore = SoldierProgressLog.Capture(
                context.StartingPlayerSoldiers.Select(soldier => soldier.Soldier));
        }

        public void OnSoldierDowned(WoundResolution wound, WoundLevel woundLevel)
        {
            RememberPlayerSnapshot(wound.Inflicter);
            RememberPlayerSnapshot(wound.Suffererer);

            if (wound.Suffererer.Soldier is not PlayerSoldier)
            {
                CreditPlayerSoldierForKill(wound.Inflicter, wound.Suffererer, wound.Weapon);
            }
        }

        public void OnSoldierKilled(WoundResolution wound, WoundLevel woundLevel)
        {
            RememberPlayerSnapshot(wound.Inflicter);
            RememberPlayerSnapshot(wound.Suffererer);

            if (wound.Suffererer.Soldier is PlayerSoldier playerSoldier)
            {
                Faction opposingFaction = _context.GetOpposingFaction(wound.Suffererer);
                playerSoldier.AddEvent(new SoldierEvent(
                    _dependencies.Date,
                    SoldierEventType.Death,
                    $"Killed in battle with the {opposingFaction?.Name ?? "enemy"} by a {wound.Weapon.Name}",
                    factionId: opposingFaction?.Id,
                    weaponTemplateId: wound.Weapon.Id));
                return;
            }

            if (CreditPlayerSoldierForKill(wound.Inflicter, wound.Suffererer, wound.Weapon))
            {
                _context.BattleHistory.EnemiesKilled++;
            }
        }

        public void OnBattleCompleted(BattleState finalState)
        {
            RememberFinalPlayerSnapshots(finalState);
            ProcessSoldierHistoryForBattle();
            ApplySoldierExperienceForBattle(finalState);
            SoldierProgressLog.LogDelta(
                $"Battle XP [{_context.Region.Name}, {_context.Region.Planet.Name}]",
                _context.StartingPlayerSoldiers.Select(soldier => soldier.Soldier),
                _progressBefore);
            List<PlayerSoldier> dead = RemoveSoldiersKilledInBattle();
            LogBattleToChapterHistory(dead);
        }

        private void RememberFinalPlayerSnapshots(BattleState finalState)
        {
            foreach (BattleSquad squad in finalState.AllAttackerSquads.Values.Concat(finalState.AllOpposingSquads.Values))
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    RememberPlayerSnapshot(soldier);
                }
            }
        }

        private void RememberPlayerSnapshot(BattleSoldier soldier)
        {
            if (soldier?.Soldier is PlayerSoldier)
            {
                _latestPlayerSoldierSnapshots[soldier.Soldier.Id] = soldier;
            }
        }

        private void ProcessSoldierHistoryForBattle()
        {
            foreach (BattleSoldier startingSoldier in _context.StartingPlayerSoldiers)
            {
                BattleSoldier soldier = _latestPlayerSoldierSnapshots.TryGetValue(startingSoldier.Soldier.Id, out BattleSoldier latest)
                    ? latest
                    : startingSoldier;
                Faction opposingFaction = _context.GetOpposingFaction(startingSoldier);

                string detail = $"Skirmish in {_context.Region.Name}, {_context.Region.Planet.Name}.";
                if (soldier.EnemiesTakenDown > 0)
                {
                    detail += $" Felled {soldier.EnemiesTakenDown} {opposingFaction?.Name ?? "enemies"}.";
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
                            detail += $" Lost his {hl.Template.Name} in the fighting.";
                        }
                    }
                    if (badWound && !sever)
                    {
                        detail += $"Was greviously wounded.";
                    }
                }

                PlayerSoldier playerSoldier = (PlayerSoldier)startingSoldier.Soldier;
                playerSoldier.AddEvent(new SoldierEvent(
                    _dependencies.Date,
                    SoldierEventType.BattleParticipation,
                    detail,
                    factionId: opposingFaction?.Id,
                    magnitude: soldier.EnemiesTakenDown > 0 ? soldier.EnemiesTakenDown : null,
                    locationName: $"{_context.Region.Name}, {_context.Region.Planet.Name}"));
            }
        }

        private static void ApplySoldierExperienceForBattle(BattleState finalState)
        {
            foreach (BattleSquad squad in finalState.AllAttackerSquads.Values.Concat(finalState.AllOpposingSquads.Values))
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    if (soldier.Soldier is not PlayerSoldier)
                    {
                        continue;
                    }

                    // Skills are "learn by doing", roll-based: each attack this battle accrued
                    // margin-scaled skill XP onto the BattleSoldier (BattleExperienceCalculator),
                    // banked here to the weapon's related skill. Aiming grants no skill XP on its own
                    // - the shot it enables carries the margin (and a well-aimed shot is an easier one,
                    // so it teaches less), consistent with the mission field-XP model.
                    if (soldier.RangedSkillXp > 0 && soldier.RangedWeapons.Count > 0)
                    {
                        soldier.Soldier.AddSkillPoints(soldier.RangedWeapons[0].Template.RelatedSkill, soldier.RangedSkillXp);
                    }
                    if (soldier.MeleeSkillXp > 0)
                    {
                        BaseSkill meleeSkill = soldier.MeleeWeapons.Count > 0
                            ? soldier.MeleeWeapons[0].Template.RelatedSkill
                            : soldier.Soldier.Template.Species.DefaultUnarmedWeapon.RelatedSkill;
                        soldier.Soldier.AddSkillPoints(meleeSkill, soldier.MeleeSkillXp);
                    }

                    // Attributes are physical conditioning, NOT roll-based: they accrue from the reps
                    // and the punishment taken, regardless of how hard any single roll was. Kept on the
                    // existing use-count model (per-turn / per-wound), unchanged by the skill rework.
                    if (soldier.TurnsShooting > 0)
                    {
                        soldier.Soldier.AddAttributePoints(OnlyWar.Models.Soldiers.Attribute.Dexterity, soldier.TurnsShooting * 0.0005f);
                    }
                    if (soldier.TurnsSwinging > 0)
                    {
                        soldier.Soldier.AddAttributePoints(OnlyWar.Models.Soldiers.Attribute.Strength, soldier.TurnsSwinging * 0.0005f);
                    }
                    if (soldier.WoundsTaken > 0)
                    {
                        soldier.Soldier.AddAttributePoints(OnlyWar.Models.Soldiers.Attribute.Constitution, soldier.WoundsTaken * 0.0005f);
                    }
                }
            }
        }

        private List<PlayerSoldier> RemoveSoldiersKilledInBattle()
        {
            List<PlayerSoldier> dead = [];
            foreach (BattleSoldier soldier in _context.StartingPlayerSoldiers)
            {
                foreach (HitLocation hl in soldier.Soldier.Body.HitLocations)
                {
                    if (hl.Template.IsVital && hl.IsSevered)
                    {
                        PlayerSoldier playerSoldier = (PlayerSoldier)soldier.Soldier;
                        dead.Add(playerSoldier);
                        RecordGeneseedRecovery(playerSoldier);
                        _dependencies.PlayerSink.MoveToFallenBrothers(playerSoldier);
                        break;
                    }
                }
            }
            return dead;
        }

        private void RecordGeneseedRecovery(PlayerSoldier soldier)
        {
            (GeneseedRecoveryResult result, float purity) = ResolveGeneseedRecovery(soldier);
            _geneseedResults[soldier.Id] = result;
            soldier.AddEvent(new SoldierEvent(
                _dependencies.Date,
                SoldierEventType.GeneseedRecovery,
                GetGeneseedRecoveryDetail(result, purity),
                magnitude: result == GeneseedRecoveryResult.Recovered
                    ? (int?)(int)System.Math.Round(purity * 100)
                    : null));
        }

        private (GeneseedRecoveryResult, float) ResolveGeneseedRecovery(PlayerSoldier soldier)
        {
            if (soldier.Body.HitLocations.Any(hl => hl.Template.HoldsProgenoid && hl.IsSevered))
            {
                return (GeneseedRecoveryResult.Destroyed, 0f);
            }
            if (_dependencies.Date.GetWeeksDifference(soldier.ProgenoidImplantDate)
                < GeneseedRules.ProgenoidMaturityWeeks)
            {
                return (GeneseedRecoveryResult.Immature, 0f);
            }
            float purity = GeneseedRules.RollRecoveredPurity(_dependencies.Random);
            _dependencies.PlayerSink.AddRecoveredGeneseed(purity);
            return (GeneseedRecoveryResult.Recovered, purity);
        }

        private static string GetGeneseedRecoveryDetail(GeneseedRecoveryResult result, float purity) =>
            result switch
            {
                GeneseedRecoveryResult.Recovered => $"Gene-seed recovered (purity {purity:P0}).",
                GeneseedRecoveryResult.Destroyed => "Gene-seed destroyed with the body.",
                _ => "Gene-seed immature and unrecoverable.",
            };

        private static string GetGeneseedStatusLabel(GeneseedRecoveryResult result) =>
            result switch
            {
                GeneseedRecoveryResult.Recovered => "Recovered",
                GeneseedRecoveryResult.Destroyed => "Destroyed",
                _ => "Immature, Unrecoverable",
            };

        private void LogBattleToChapterHistory(List<PlayerSoldier> killedInBattle)
        {
            List<string> subEvents = GetBattleLog(killedInBattle);
            string title = $"A skirmish in {_context.Region.Name}, {_context.Region.Planet.Name}";
            _dependencies.PlayerSink.AddToBattleHistory(_dependencies.Date, title, subEvents);
        }

        private List<string> GetBattleLog(List<PlayerSoldier> killedInBattle)
        {
            List<string> battleEvents = [];
            int marineCount = _context.StartingPlayerSoldiers.Count;
            int enemyCount = _context.StartingSoldiers.Count - marineCount;
            battleEvents.Add(marineCount.ToString() + " stood against " + enemyCount.ToString() + " enemies");
            foreach (PlayerSoldier soldier in killedInBattle)
            {
                string geneseedStatus = _geneseedResults.TryGetValue(soldier.Id, out GeneseedRecoveryResult result)
                    ? GetGeneseedStatusLabel(result)
                    : "Unknown";
                battleEvents.Add(
                    $"{soldier.Template.Name} {soldier.Name} died in the service of the emperor. Geneseed: {geneseedStatus}.");
            }
            return battleEvents;
        }

        private bool CreditPlayerSoldierForKill(BattleSoldier inflicter, BattleSoldier sufferer, WeaponTemplate weapon)
        {
            if (inflicter?.Soldier is not PlayerSoldier playerSoldier
                || sufferer?.Soldier is PlayerSoldier
                || !_context.AreOpposingSides(inflicter, sufferer))
            {
                return false;
            }

            inflicter.EnemiesTakenDown++;
            int factionId = sufferer.BattleSquad.Squad.Faction.Id;
            if (weapon.RelatedSkill.Category == SkillCategory.Melee)
            {
                playerSoldier.AddMeleeKill(factionId, weapon.Id);
            }
            else
            {
                playerSoldier.AddRangedKill(factionId, weapon.Id);
            }
            return true;
        }

        private enum GeneseedRecoveryResult
        {
            Recovered,
            Destroyed,
            Immature
        }
    }
}
