using System;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    public static class BattleModifiersUtil
    {
        /// <summary>
        /// Scale of the logarithmic range curve: the to-hit penalty is
        /// <c>RangeModifierCoefficient * ln(2 / (range + relativeTargetSpeed))</c>. Named so the
        /// closed-form range rescaling in <see cref="SquadPairRemovalRate"/> -- which relies on
        /// <c>modifier(r) - modifier(r0) = RangeModifierCoefficient * ln((r0+v0)/(r+v))</c> -- reads
        /// the same constant this function does rather than repeating the literal.
        /// </summary>
        public const float RangeModifierCoefficient = 2.4663f;

        public static float GetRangeForModifier(float modifier)
        {
            return (float)(2 * Math.Exp(-modifier / RangeModifierCoefficient));
        }

        public static float CalculateRangeModifier(float range, float relativeTargetSpeed)
        {
            //
            return (float)(RangeModifierCoefficient * Math.Log(2 / (range + relativeTargetSpeed)));
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
        /// take -<see cref="ThrownRangePenaltyAtMax"/> x (range / effective max range)^2,
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
            return CalculateDamageAtRange(weapon.Template, range);
        }

        /// <summary>
        /// Damage coefficient at a range. NOTE (relied on by the Phase 4 removal-rate table, see
        /// Design/Reference/BattleLogic.md): when DoesDamageDegradeWithRange is false
        /// this is a FLAT DamageMultiplier, so take-out probability is genuinely range-independent
        /// for such a weapon and caching it once at a reference range is exact, not an
        /// approximation. Only the degrading branch needs the per-hit-location rescaling path.
        /// </summary>
        public static float CalculateDamageAtRange(RangedWeaponTemplate template, float range)
        {
            return template.DoesDamageDegradeWithRange ?
                                template.DamageMultiplier * (1 - (range / template.MaximumRange)) :
                                template.DamageMultiplier;
        }

        /// <summary>
        /// The outer edge of this soldier's useful engagement band against a representative
        /// target: the largest range at which it is still doing
        /// <see cref="RangedEffectivenessCurve.SaturationFraction"/> of its best work.
        ///
        /// <para>PHASE 6 (Design/Reference/BattleLogic.md). Now a thin derivation of
        /// <see cref="RangedEffectivenessCurve"/>, the one engagement-range model. It used to be
        /// <c>max over weapons of min(EstimateHitDistance, EstimateKillDistance)</c>; those two
        /// functions and <c>CalculateOpeningDistance</c> are gone. See the type comment on
        /// <see cref="RangedEffectivenessCurve"/> for why a threshold construction had to go -- in
        /// short, EstimateKillDistance returned the weapon's MaximumRange outright for a
        /// non-degrading weapon, so the "optimal" distance was simply reach for most marine small
        /// arms, and EstimateHitDistance returned a hard 0 whenever the to-hit total failed to
        /// reach 10.5, which was the only reason CalculateOpeningDistance had to exist to
        /// disambiguate that 0.</para>
        ///
        /// <para>Return values keep their old meanings, so no caller changed: -1 for a soldier with
        /// no functioning hands, and 0 for "there is nothing to stand off for" -- no usable ranged
        /// weapon, or a target this soldier cannot damage at any range.</para>
        /// </summary>
        public static float CalculateOptimalDistance(BattleSoldier soldier, float targetSize, float targetArmor, float targetCon, float targetRangedEvasion = 0)
        {
            if (soldier.FunctioningHands == 0)
            {
                // with no hands free, there's not much combat left for this soldier
                return -1;
            }
            return RangedEffectivenessCurve
                .Build(
                    [soldier],
                    new EngagementTargetProfile(
                        targetSize, targetArmor, targetCon, targetRangedEvasion))
                .SaturationRange(RangedEffectivenessCurve.SaturationFraction);
        }
    }
}
