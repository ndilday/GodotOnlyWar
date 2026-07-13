using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles.Actions
{
    public class PlannedMeleeStrike
    {
        public int TargetId { get; }
        public int WeaponTemplateId { get; }
        public string TargetName { get; }
        public string WeaponName { get; }

        public PlannedMeleeStrike(int targetId, int weaponTemplateId, string targetName, string weaponName)
        {
            TargetId = targetId;
            WeaponTemplateId = weaponTemplateId;
            TargetName = targetName;
            WeaponName = weaponName;
        }
    }

    public class MeleeAttackAction : IAction
    {
        // Flat defender-advantage constant in the contested melee roll: "it is
        // easier to avoid a blow than to land one." At equal skill and zero evasion
        // this yields a ~24% per-swing hit rate. The single most important melee
        // balance knob — see Design/EvasionBurrowAndAmbush.md.
        public const float MeleeDefenderAdvantage = 3.0f;
        private const float MovementAttackPenalty = 2.0f;
        private static readonly float OpposedRollSigma = (float)Math.Sqrt(18.0f);

        private readonly int _attackerId;
        private readonly string _attackerName;
        private readonly ConcurrentQueue<string> _log;
        private readonly bool _didMove;
        private bool _wasExecuted;

        public List<WoundResolution> WoundResolutions { get; }
        public IReadOnlyList<PlannedMeleeStrike> StrikePlans { get; }
        public IReadOnlyCollection<int> TargetedDefenderIds => _targetedDefenderIds;
        private readonly HashSet<int> _targetedDefenderIds;

        public int ActorId => _attackerId;

        public MeleeAttackAction(BattleSoldier attacker,
                                 IReadOnlyList<PlannedMeleeStrike> strikePlans,
                                 bool didMove,
                                 ConcurrentQueue<string> log)
        {
            _attackerId = attacker.Soldier.Id;
            _attackerName = attacker.Soldier.Name;
            _didMove = didMove;
            _log = log;
            StrikePlans = strikePlans?.ToList() ?? [];
            WoundResolutions = [];
            _targetedDefenderIds = [];
        }

        public MeleeAttackAction(BattleSoldier attacker,
                                 BattleSoldier target,
                                 MeleeWeapon weapon,
                                 bool didMove,
                                 ConcurrentQueue<string> log)
            : this(attacker,
                   [new PlannedMeleeStrike(target.Soldier.Id,
                                           weapon.Template.Id,
                                           target.Soldier.Name,
                                           weapon.Template.Name)],
                   didMove,
                   log)
        {
        }

        public void Execute(BattleState state)
        {
            if (_wasExecuted)
            {
                return;
            }

            _wasExecuted = true;

            if (StrikePlans.Count == 0)
            {
                return;
            }

            BattleSoldier attacker = state.GetSoldier(_attackerId);
            bool attemptedAnyStrike = false;
            foreach (PlannedMeleeStrike strikePlan in StrikePlans)
            {
                if (!state.Soldiers.ContainsKey(strikePlan.TargetId))
                {
                    continue;
                }

                BattleSoldier target = state.GetSoldier(strikePlan.TargetId);
                if (!IsAdjacent(attacker, target))
                {
                    continue;
                }

                attemptedAnyStrike = true;
                _targetedDefenderIds.Add(target.Soldier.Id);

                attacker.IsInMelee = true;
                target.IsInMelee = true;
                attacker.BattleSquad.IsInMelee = true;
                target.BattleSquad.IsInMelee = true;

                MeleeWeapon weapon = ResolveWeapon(attacker, strikePlan.WeaponTemplateId);
                float attackSkill = attacker.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                float defenderSkill = GetDefenderMeleeSkill(target, weapon.Template.RelatedSkill);
                float defenderDefenseModifier = GetDefenderDefenseModifier(target);
                bool hit = RollMeleeHit(attackSkill,
                                        weapon.Template.Accuracy,
                                        _didMove,
                                        defenderSkill,
                                        target.Soldier.Template.Species.MeleeEvasion,
                                        defenderDefenseModifier);

                _log.Enqueue(attacker.Soldier.Name + " swings at " + target.Soldier);
                if (hit)
                {
                    _log.Enqueue(attacker.Soldier.Name + " strikes " + target.Soldier);
                    HandleHit(attacker, weapon, target);
                }
            }

            if (attemptedAnyStrike)
            {
                attacker.TurnsSwinging++;
            }
            else
            {
                _log.Enqueue("<color=orange>" + attacker.Soldier.Name + " did not get close enough to attack</color>");
            }
        }

        /// <summary>
        /// Contested melee hit roll. The attacker's skill + weapon accuracy (less a
        /// movement penalty) is opposed by the defender's melee skill, evasion,
        /// defense modifiers, and the flat <see cref="MeleeDefenderAdvantage"/>;
        /// each side carries its own random draw. A hit lands when the attacker's
        /// total exceeds the defender's.
        /// </summary>
        public static bool RollMeleeHit(float attackSkill,
                                        float weaponAccuracy,
                                        bool didMove,
                                        float defenderSkill,
                                        float defenderEvasion)
        {
            return RollMeleeHit(attackSkill,
                                weaponAccuracy,
                                didMove,
                                defenderSkill,
                                defenderEvasion,
                                0);
        }

        public static bool RollMeleeHit(float attackSkill,
                                        float weaponAccuracy,
                                        bool didMove,
                                        float defenderSkill,
                                        float defenderEvasion,
                                        float defenderDefenseModifier)
        {
            float attackTotal = attackSkill + weaponAccuracy + (didMove ? -MovementAttackPenalty : 0)
                                + (3.0f * (float)RNG.NextRandomZValue());
            float defendTotal = defenderSkill + defenderEvasion + MeleeDefenderAdvantage
                                + defenderDefenseModifier
                                + (3.0f * (float)RNG.NextRandomZValue());
            return attackTotal > defendTotal;
        }

        public static float EstimateHitProbability(float attackSkill,
                                                   float weaponAccuracy,
                                                   bool didMove,
                                                   float defenderSkill,
                                                   float defenderEvasion,
                                                   float defenderDefenseModifier)
        {
            float attackMean = attackSkill + weaponAccuracy + (didMove ? -MovementAttackPenalty : 0);
            float defendMean = defenderSkill + defenderEvasion + MeleeDefenderAdvantage + defenderDefenseModifier;
            float zScore = (attackMean - defendMean) / OpposedRollSigma;
            return GaussianCalculator.ApproximateNormalCDF(zScore);
        }

        public static float GetDefenderDefenseModifier(BattleSoldier defender)
        {
            float parryModifier = defender.GetMeleeParryModifier();
            if (defender.EquippedMeleeWeapons.Count == 0)
            {
                parryModifier += GetUnarmedWeapon(defender)?.Template.ParryModifier ?? 0;
            }

            return parryModifier + (defender.IsDualWieldingMelee() ? 1.0f : 0.0f);
        }

        public static float GetDefenderMeleeSkill(BattleSoldier defender, BaseSkill fallbackSkill)
        {
            float best = float.MinValue;
            foreach (MeleeWeapon weapon in defender.EquippedMeleeWeapons)
            {
                float value = defender.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
                if (value > best)
                {
                    best = value;
                }
            }

            if (best == float.MinValue)
            {
                // Unarmed defenders fight with the default unarmed weapon's skill (Fist for
                // Astartes, Generic Melee for NPC species). Basic unarmed-combat training is
                // part of every soldier's MOS data, so this stays above raw untrained.
                MeleeWeapon unarmedWeapon = GetUnarmedWeapon(defender);
                best = defender.Soldier.GetTotalSkillValue(
                    unarmedWeapon?.Template.RelatedSkill ?? fallbackSkill);
            }

            return best;
        }

        private static MeleeWeapon GetUnarmedWeapon(BattleSoldier defender)
        {
            if (defender?.BattleSquad == null)
            {
                return null;
            }

            GameRulesData rules = GameDataSingleton.Instance.GameRulesData;
            if (rules == null)
            {
                return null;
            }

            MeleeWeaponTemplate template = defender.BattleSquad.IsPlayerSquad
                ? rules.BattleDefaults.ImperialUnarmedWeapon
                : rules.BattleDefaults.GenericUnarmedWeapon;
            return template == null ? null : new MeleeWeapon(template);
        }

        private static bool IsAdjacent(BattleSoldier attacker, BattleSoldier target)
        {
            int topLimit = attacker.TopLeft.Item2 + 1;
            int leftLimit = attacker.TopLeft.Item1 - 1;
            int bottomLimit = attacker.BottomRight.Item2 - 1;
            int rightLimit = attacker.BottomRight.Item1 + 1;

            bool targetIsAbove = target.BottomRight.Item2 > topLimit;
            bool targetIsBelow = target.TopLeft.Item2 < bottomLimit;
            bool targetIsLeft = target.BottomRight.Item1 < leftLimit;
            bool targetIsRight = target.TopLeft.Item1 > rightLimit;

            return !targetIsAbove && !targetIsBelow && !targetIsLeft && !targetIsRight;
        }

        private static MeleeWeapon ResolveWeapon(BattleSoldier attacker, int weaponTemplateId)
        {
            MeleeWeapon matchingWeapon = attacker.EquippedMeleeWeapons
                .Concat(attacker.MeleeWeapons)
                .FirstOrDefault(weapon => weapon.Template.Id == weaponTemplateId);
            if (matchingWeapon != null)
            {
                return matchingWeapon;
            }

            if (GameDataSingleton.Instance.GameRulesData.MeleeWeaponTemplates.TryGetValue(weaponTemplateId, out MeleeWeaponTemplate template))
            {
                return new MeleeWeapon(template);
            }

            throw new InvalidOperationException($"Melee weapon template {weaponTemplateId} is not available for attacker {attacker.Soldier.Id}.");
        }

        private void HandleHit(BattleSoldier attacker, MeleeWeapon weapon, BattleSoldier target)
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(target);
            if (!hitLocation.IsSevered)
            {
                float damage = attacker.Soldier.Strength * weapon.Template.StrengthMultiplier
                    * (3.5f + ((float)RNG.NextRandomZValue() * 1.75f));
                float effectiveArmor = (target.Armor?.Template.ArmorProvided ?? 0) * weapon.Template.ArmorMultiplier;
                float penDamage = damage - effectiveArmor;
                if (penDamage > 0)
                {
                    float totalDamage = penDamage * weapon.Template.WoundMultiplier;
                    WoundResolutions.Add(new WoundResolution(attacker, weapon.Template, target, totalDamage, hitLocation));
                }
            }
        }

        public string Description()
        {
            Dictionary<Tuple<int, int>, int> hitCountByTargetAndWeapon = [];
            foreach (WoundResolution wound in WoundResolutions)
            {
                Tuple<int, int> key = new Tuple<int, int>(wound.Suffererer.Soldier.Id, wound.Weapon.Id);
                if (hitCountByTargetAndWeapon.ContainsKey(key))
                {
                    hitCountByTargetAndWeapon[key]++;
                }
                else
                {
                    hitCountByTargetAndWeapon[key] = 1;
                }
            }

            HashSet<Tuple<int, int>> describedKeys = [];
            string desc = "";
            foreach (PlannedMeleeStrike strikePlan in StrikePlans)
            {
                Tuple<int, int> key = new Tuple<int, int>(strikePlan.TargetId, strikePlan.WeaponTemplateId);
                if (!describedKeys.Add(key))
                {
                    continue;
                }

                hitCountByTargetAndWeapon.TryGetValue(key, out int hitCount);
                desc += $"{_attackerName} attacks {strikePlan.TargetName} with {strikePlan.WeaponName}\n";
                desc += $"Hitting {hitCount} times.\n";
            }

            foreach (WoundResolution wound in WoundResolutions)
            {
                desc += wound.Description;
            }

            return desc;
        }
    }
}
