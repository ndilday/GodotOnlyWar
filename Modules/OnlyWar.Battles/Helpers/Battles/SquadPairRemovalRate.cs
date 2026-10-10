using System;
using System.Collections.Generic;
using OnlyWar.Domain.Math;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles
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
    /// <para>THE WOUND LADDER (2026-10-06). The resolver's damage-to-wound-ratio map is affine
    /// (<c>ratio = (d * weaponWoundMultiplier - naturalArmor) * woundMultiplier / constitution</c>),
    /// so the damage at which a hit reaches wound ratio <c>r</c> is
    /// <c>ZeroProgressThreshold + DamagePerRatio * r</c>, with
    /// <c>ZeroProgressThreshold = effectiveArmor + naturalArmor / weaponWoundMultiplier</c> and
    /// <c>DamagePerRatio = constitution / (locationWoundMultiplier * weaponWoundMultiplier)</c>.
    /// Any hit that beats <see cref="EffectiveArmor"/> wounds (a Negligible at least). The
    /// location's wound counter holds at most five wounds of a level, six of which become one of
    /// the next, so it is a base-6 number in Negligible units; <see cref="Gap"/> is how many such
    /// units remain before the location reaches its cripple or sever value. Those numbers are all
    /// <see cref="RemovalMath"/> needs to price the hits that soften a target before the one that
    /// takes it out, in the engine's own currency.</para>
    /// </summary>
    internal readonly struct TakeOutLocationTerm
    {
        internal float Weight { get; }
        internal float PenetrationThreshold { get; }
        internal float RequiredRatio { get; }
        /// <summary>The damage at which this location's wound ratio is 0.</summary>
        internal float ZeroProgressThreshold { get; }
        /// <summary>The damage a hit must beat to wound at all: the worn armor.</summary>
        internal float EffectiveArmor { get; }
        /// <summary>Damage per unit of wound ratio at this location.</summary>
        internal float DamagePerRatio { get; }
        /// <summary>Negligible-wound units left before this location disables.</summary>
        internal float Gap { get; }

        internal TakeOutLocationTerm(
            float weight,
            float penetrationThreshold,
            float requiredRatio,
            float zeroProgressThreshold,
            float effectiveArmor,
            float damagePerRatio,
            float gap)
        {
            Weight = weight;
            PenetrationThreshold = penetrationThreshold;
            RequiredRatio = requiredRatio;
            ZeroProgressThreshold = zeroProgressThreshold;
            EffectiveArmor = effectiveArmor;
            DamagePerRatio = damagePerRatio;
            Gap = gap;
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
    /// would mean calling the targeting stack, which is exactly what the engagement potential's
    /// per-option projections must not do.</para>
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
        /// again once the potential projects the squads inside reach, which is the gradient that
        /// tells an out-of-range squad closing is worth something.
        /// </summary>
        internal float MaximumEffectiveRange { get; }
        /// <summary>Null for a non-degrading weapon, where take-out does not vary with range.</summary>
        internal IReadOnlyList<TakeOutLocationTerm> TakeOutTerms { get; }
        /// <summary>
        /// The aimed shots the shot planner weighs against firing now
        /// (<see cref="RangedTargetSelector.EvaluateFireTiming"/>), one per number of aim turns, at
        /// the reference range and speed: each one's to-hit total, the burst the planner chooses
        /// for it, and the turns the whole cycle takes (aim turns + the shot). Empty when the
        /// weapon cannot be aimed (a cone or other template weapon). Read only by
        /// <see cref="SustainedRateAt"/>.
        /// </summary>
        internal IReadOnlyList<AimPlan> AimPlans { get; }
        /// <summary>
        /// The ammunition scarcity the planner's round charge uses
        /// (<see cref="RangedTargetSelector.AmmunitionScarcity"/>), captured with the term.
        /// </summary>
        internal float AmmunitionScarcity { get; }
        /// <summary>
        /// Rounds a shot spends, as the planner counts them: null for the burst itself, 0 for a
        /// weapon whose supply never runs down, 1 for a consumable item.
        /// </summary>
        internal int? FixedRoundsPerShot { get; }

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
            IReadOnlyList<TakeOutLocationTerm> takeOutTerms,
            IReadOnlyList<AimPlan> aimPlans = null,
            float ammunitionScarcity = 0,
            int? fixedRoundsPerShot = null)
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
            AimPlans = aimPlans ?? Array.Empty<AimPlan>();
            AmmunitionScarcity = Math.Clamp(ammunitionScarcity, 0f, 1f);
            FixedRoundsPerShot = fixedRoundsPerShot;
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
        /// <c>hit * (takeOut + woundProgress) * targetBV</c> currency
        /// <see cref="BattleSquadPlanner.EvaluateRangedTarget"/> reports, so it is directly
        /// commensurable with `outgoing`. Zero beyond <see cref="MaximumEffectiveRange"/>: the
        /// shooter cannot fire at all there. This is the UN-AIMED shot; see
        /// <see cref="SustainedRateAt"/> for the rate that includes aiming.
        /// </summary>
        internal float RemovalAt(float range, float targetSpeed, float shooterBulkMultiplier)
        {
            if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
            {
                return 0f;
            }
            float hitTotal = HitTotalAt(range, targetSpeed)
                - (WeaponTemplate.Bulk * Math.Max(0f, shooterBulkMultiplier));
            // Same burst model as EvaluateRangedTarget -- the doc comment above promises this is
            // the identical currency, so the two must integrate the recoil loop the same way.
            return RemovalMath.ExpectedBurstRemovalFraction(
                hitTotal,
                ReferenceShotsToFire,
                WeaponTemplate.Recoil,
                RemovalFractionAt(range)) * TargetBattleValue;
        }

        internal float RemovalAt(float range, float targetSpeed) =>
            RemovalAt(range, targetSpeed, 0f);

        internal float RemovalAt(float range) => RemovalAt(range, ReferenceTargetSpeed);

        /// <summary>
        /// Expected battle value a STATIONARY shooter removes per turn, sustained, by the plan the
        /// shot planner itself would choose at this range: fire every turn un-aimed, or one of the
        /// aim-then-fire cycles in <see cref="AimPlans"/>. The choice is the planner's rule
        /// (<see cref="RangedTargetSelector.EvaluateFireTiming"/>): each plan's value per turn net
        /// of the round charge (scarcity x the best value per round on offer x the rounds it
        /// spends), firing now when that is at least the best prepared rate. The rate credited is
        /// the chosen plan's GROSS removal per turn -- what it takes off the enemy; the round
        /// charge decides how the soldier fires, not what a turn of it is worth. Both sides stand
        /// in a standing phase, so no target movement is projected.
        ///
        /// <para>HISTORY. Until 2026-10-06 this was the best of up to three extra aim turns at the
        /// un-aimed burst, which credited Neophytes with a fully aimed ten-round burst they never
        /// fired. From then until 2026-10-09 it was one aim turn (product-owner decision), which the
        /// round charge made wrong in both directions: it puts the charge's preferred plan at four
        /// turns of aim for most soldiers, so Neophyte Hybrids were credited 13.4 battle value a
        /// turn and delivered 9.7, and Shoota Boyz were credited 1.6 and delivered 3.9. The
        /// planner then believed the Hybrids out-shot the Orks several times over, and they crept
        /// forward under fire without shooting (Design/Active/BattleValueCalibration.md, 7.21).</para>
        ///
        /// <para>The plans are captured at the reference range and rescaled, so the choice can
        /// change with range: closer in, a shorter aim or firing every turn usually wins.</para>
        /// </summary>
        internal float SustainedRateAt(float range)
        {
            if (MaximumEffectiveRange > 0f && range > MaximumEffectiveRange)
            {
                return 0f;
            }
            float removalFraction = RemovalFractionAt(range);
            float shift = HitTotalAt(range, ReferenceTargetSpeed) - ReferenceHitTotal;
            float shootNow = RemovalMath.ExpectedBurstRemovalFraction(
                ReferenceHitTotal + shift,
                ReferenceShotsToFire,
                WeaponTemplate.Recoil,
                removalFraction) * TargetBattleValue;
            if (AimPlans.Count == 0)
            {
                return shootNow;
            }

            Span<float> gross = stackalloc float[AimPlans.Count];
            float valuePerRound = PerRound(shootNow, ReferenceShotsToFire);
            for (int index = 0; index < AimPlans.Count; index++)
            {
                AimPlan plan = AimPlans[index];
                gross[index] = RemovalMath.ExpectedBurstRemovalFraction(
                    plan.HitTotal + shift,
                    plan.ShotsToFire,
                    WeaponTemplate.Recoil,
                    removalFraction) * TargetBattleValue;
                valuePerRound = Math.Max(valuePerRound, PerRound(gross[index], plan.ShotsToFire));
            }

            float shootNowNet = shootNow
                - AmmunitionScarcity * valuePerRound * RoundsUsed(ReferenceShotsToFire);
            float bestPreparedNet = float.MinValue;
            float bestPreparedGross = 0f;
            for (int index = 0; index < AimPlans.Count; index++)
            {
                AimPlan plan = AimPlans[index];
                float net = (gross[index]
                    - AmmunitionScarcity * valuePerRound * RoundsUsed(plan.ShotsToFire)) / plan.Turns;
                if (net > bestPreparedNet)
                {
                    bestPreparedNet = net;
                    bestPreparedGross = gross[index] / plan.Turns;
                }
            }
            return shootNowNet >= bestPreparedNet ? shootNow : bestPreparedGross;
        }

        private int RoundsUsed(int shots) => FixedRoundsPerShot ?? Math.Max(1, shots);

        private float PerRound(float value, int shots)
        {
            int rounds = RoundsUsed(shots);
            return rounds == 0 ? 0f : Math.Max(0f, value) / rounds;
        }

        private float RemovalFractionAt(float range) =>
            TakeOutTerms == null
                ? RemovalMath.CombineRemovalFraction(ReferenceTakeOut, ReferenceWoundProgress)
                : RemovalMath.EvaluateRemovalFraction(
                    TakeOutTerms,
                    BattleModifiersUtil.CalculateDamageAtRange(WeaponTemplate, range));
    }

    /// <summary>
    /// One aim-then-fire cycle the shot planner weighs: the aimed shot's to-hit total at the
    /// reference range, the burst the planner chooses for it, and the turns the cycle takes.
    /// </summary>
    internal readonly record struct AimPlan(float HitTotal, int ShotsToFire, int Turns);

    /// <summary>
    /// One (shooter squad, target squad) cell of the Phase 4 removal-rate table
    /// (Design/Reference/BattleLogic.md).
    ///
    /// <para>SEMANTICS. The rate is the SUM over the shooter squad's able, placed, ranged-armed
    /// soldiers of each soldier's single best target's expected removal -- the same
    /// per-soldier-argmax aggregation `outgoing` uses in <c>EvaluateImmediateActionValue</c>, and
    /// deliberately NOT an average, because the engagement scorer wants a squad-level battle value
    /// per turn. A soldier contributes to exactly one cell: the one containing the enemy squad its
    /// best target belongs to. A pair with no shooter aimed into it is absent from the table,
    /// meaning rate 0.</para>
    ///
    /// <para>The rate is captured for the STATIONARY, un-aimed, no-bulk reference posture. The
    /// consumer rescales the cached terms for target motion and the enemy's projected bulk. (The
    /// removed bounded policy rollout also applied an explicit per-policy outgoing retention
    /// here.) <see cref="SustainedRateAtRange"/> is the one reading that includes aiming.</para>
    ///
    /// <para>No cap is applied. <c>EngagementExchangeModel.EvaluateOutgoingExchangeRate</c>,
    /// <c>EvaluateSustainedOutgoingRate</c> and <c>EvaluateIncomingExchangeRate</c> clamp to the
    /// defender's <c>TotalAbleBattleValue</c>, and `outgoing` caps per target soldier; leaving the
    /// raw sum here keeps the choice with the consumer.</para>
    ///
    /// <para>PHASE 5 RESOLVED the pair-weights question, asymmetrically. The OUTGOING exchange
    /// rate reads a cell directly with NO <c>PairWeights</c> factor, because this table is
    /// already target-selected and multiplying by a normalized allocation would divide the squad's
    /// fire twice; the INCOMING rate keeps <c>PairWeights</c>, applied to the enemy's whole-squad
    /// rate, because there the allocation is the real question. See
    /// <c>EngagementExchangeModel.EvaluateOutgoingExchangeRate</c>.</para>
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
        /// engagement potential call it for every option x enemy squad x (current, projected)
        /// separation. (It was first sized for a 3-policy x 2-ply x N-squad lookahead, since
        /// removed.)
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

        /// <summary>
        /// <see cref="RateAtRange(float)"/> for a squad that stands and may aim: each term at
        /// <see cref="PairRemovalTerm.SustainedRateAt"/>.
        ///
        /// <para>The STANDING phases of <see cref="EngagementExchangeForecast"/> read it: a squad
        /// that stands can aim and then fire. Read at the un-aimed rate, a sniper rifle (Accuracy
        /// 9) scores almost nothing at long range, and sniper scouts once ran at 'Eavy Nobz for 39
        /// turns without a shot (Grist Nine Epsilon, 2026-09-23).</para>
        ///
        /// <para>The forecast once priced a moving option at this rate on arrival without charging
        /// for the aim that moving throws away, so every step toward the enemy looked like a gain
        /// (a rifle squad in its band left Hold to run at an approaching melee squad). It charges
        /// for it now: the approach phase fires nothing, the move forfeits any stored aim through
        /// the readiness term, and the immediate term fires nothing on a run.</para>
        /// </summary>
        internal float SustainedRateAtRange(float range)
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
                total += term.SustainedRateAt(Math.Max(0f, term.ReferenceRange + shift));
            }
            return total;
        }
    }
}
