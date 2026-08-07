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
        // The weapon behind each player soldier's first mortal wound. The wound resolver raises
        // its death hook when a vital location is *crippled*, but a soldier is only actually
        // removed when a vital location is *severed* -- a stricter bar he may never reach. Banking
        // the weapon here and writing the event from RemoveSoldiersKilledInBattle keeps the career
        // log agreeing with who really died, and holds it to one entry however many mortal wounds
        // landed before the wound queue drained.
        private readonly Dictionary<int, WeaponTemplate> _mortalWoundWeapons = new();
        // (inflicter, sufferer) pairs already credited to a player soldier's career this battle.
        // Hit locations resolve independently, so one WoundResolver pass can raise the fall hook
        // (a motive or hand location lost) and the death hook (a vital location crippled) for the
        // same enemy off two different wounds -- but an enemy can only be taken down once.
        private readonly HashSet<(int InflicterId, int SuffererId)> _creditedTakedowns = new();
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
                // Keep the blow that first proved mortal; whether it actually killed him is
                // settled at battle end, and the death event is written there.
                _mortalWoundWeapons.TryAdd(playerSoldier.Id, wound.Weapon);
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
            PlayerCasualtyDisposition disposition = ResolvePlayerCasualtyDispositions();
            LogBattleToChapterHistory(disposition);
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

        /// <summary>
        /// Did the Chapter own the ground when the shooting stopped? Only then are its wounded
        /// carried off it. A battle nobody won (mutual disengagement, the turn cap) leaves both
        /// sides free to recover their own -- the same rule
        /// <c>BattleTurnResolver.FinishOffAbandonedWounded</c> applies to everyone else.
        /// </summary>
        private bool PlayerHeldTheField()
        {
            if (_context.BattleHistory.Outcome?.SideHoldingField is not BattleSide holder)
            {
                return true;
            }
            BattleSoldier anyPlayerSoldier = _context.StartingPlayerSoldiers.FirstOrDefault();
            BattleSide playerSide = _context.IsSecondSide(anyPlayerSoldier)
                ? BattleSide.Opposing
                : BattleSide.Attacker;
            return holder == playerSide;
        }

        /// <summary>
        /// Settles every battle-brother who started the fight into one of the three outcomes of
        /// Design/Active/CasualtyRealism.md §2.3, and makes <see cref="BattleHistory"/> agree.
        /// The wound resolver's death hook fires on a *crippled* vital and its fall hook on a lost
        /// limb, neither of which is a verdict -- power-armor biostasis means a downed brother
        /// cannot die of his wounds while he waits, so the only questions that decide his fate are
        /// whether a vital location came off and whether his side held the ground he fell on.
        /// </summary>
        private PlayerCasualtyDisposition ResolvePlayerCasualtyDispositions()
        {
            bool recovered = PlayerHeldTheField();
            List<PlayerSoldier> dead = [];
            List<PlayerSoldier> incapacitated = [];
            HashSet<int> killedIds = _context.BattleHistory.KilledSoldierIds;
            HashSet<int> incapacitatedIds = _context.BattleHistory.IncapacitatedSoldierIds;

            foreach (BattleSoldier soldier in _context.StartingPlayerSoldiers)
            {
                PlayerSoldier playerSoldier = (PlayerSoldier)soldier.Soldier;
                CasualtyState state = CasualtyStateEvaluator.Classify(playerSoldier, recovered);
                // This policy is authoritative for its own brothers: the in-battle hooks marked
                // them provisionally, and both sets are corrected here in both directions.
                killedIds.Remove(playerSoldier.Id);
                incapacitatedIds.Remove(playerSoldier.Id);

                switch (state)
                {
                    case CasualtyState.Killed:
                        bool bodyRecovered =
                            CasualtyStateEvaluator.HasSeveredVitalLocation(playerSoldier);
                        killedIds.Add(playerSoldier.Id);
                        dead.Add(playerSoldier);
                        RecordDeath(playerSoldier, soldier, bodyRecovered);
                        RecordGeneseedRecovery(playerSoldier, bodyRecovered);
                        _dependencies.PlayerSink.MoveToFallenBrothers(playerSoldier);
                        break;
                    case CasualtyState.Incapacitated:
                        incapacitatedIds.Add(playerSoldier.Id);
                        incapacitated.Add(playerSoldier);
                        RecordIncapacitation(playerSoldier, soldier);
                        break;
                }
            }

            return new PlayerCasualtyDisposition(dead, incapacitated);
        }

        // Exactly one death entry per fallen brother, written where death is actually decided.
        // A brother whose vital wound was mortal but not severing lives on, and must not be
        // recorded as killed.
        //
        // <paramref name="bodyRecovered"/> separates the two ways to die: a severed vital, with the
        // Chapter standing over him, and being left on ground the Chapter could not hold. The
        // second is a presumption, and the record says so rather than inventing a killing blow.
        private void RecordDeath(
            PlayerSoldier soldier, BattleSoldier battleSoldier, bool bodyRecovered)
        {
            Faction opposingFaction = _context.GetOpposingFaction(battleSoldier);
            string enemy = opposingFaction?.Name ?? "enemy";
            // A vital location can be severed by a wound that never raised the death hook (the
            // location was already crippled), leaving no weapon to name.
            WeaponTemplate weapon = _mortalWoundWeapons.GetValueOrDefault(soldier.Id);
            string detail;
            if (!bodyRecovered)
            {
                detail = $"Fell in battle with the {enemy} and was left on the field; "
                    + "presumed dead, body not recovered";
            }
            else
            {
                detail = weapon == null
                    ? $"Killed in battle with the {enemy}"
                    : $"Killed in battle with the {enemy} by a {weapon.Name}";
            }

            soldier.AddEvent(new SoldierEvent(
                _dependencies.Date,
                SoldierEventType.Death,
                detail,
                factionId: opposingFaction?.Id,
                weaponTemplateId: bodyRecovered ? weapon?.Id : null));
        }

        // A brother carried off the field alive but out of the fight. He keeps his place on the
        // roster and enters the wound-recovery pipeline; this is the career-log record that he
        // went down, which the plain BattleParticipation note cannot express on its own.
        private void RecordIncapacitation(PlayerSoldier soldier, BattleSoldier battleSoldier)
        {
            Faction opposingFaction = _context.GetOpposingFaction(battleSoldier);
            string enemy = opposingFaction?.Name ?? "enemy";
            soldier.AddEvent(new SoldierEvent(
                _dependencies.Date,
                SoldierEventType.Incapacitated,
                $"Incapacitated in battle with the {enemy} and recovered from the field.",
                factionId: opposingFaction?.Id,
                locationName: _context.Region == null
                    ? null
                    : $"{_context.Region.Name}, {_context.Region.Planet.Name}"));
        }

        private void RecordGeneseedRecovery(PlayerSoldier soldier, bool bodyRecovered)
        {
            (GeneseedRecoveryResult result, float purity) =
                ResolveGeneseedRecovery(soldier, bodyRecovered);
            _geneseedResults[soldier.Id] = result;
            soldier.AddEvent(new SoldierEvent(
                _dependencies.Date,
                SoldierEventType.GeneseedRecovery,
                GetGeneseedRecoveryDetail(result, purity),
                magnitude: result == GeneseedRecoveryResult.Recovered
                    ? (int?)(int)System.Math.Round(purity * 100)
                    : null));
        }

        private (GeneseedRecoveryResult, float) ResolveGeneseedRecovery(
            PlayerSoldier soldier, bool bodyRecovered)
        {
            // No body, no progenoids. An Apothecary cannot cut what the enemy is standing on.
            if (!bodyRecovered)
            {
                return (GeneseedRecoveryResult.Lost, 0f);
            }
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
                GeneseedRecoveryResult.Lost => "Gene-seed lost with the body.",
                _ => "Gene-seed immature and unrecoverable.",
            };

        private static string GetGeneseedStatusLabel(GeneseedRecoveryResult result) =>
            result switch
            {
                GeneseedRecoveryResult.Recovered => "Recovered",
                GeneseedRecoveryResult.Destroyed => "Destroyed",
                GeneseedRecoveryResult.Lost => "Lost",
                _ => "Immature, Unrecoverable",
            };

        private void LogBattleToChapterHistory(PlayerCasualtyDisposition disposition)
        {
            List<string> subEvents = GetBattleLog(disposition);
            string title = $"A skirmish in {_context.Region.Name}, {_context.Region.Planet.Name}";
            _dependencies.PlayerSink.AddToBattleHistory(_dependencies.Date, title, subEvents);
        }

        private List<string> GetBattleLog(PlayerCasualtyDisposition disposition)
        {
            List<string> battleEvents = [];
            int marineCount = _context.StartingPlayerSoldiers.Count;
            int enemyCount = _context.StartingSoldiers.Count - marineCount;
            battleEvents.Add(marineCount.ToString() + " stood against " + enemyCount.ToString() + " enemies");
            foreach (PlayerSoldier soldier in disposition.Killed)
            {
                string geneseedStatus = _geneseedResults.TryGetValue(soldier.Id, out GeneseedRecoveryResult result)
                    ? GetGeneseedStatusLabel(result)
                    : "Unknown";
                battleEvents.Add(
                    $"{soldier.Template.Name} {soldier.Name} died in the service of the emperor. Geneseed: {geneseedStatus}.");
            }
            foreach (PlayerSoldier soldier in disposition.Incapacitated)
            {
                battleEvents.Add(
                    $"{soldier.Template.Name} {soldier.Name} was carried from the field alive but out of the fight.");
            }
            return battleEvents;
        }

        // Who lived, who did not, once the field had an owner.
        private sealed record PlayerCasualtyDisposition(
            IReadOnlyList<PlayerSoldier> Killed,
            IReadOnlyList<PlayerSoldier> Incapacitated);

        // Returns true when the wound is a player soldier's hit on an enemy -- whether or not it
        // produced a fresh career credit, so the per-hit aggregate above stays per-hit.
        private bool CreditPlayerSoldierForKill(BattleSoldier inflicter, BattleSoldier sufferer, WeaponTemplate weapon)
        {
            if (inflicter?.Soldier is not PlayerSoldier playerSoldier
                || sufferer?.Soldier is PlayerSoldier
                || !_context.AreOpposingSides(inflicter, sufferer))
            {
                return false;
            }

            // One takedown, one credit: the fall and death hooks can both fire for this enemy.
            if (!_creditedTakedowns.Add((playerSoldier.Id, sufferer.Soldier.Id)))
            {
                return true;
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
            Immature,
            // The brother was left on ground the Chapter did not hold, so there were no progenoids
            // to cut. Distinct from Destroyed, which means the glands themselves were ruined.
            Lost
        }
    }
}
