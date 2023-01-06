﻿using System;
using OnlyWar.Models.Equippables;

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
    }
}
