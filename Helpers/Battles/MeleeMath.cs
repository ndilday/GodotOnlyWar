using OnlyWar.Models.Equippables;

using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles
{
    public static class MeleeMath
    {
        public const float DefaultAttackSpeedMultiplier = MeleeWeaponTemplate.DefaultAttackSpeedMultiplier;
        public const float TakeOutConfidenceTarget = 0.75f;

        public static float CalculateBaseAttackCount(float attackSpeed, float attackSpeedMultiplier)
        {
            if (attackSpeed <= 0 || attackSpeedMultiplier <= 0)
            {
                return 0;
            }

            return (attackSpeed / 10.0f) * attackSpeedMultiplier;
        }

        public static int CalculateGuaranteedAttackCount(float attackSpeed,
                                                         float attackSpeedMultiplier,
                                                         int dualWieldBonusAttacks = 0)
        {
            float attackCount = CalculateBaseAttackCount(attackSpeed, attackSpeedMultiplier);
            return Math.Max(0, (int)Math.Floor(attackCount) + dualWieldBonusAttacks);
        }

        public static float CalculateFractionalAttackChance(float attackSpeed, float attackSpeedMultiplier)
        {
            float attackCount = CalculateBaseAttackCount(attackSpeed, attackSpeedMultiplier);
            return attackCount - (float)Math.Floor(attackCount);
        }

        public static float CalculateContestedHitProbability(float attackSkill,
                                                             float weaponAccuracy,
                                                             bool didMove,
                                                             float defenderSkill,
                                                             float defenderEvasion,
                                                             float defenderParryModifier = 0,
                                                             float defenderAdvantage = Actions.MeleeAttackAction.MeleeDefenderAdvantage)
        {
            float meanDelta = attackSkill + weaponAccuracy + (didMove ? -2 : 0)
                              - defenderSkill - defenderEvasion - defenderAdvantage
                              - defenderParryModifier;
            float standardDeviation = (float)Math.Sqrt(18.0);
            return StandardNormalCdf(meanDelta / standardDeviation);
        }

        public static int CalculateTrialsForCumulativeSuccess(float independentSuccessProbability,
                                                              float targetConfidence = TakeOutConfidenceTarget)
        {
            if (independentSuccessProbability <= 0)
            {
                return int.MaxValue;
            }

            if (independentSuccessProbability >= 1 || targetConfidence <= 0)
            {
                return 1;
            }

            if (targetConfidence >= 1)
            {
                return int.MaxValue;
            }

            return (int)Math.Ceiling(Math.Log(1 - targetConfidence) / Math.Log(1 - independentSuccessProbability));
        }

        public static float SumParryModifiers(IEnumerable<MeleeWeapon> equippedWeapons)
        {
            if (equippedWeapons == null)
            {
                return 0;
            }

            float total = 0;
            foreach (MeleeWeapon weapon in equippedWeapons)
            {
                total += weapon?.Template?.ParryModifier ?? 0;
            }
            return total;
        }

        private static float StandardNormalCdf(float z)
        {
            double sign = z < 0 ? -1 : 1;
            double absoluteZ = Math.Abs(z);
            double t = 1.0 / (1.0 + 0.2316419 * absoluteZ);
            double density = 0.3989422804014327 * Math.Exp((-absoluteZ * absoluteZ) / 2.0);
            double polynomial = (((((1.330274429 * t) - 1.821255978) * t) + 1.781477937) * t - 0.356563782) * t + 0.319381530;
            double cdf = 1.0 - (density * polynomial * t);

            return (float)(sign > 0 ? cdf : 1.0 - cdf);
        }
    }
}
