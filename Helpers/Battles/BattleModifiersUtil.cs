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

        public static float CalculateDamageAtRange(RangedWeapon weapon, float range)
        {
            return weapon.Template.DoesDamageDegradeWithRange ?
                                weapon.Template.DamageMultiplier * (1 - (range / weapon.Template.MaximumRange)) :
                                weapon.Template.DamageMultiplier;
        }

        public static float CalculateOptimalDistance(BattleSoldier soldier, float targetSize, float targetArmor, float targetCon)
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
                float hitRange = EstimateHitDistance(soldier.Soldier, weapon, targetSize, freeHands);
                float damRange = EstimateKillDistance(weapon, targetArmor, targetCon);
                float minVal = Math.Min(hitRange, damRange);
                if (minVal > range) range = minVal;
            }
            return range;
        }

        public static float EstimateHitDistance(ISoldier soldier, RangedWeapon weapon, float targetSize, int freeHands)
        {
            float baseTotal = soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);

            if (weapon.Template.Location == EquipLocation.TwoHand && freeHands == 1)
            {
                // unless the soldier is strong enough, the weapon can't be used one-handed
                if (weapon.Template.RequiredStrength * 1.5f > soldier.Strength) return 0;
                if (weapon.Template.RequiredStrength * 2 > soldier.Strength)
                {
                    baseTotal -= (weapon.Template.RequiredStrength * 2) - soldier.Strength;
                }
            }

            // we'd like to get to a range where at least 1 bullet will hit more often than not when we aim
            // +1 for all-out attack, - ROF after the first shot
            // z value of 0.43 is 
            baseTotal = baseTotal + 1 + weapon.Template.Accuracy;
            baseTotal += BattleModifiersUtil.CalculateRateOfFireModifier(weapon.Template.RateOfFire);
            baseTotal += BattleModifiersUtil.CalculateSizeModifier(targetSize);
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
