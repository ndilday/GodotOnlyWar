using System;
using System.Collections.Generic;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// One hit location's range-INDEPENDENT contribution to take-out probability.
    ///
    /// <para><c>Weight</c> is <c>w_loc</c>, the location's share of the stance's hit lottery.
    /// <c>PenetrationThreshold</c> is <c>K_loc = effectiveArmor + requiredPenetratingDamage</c>;
    /// <see cref="RemovalMath.CalculateTakeOutProbabilityOnHit"/> forms
    /// <c>requiredRoll = K_loc / damageCoefficient</c>, and since the numerator carries no range
    /// term the whole range dependence of take-out lives in <c>damageCoefficient(r)</c>. That is
    /// what makes <see cref="PairRemovalTerm.TakeOutProbabilityAt"/> a fixed-size sum of normal
    /// CDFs instead of a hit-location and wound-state traversal.</para>
    ///
    /// <para><c>RequiredRatio</c> is the wound ratio the location must reach to disable, given the
    /// wounds it already carries.</para>
    ///
    /// <para>PHASE 5. <c>ZeroProgressThreshold</c> is <c>K_zero = effectiveArmor +
    /// naturalArmor / weaponWoundMultiplier</c>: the damage at which this location starts taking
    /// any wound at all. Because the resolver's damage-to-wound-ratio map is AFFINE
    /// (<c>ratio = (d * weaponWoundMultiplier - naturalArmor) * woundMultiplier / constitution</c>),
    /// the fraction of the remaining gap to the disable threshold closed by a hit is linear in the
    /// damage roll between <c>K_zero</c> and <c>PenetrationThreshold</c>. Those two numbers are
    /// therefore all <see cref="RemovalMath.EvaluateWoundProgress"/> needs to price the
    /// twenty hits that soften a target before the twenty-first takes it out -- no wound-state
    /// traversal, and no separate accumulator, because
    /// <see cref="RemovalMath.CalculateTakeOutProbabilityOnHit"/> is already wound-state
    /// aware through <c>RequiredRatio</c>.</para>
    /// </summary>
    internal readonly struct TakeOutLocationTerm
    {
        internal float Weight { get; }
        internal float PenetrationThreshold { get; }
        internal float RequiredRatio { get; }
        /// <summary>K_zero -- the damage at which this location first takes a nonzero wound.</summary>
        internal float ZeroProgressThreshold { get; }

        internal TakeOutLocationTerm(
            float weight,
            float penetrationThreshold,
            float requiredRatio,
            float zeroProgressThreshold)
        {
            Weight = weight;
            PenetrationThreshold = penetrationThreshold;
            RequiredRatio = requiredRatio;
            ZeroProgressThreshold = zeroProgressThreshold;
        }
    }

    /// <summary>
    /// One shooter's expected battle-value removal against one enemy soldier, captured at a
    /// reference range and rescalable to any other range in closed form.
    ///
    /// <para>Hit rescaling is exact up to float rounding. The to-hit total is a sum in which range
    /// enters only through
    /// <see cref="BattleModifiersUtil.CalculateRangeModifier"/> =
    /// <c>k * ln(2/(r+v))</c>, so the total at range <c>r</c> is the reference total plus
    /// <c>k * ln((r0+v0)/(r+v))</c> and
    /// <c>hit(r) = Phi((total0 + k*ln((r0+v0)/(r+v)) - 10.5) / 3)</c> -- one <c>ln</c>, one CDF.</para>
    ///
    /// <para>Take-out rescaling is EXACT for a non-degrading weapon, because
    /// <see cref="BattleModifiersUtil.CalculateDamageAtRange"/> returns a flat
    /// <c>DamageMultiplier</c> in that case and take-out is genuinely range-independent; those
    /// terms carry no location vector at all. A degrading weapon carries the
    /// <see cref="TakeOutLocationTerm"/> vector and re-evaluates the sum at the new damage
    /// coefficient.</para>
    ///
    /// <para>APPROXIMATION, deliberate and documented: the shot count baked into <c>total0</c> (via
    /// the rate-of-fire modifier) is held fixed under rescaling. The live path re-derives shots
    /// from the hit probability, so a rescaled rate can differ from a full re-evaluation for a
    /// burst weapon whose chosen rate of fire would change with range. Rerunning that fixed point
    /// would mean calling the targeting stack, which is exactly what the lookahead must not do.</para>
    /// </summary>
    internal sealed class PairRemovalTerm
    {
        internal int ShooterId { get; }
        internal int TargetId { get; }
        internal RangedWeaponTemplate WeaponTemplate { get; }
        /// <summary>total0 -- the pre-roll to-hit total at (ReferenceRange, ReferenceTargetSpeed).</summary>
        internal float ReferenceHitTotal { get; }
        /// <summary>
        /// The shot count total0's rate-of-fire modifier was taken at, and the cap on how many
        /// hits the burst model may credit. Held fixed under rescaling -- the same deliberate
        /// approximation the class summary documents for total0 itself.
        /// </summary>
        internal int ReferenceShotsToFire { get; }
        /// <summary>r0.</summary>
        internal float ReferenceRange { get; }
        /// <summary>v0 -- the relative target speed the reference range modifier was taken at.</summary>
        internal float ReferenceTargetSpeed { get; }
        /// <summary>takeOut0. Also the answer at every range for a non-degrading weapon.</summary>
        internal float ReferenceTakeOut { get; }
        /// <summary>
        /// PHASE 5. <c>E[woundProgress; no takeout]</c> at the reference range -- like
        /// <see cref="ReferenceTakeOut"/>, exact at every range for a non-degrading weapon.
        /// </summary>
        internal float ReferenceWoundProgress { get; }
        internal float TargetBattleValue { get; }
        /// <summary>
        /// PHASE 5. Reach for THIS shooter with THIS weapon (a thrown weapon's reach scales with
        /// the thrower's Strength). Beyond it the shooter cannot fire at all, so the rescaled rate
        /// must be exactly 0 rather than the smooth Gaussian tail -- and it must become positive
        /// again once the lookahead projects the squads inside reach, which is the gradient that
        /// tells an out-of-range squad closing is worth something.
        /// </summary>
        internal float MaximumEffectiveRange { get; }
        /// <summary>Null for a non-degrading weapon, where take-out does not vary with range.</summary>
        internal IReadOnlyList<TakeOutLocationTerm> TakeOutTerms { get; }

        internal PairRemovalTerm(
            int shooterId,
            int targetId,
            RangedWeaponTemplate weaponTemplate,
            float referenceHitTotal,
            int referenceShotsToFire,
            float referenceRange,
            float referenceTargetSpeed,
            float referenceTakeOut,
            float referenceWoundProgress,
            float targetBattleValue,
            float maximumEffectiveRange,
            IReadOnlyList<TakeOutLocationTerm> takeOutTerms)
        {
            ShooterId = shooterId;
            TargetId = targetId;
            WeaponTemplate = weaponTemplate;
            ReferenceHitTotal = referenceHitTotal;
            ReferenceShotsToFire = referenceShotsToFire;
            ReferenceRange = referenceRange;
            ReferenceTargetSpeed = referenceTargetSpeed;
            ReferenceTakeOut = referenceTakeOut;
            ReferenceWoundProgress = referenceWoundProgress;
            TargetBattleValue = targetBattleValue;
            MaximumEffectiveRange = maximumEffectiveRange;
            TakeOutTerms = takeOutTerms;
        }

        /// <summary>
        /// Stands in for an unbounded to-hit total where the range term is +infinity. Far enough
        /// above <see cref="RemovalMath.HitRollMean"/> that every shot in any burst lands,
        /// but finite, so it can be fed to the same arithmetic every other range uses.
        /// </summary>
        private const float CertainHitTotal = 1_000f;

        /// <summary>
        /// The pre-roll to-hit total rescaled to <paramref name="range"/>. Split out from
        /// <see cref="HitProbabilityAt"/> because the burst model needs the TOTAL, not the
        /// first-shot probability: hit k of a burst is its own threshold on the same margin.
        /// </summary>
        internal float HitTotalAt(float range, float targetSpeed)
        {
            float referenceDenominator = ReferenceRange + ReferenceTargetSpeed;
            float denominator = range + targetSpeed;
            if (denominator <= 0f)
            {
                // ln(2/0) is +infinity: point blank against a stationary target never misses.
                return CertainHitTotal;
            }
            if (referenceDenominator <= 0f)
            {
                // No usable reference to shift from; the captured total is all we know.
                return ReferenceHitTotal;
            }
            return ReferenceHitTotal
                + (BattleModifiersUtil.RangeModifierCoefficient
                    * (float)Math.Log(referenceDenominator / denominator));
        }

        internal float HitProbabilityAt(float range, float targetSpeed)
        {
            return GaussianCalculator.ApproximateNormalCDF(
                (HitTotalAt(range, targetSpeed) - RemovalMath.HitRollMean)
                    / RemovalMath.HitRollStdDev);
        }

        internal float TakeOutProbabilityAt(float range)
        {
            if (TakeOutTerms == null)
            {
                return ReferenceTakeOut;
            }
            return RemovalMath.EvaluateTakeOutProbability(
                TakeOutTerms,
                BattleModifiersUtil.CalculateDamageAtRange(WeaponTemplate, range));
        }

        /// <summary>
        /// Expected enemy battle value this shooter removes from this target in one turn at
        /// <paramref name="range"/> -- the same
        /// <c>hit * (takeOut + lambda * woundProgress) * targetBV</c> currency
        /// <see cref="BattleSquadPlanner.EvaluateRangedTarget"/> reports, so it is directly
        /// commensurable with `outgoing`. Zero beyond <see cref="MaximumEffectiveRange"/>: the
        /// shooter cannot fire at all there.
        /// </summary>
        internal float RemovalAt(float range, float targetSpeed, float shooterBulkMultiplier)
        {
            if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
            {
                return 0f;
            }
            // One pass over the location vector for a degrading weapon; a pair of field reads for
            // a non-degrading one, where both halves are range-independent and already captured.
            float removalFraction = TakeOutTerms == null
                ? RemovalMath.CombineRemovalFraction(
                    ReferenceTakeOut, ReferenceWoundProgress)
                : RemovalMath.EvaluateRemovalFraction(
                    TakeOutTerms,
                    BattleModifiersUtil.CalculateDamageAtRange(WeaponTemplate, range));
            // Same burst model as EvaluateRangedTarget -- the doc comment above promises this is
            // the identical currency, so the two must integrate the recoil loop the same way.
            return RemovalMath.ExpectedBurstRemovalFraction(
                HitTotalAt(range, targetSpeed)
                    - (WeaponTemplate.Bulk * Math.Max(0f, shooterBulkMultiplier)),
                ReferenceShotsToFire,
                WeaponTemplate.Recoil,
                removalFraction)
                * TargetBattleValue;
        }

        internal float RemovalAt(float range, float targetSpeed) =>
            RemovalAt(range, targetSpeed, 0f);

        internal float RemovalAt(float range) => RemovalAt(range, ReferenceTargetSpeed);
    }

    /// <summary>
    /// One (shooter squad, target squad) cell of the Phase 4 removal-rate table
    /// (Design/Reference/BattleLogic.md).
    ///
    /// <para>SEMANTICS. The rate is the SUM over the shooter squad's able, placed, ranged-armed
    /// soldiers of each soldier's single best target's expected removal -- the same
    /// per-soldier-argmax aggregation `outgoing` uses in <c>EvaluateImmediateActionValue</c>, and
    /// deliberately NOT an average, because the lookahead wants a squad-level battle value per
    /// turn. A soldier contributes to exactly one cell: the one containing the enemy squad its best
    /// target belongs to. A pair with no shooter aimed into it is absent from the table, meaning
    /// rate 0.</para>
    ///
    /// <para>The rate is captured for the STATIONARY, un-aimed, no-bulk reference posture. The
    /// lookahead consumer applies its own explicit per-policy outgoing retention and can rescale
    /// the cached terms for target motion and the enemy's projected bulk.</para>
    ///
    /// <para>No cap is applied. <c>BattleSquadPlanner.EvaluateExchangeRate</c> clamps to the
    /// defender's <c>TotalAbleBattleValue</c>, and `outgoing` caps per target soldier; leaving the
    /// raw sum here keeps the choice with the consumer.</para>
    ///
    /// <para>PHASE 5 RESOLVED the pair-weights question, asymmetrically. The OUTGOING half of the
    /// lookahead reads a cell directly with NO <c>PairWeights</c> factor, because this table is
    /// already target-selected and multiplying by a normalized allocation would divide the squad's
    /// fire twice; the INCOMING half keeps <c>PairWeights</c>, applied to the enemy's whole-squad
    /// rate, because there the allocation is the real question. See
    /// <c>BattleSquadPlanner.EvaluateExchangeRate</c>.</para>
    /// </summary>
    internal sealed class SquadPairRemovalRate
    {
        internal int ShooterSquadId { get; }
        internal int TargetSquadId { get; }
        /// <summary>
        /// Representative shooter-to-target distance at capture: the mean of the terms' own
        /// reference ranges. Rescaling shifts every term by <c>range - ReferencePairRange</c>, so
        /// per-soldier geometry offsets within the two squads survive, and
        /// <c>RateAtRange(ReferencePairRange) == ReferenceRate</c> exactly.
        /// </summary>
        internal float ReferencePairRange { get; }
        internal float ReferenceRate { get; }
        internal IReadOnlyList<PairRemovalTerm> Terms { get; }

        internal SquadPairRemovalRate(
            int shooterSquadId,
            int targetSquadId,
            IReadOnlyList<PairRemovalTerm> terms)
        {
            ShooterSquadId = shooterSquadId;
            TargetSquadId = targetSquadId;
            Terms = terms ?? Array.Empty<PairRemovalTerm>();
            float rangeTotal = 0f;
            float rateTotal = 0f;
            for (int index = 0; index < Terms.Count; index++)
            {
                rangeTotal += Terms[index].ReferenceRange;
                rateTotal += Terms[index].RemovalAt(Terms[index].ReferenceRange);
            }
            ReferencePairRange = Terms.Count == 0 ? 0f : rangeTotal / Terms.Count;
            ReferenceRate = rateTotal;
        }

        /// <summary>
        /// Battle value this squad removes from the paired enemy squad per turn at a projected
        /// centroid separation. Closed-form arithmetic over the cached terms: per term one
        /// <c>ln</c>, one normal CDF, and (degrading weapons only) a fixed-size CDF sum. Nothing
        /// here touches the grid, the targeting stack, or wound state, which is what lets the
        /// 3-policy x 2-ply x N-squad lookahead call it.
        /// </summary>
        internal float RateAtRange(float range)
        {
            return RateAtRangeCore(range, true, 0f, 0f);
        }

        internal float RateAtRange(
            float range,
            float targetSpeed,
            float shooterBulkMultiplier)
        {
            return RateAtRangeCore(range, false, targetSpeed, shooterBulkMultiplier);
        }

        private float RateAtRangeCore(
            float range,
            bool useReferenceTargetSpeed,
            float targetSpeed,
            float shooterBulkMultiplier)
        {
            if (Terms.Count == 0)
            {
                return 0f;
            }
            float shift = range - ReferencePairRange;
            float total = 0f;
            for (int index = 0; index < Terms.Count; index++)
            {
                PairRemovalTerm term = Terms[index];
                total += term.RemovalAt(
                    Math.Max(0f, term.ReferenceRange + shift),
                    useReferenceTargetSpeed ? term.ReferenceTargetSpeed : targetSpeed,
                    shooterBulkMultiplier);
            }
            return total;
        }
    }
}
