using System;
using System.Linq;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    public static class BattleModifiersUtil
    {
        public static float GetRangeForModifier(float modifier)
        {
            return (float)(2 * Math.Exp(-modifier / 2.4663f));
        }

        public static float CalculateRangeModifier(float range, float relativeTargetSpeed)
        {
            // 
            return (float)(2.4663f * Math.Log(2 / (range + relativeTargetSpeed)));
        }

        public static float CalculateSizeModifier(float size)
        {
            // this is just the opposite of the range modifier
            return -CalculateRangeModifier(size, 0);
        }

        public static float CalculateRateOfFireModifier(int rateOfFire)
        {
            if (rateOfFire == 1) return 0;
            return (float)(Math.Log(rateOfFire, 2));
        }

        /// <summary>
        /// A thrown weapon's template stores meters-per-Strength-point in MaximumRange,
        /// so its reach scales with the thrower; every other weapon uses MaximumRange as-is.
        /// </summary>
        public static float GetEffectiveMaxRange(ISoldier soldier, RangedWeaponTemplate template)
        {
            return template.IsThrown
                ? soldier.Strength * template.MaximumRange
                : template.MaximumRange;
        }

        /// <summary>Accuracy penalty at the thrower's maximum range. Balance knob: throws
        /// near the limit of the thrower's Strength are desperate heaves, so the penalty
        /// grows quadratically with the fraction of max range used, not absolute distance.</summary>
        public const float ThrownRangePenaltyAtMax = 12f;

        /// <summary>
        /// The range portion of a blast delivery check. Launched blasts are aimed like
        /// firearms and use the standard logarithmic range curve; thrown blasts instead
        /// take −<see cref="ThrownRangePenaltyAtMax"/> × (range / effective max range)²,
        /// so a stronger thrower is more accurate at the same distance, short lobs are
        /// nearly automatic, and max-range heaves scatter badly.
        /// </summary>
        public static float CalculateBlastRangeModifier(ISoldier soldier, RangedWeaponTemplate template, float range)
        {
            if (!template.IsThrown)
            {
                return CalculateRangeModifier(range, 0f);
            }

            float effectiveMaxRange = GetEffectiveMaxRange(soldier, template);
            if (effectiveMaxRange <= 0)
            {
                return float.MinValue;
            }

            float rangeFraction = range / effectiveMaxRange;
            return -ThrownRangePenaltyAtMax * rangeFraction * rangeFraction;
        }

        public static float CalculateDamageAtRange(RangedWeapon weapon, float range)
        {
            return weapon.Template.DoesDamageDegradeWithRange ?
                                weapon.Template.DamageMultiplier * (1 - (range / weapon.Template.MaximumRange)) :
                                weapon.Template.DamageMultiplier;
        }

        public static float CalculateOptimalDistance(BattleSoldier soldier, float targetSize, float targetArmor, float targetCon, float targetRangedEvasion = 0)
        {
            int freeHands = soldier.Soldier.FunctioningHands;
            if (freeHands == 0)
            {
                // with no hands free, there's not much combat left for this soldier
                return -1;
            }
            float range = 0;
            var weapons = soldier.EquippedRangedWeapons.Where(w => (int)w.Template.Location <= freeHands).OrderByDescending(w => w.Template.MaximumRange);
            foreach (RangedWeapon weapon in weapons)
            {
                float hitRange = EstimateHitDistance(soldier.Soldier, weapon, targetSize, freeHands, targetRangedEvasion);
                float damRange = EstimateKillDistance(weapon, targetArmor, targetCon);
                float minVal = Math.Min(hitRange, damRange);
                if (minVal > range) range = minVal;
            }
            return range;
        }

        public static float EstimateHitDistance(ISoldier soldier, RangedWeapon weapon, float targetSize, int functioningHands, float targetRangedEvasion = 0)
        {
            if ((int)weapon.Template.Location > functioningHands)
            {
                return 0;
            }

            float baseTotal = soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);

            // we'd like to get to a range where at least 1 bullet will hit more often than not when we aim
            // +1 for all-out attack, - ROF after the first shot
            // z value of 0.43 is 
            baseTotal = baseTotal + 1 + weapon.Template.Accuracy;
            baseTotal += BattleModifiersUtil.CalculateRateOfFireModifier(weapon.Template.RateOfFire);
            baseTotal += BattleModifiersUtil.CalculateSizeModifier(targetSize);
            // elusive targets are flatly harder to hit, which pulls the optimal
            // engagement range closer — keeps the AI from over-estimating its reach
            // against Raveners/Genestealers. See Design/EvasionBurrowAndAmbush.md.
            baseTotal -= targetRangedEvasion;
            // if the total doesn't get to 10.5, there will be no range where there's a good chance of hitting, so just keep getting closer
            if (baseTotal < 10.5) return 0;

            return BattleModifiersUtil.GetRangeForModifier(10.5f - baseTotal);
        }

        public static float EstimateKillDistance(RangedWeapon weapon, float targetArmor, float targetCon)
        {
            // if range doesn't matter for damage, we can just limit on hitting 
            if (!weapon.Template.DoesDamageDegradeWithRange) return weapon.Template.MaximumRange;
            float effectiveArmor = targetArmor * weapon.Template.ArmorMultiplier;

            // if there's no chance of doing a wound, maybe we should run?
            if (weapon.Template.DamageMultiplier * 6 < effectiveArmor) return -1;
            //if we can't kill in one shot at point blank range, we still need to get as close as possible to have the best chance of taking the target down
            if ((weapon.Template.DamageMultiplier * 6 - effectiveArmor) * weapon.Template.WoundMultiplier < targetCon) return 0;
            // find the range with a 1/3 chance of a killshot
            float distanceRatio = 1 - (((targetCon / weapon.Template.WoundMultiplier) + effectiveArmor) / (4.25f * weapon.Template.DamageMultiplier));
            if (distanceRatio < 0) return 0;
            return weapon.Template.MaximumRange * distanceRatio;
        }
    }
}
