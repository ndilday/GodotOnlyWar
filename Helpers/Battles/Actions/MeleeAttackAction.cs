using System;
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
        // Flat defender-advantage constant in the contested melee roll. Zero since the
        // melee rework calibration pass: equal-skill fighters trade at ~50% per swing
        // (tabletop's "hit on 4s"); melee pacing is governed by attack counts and damage,
        // not an artificial whiff rate. Raise this to make all melee defense stronger.
        public const float MeleeDefenderAdvantage = 0.0f;

        // Standard deviation of each side's random draw in the contested roll. Doubled
        // (3 -> 6) in the same calibration pass: each point of skill margin is worth
        // ~5.6% hit chance near parity instead of ~9.4%, compressing large skill gaps
        // toward tabletop's clamped 33-67% band (a genestealer is mega scary but not
        // unhittable) while keeping the smooth Gaussian tails.
        public const float MeleeRollStandardDeviation = 6.0f;

        private const float MovementAttackPenalty = 2.0f;
        private static readonly float OpposedRollSigma =
            (float)(MeleeRollStandardDeviation * Math.Sqrt(2.0));

        private readonly int _attackerId;
        private readonly string _attackerName;
        private readonly Action<string> _log;
        private readonly bool _didMove;
        private readonly IRNG _random;
        private readonly IReadOnlyDictionary<int, MeleeWeaponTemplate> _meleeWeaponTemplates;
        private bool _wasExecuted;

        public List<WoundResolution> WoundResolutions { get; }
        public IReadOnlyList<PlannedMeleeStrike> StrikePlans { get; }
        public IReadOnlyCollection<int> TargetedDefenderIds => _targetedDefenderIds;
        private readonly HashSet<int> _targetedDefenderIds;

        public int ActorId => _attackerId;

        public MeleeAttackAction(BattleSoldier attacker,
                                 IReadOnlyList<PlannedMeleeStrike> strikePlans,
                                 bool didMove,
                                 Action<string> log,
                                 IRNG random,
                                 IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates)
        {
            _attackerId = attacker.Soldier.Id;
            _attackerName = attacker.Soldier.Name;
            _didMove = didMove;
            _log = log;
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _meleeWeaponTemplates = meleeWeaponTemplates
                ?? throw new ArgumentNullException(nameof(meleeWeaponTemplates));
            StrikePlans = strikePlans?.ToList() ?? [];
            WoundResolutions = [];
            _targetedDefenderIds = [];
        }

        public MeleeAttackAction(BattleSoldier attacker,
                                 BattleSoldier target,
                                 MeleeWeapon weapon,
                                 bool didMove,
                                 Action<string> log,
                                 IRNG random,
                                 IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates)
            : this(attacker,
                   [new PlannedMeleeStrike(target.Soldier.Id,
                                           weapon.Template.Id,
                                           target.Soldier.Name,
                                           weapon.Template.Name)],
                   didMove,
                   log,
                   random,
                   meleeWeaponTemplates)
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
                                        defenderDefenseModifier,
                                        _random);

                _log?.Invoke(attacker.Soldier.Name + " swings at " + target.Soldier);
                if (hit)
                {
                    _log?.Invoke(attacker.Soldier.Name + " strikes " + target.Soldier);
                    HandleHit(attacker, weapon, target);
                }
            }

            if (attemptedAnyStrike)
            {
                attacker.TurnsSwinging++;
            }
            else
            {
                _log?.Invoke("<color=orange>" + attacker.Soldier.Name + " did not get close enough to attack</color>");
            }
        }

        /// <summary>
        /// Contested melee hit roll. The attacker's skill + weapon accuracy (less a
        /// movement penalty) is opposed by the defender's melee skill, evasion,
        /// defense modifiers, and the flat <see cref="MeleeDefenderAdvantage"/>;
        /// each side carries its own random draw of
        /// <see cref="MeleeRollStandardDeviation"/>. A hit lands when the attacker's
        /// total exceeds the defender's. At equal skill and zero modifiers this
        /// yields a ~50% per-swing hit rate.
        /// </summary>
        public static bool RollMeleeHit(float attackSkill,
                                        float weaponAccuracy,
                                        bool didMove,
                                        float defenderSkill,
                                        float defenderEvasion,
                                        IRNG random)
        {
            return RollMeleeHit(attackSkill,
                                weaponAccuracy,
                                didMove,
                                defenderSkill,
                                defenderEvasion,
                                0,
                                random);
        }

        public static bool RollMeleeHit(float attackSkill,
                                        float weaponAccuracy,
                                        bool didMove,
                                        float defenderSkill,
                                        float defenderEvasion,
                                        float defenderDefenseModifier,
                                        IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            float attackTotal = attackSkill + weaponAccuracy + (didMove ? -MovementAttackPenalty : 0)
                                + (MeleeRollStandardDeviation * (float)random.NextRandomZValue());
            float defendTotal = defenderSkill + defenderEvasion + MeleeDefenderAdvantage
                                + defenderDefenseModifier
                                + (MeleeRollStandardDeviation * (float)random.NextRandomZValue());
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
            return GetDefenderDefenseModifier(defender, defender?.EquippedMeleeWeapons);
        }

        /// <summary>
        /// Returns the defensive modifier for a specific projected melee loadout. The planner uses
        /// this overload when comparing a gun currently in hand with the melee weapon that would be
        /// readied instead; planning must not mutate the soldier's real equipment to value that parry.
        /// </summary>
        internal static float GetDefenderDefenseModifier(
            BattleSoldier defender,
            IReadOnlyCollection<MeleeWeapon> projectedMeleeWeapons)
        {
            // Defensive value of the weapons in hand is expressed solely through their
            // parry modifiers (summed across equipped weapons); a second weapon helps
            // defense only if it is actually a parrying tool. No flat dual-wield bonus.
            float parryModifier = MeleeMath.SumParryModifiers(projectedMeleeWeapons);
            if (projectedMeleeWeapons == null || projectedMeleeWeapons.Count == 0)
            {
                parryModifier += GetUnarmedWeapon(defender)?.Template.ParryModifier ?? 0;
            }

            return parryModifier;
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

        internal static MeleeWeapon GetUnarmedWeapon(BattleSoldier defender)
        {
            if (defender?.Soldier?.Template?.Species?.DefaultUnarmedWeapon == null)
            {
                return null;
            }

            return new MeleeWeapon(defender.Soldier.Template.Species.DefaultUnarmedWeapon);
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

        private MeleeWeapon ResolveWeapon(BattleSoldier attacker, int weaponTemplateId)
        {
            MeleeWeapon matchingWeapon = attacker.EquippedMeleeWeapons
                .Concat(attacker.MeleeWeapons)
                .FirstOrDefault(weapon => weapon.Template.Id == weaponTemplateId);
            if (matchingWeapon != null)
            {
                return matchingWeapon;
            }

            if (_meleeWeaponTemplates.TryGetValue(weaponTemplateId, out MeleeWeaponTemplate template))
            {
                return new MeleeWeapon(template);
            }

            throw new InvalidOperationException($"Melee weapon template {weaponTemplateId} is not available for attacker {attacker.Soldier.Id}.");
        }

        private void HandleHit(BattleSoldier attacker, MeleeWeapon weapon, BattleSoldier target)
        {
            HitLocation hitLocation = HitLocationCalculator.DetermineHitLocation(target, _random);
            if (!hitLocation.IsSevered)
            {
                float damage = attacker.Soldier.Strength * weapon.Template.StrengthMultiplier
                    * (3.5f + ((float)_random.NextRandomZValue() * 1.75f));
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
