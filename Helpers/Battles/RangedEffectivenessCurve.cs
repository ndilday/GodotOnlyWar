using System;
using System.Collections.Generic;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// A representative target for the engagement-range model: the scalar profile every one of the
    /// four collapsed range approximations already reduced its opponent to.
    /// </summary>
    internal readonly struct EngagementTargetProfile
    {
        internal float Size { get; }
        internal float Armor { get; }
        internal float Constitution { get; }
        internal float RangedEvasion { get; }
        /// <summary>
        /// Battle value of one representative target. Only a scale factor for a single curve, but
        /// it is what makes an outgoing curve and an incoming curve commensurable.
        /// </summary>
        internal float BattleValue { get; }

        internal EngagementTargetProfile(
            float size,
            float armor,
            float constitution,
            float rangedEvasion,
            float battleValue = 1f)
        {
            Size = size;
            Armor = armor;
            Constitution = constitution;
            RangedEvasion = rangedEvasion;
            BattleValue = battleValue;
        }
    }

    /// <summary>
    /// PHASE 6 (Design/Reference/BattleLogic.md). THE engagement-range model.
    ///
    /// <para>Expected battle value removed per turn, as a smooth function of range, by a set of
    /// shooters firing at a representative target. It replaces four parallel approximations of the
    /// same quantity -- <c>EstimateHitDistance</c>, <c>EstimateKillDistance</c>,
    /// <c>CalculateOptimalDistance</c>'s <c>min(hitRange, killRange)</c> construction and
    /// <c>CalculateOpeningDistance</c>'s hit-limited/wound-limited disambiguation branch -- with one
    /// curve those questions are all asked of.</para>
    ///
    /// <para>WHY THE THRESHOLD MODEL HAD TO GO. Each of the four functions returned a single
    /// DISTANCE derived from a threshold: the range where the to-hit total crosses 10.5, or where a
    /// one-third-quantile damage roll stops penetrating. Thresholds give cliffs, and the cliffs
    /// were the bugs. <c>EstimateKillDistance</c> returned the weapon's <c>MaximumRange</c> outright
    /// for a non-degrading weapon, so <c>min(hitRange, killRange)</c> collapsed to reach for most
    /// marine small arms; and <c>CalculateOptimalDistance</c> returned exactly 0 for a weapon whose
    /// to-hit total never reaches 10.5, which is the only reason
    /// <c>CalculateOpeningDistance</c> had to exist to disambiguate that 0. A curve has neither
    /// cliff: a weapon that hits 20% of the time at 400 yards scores 20%, not zero.</para>
    ///
    /// <para>SHAPE. Per shooter and weapon,
    /// <c>removal(r) = Phi((total0 + k*ln(2/r) - 10.5) / 3) * removalFraction(r) * targetBV</c>,
    /// gated to 0 beyond that shooter's reach. This is the SAME
    /// <c>hit * (takeOut + lambda * woundProgress) * BV</c> currency Phase 4/5 put `outgoing`,
    /// `future` and <see cref="SquadPairRemovalRate.RateAtRange"/> in; the removal fraction is
    /// evaluated by <see cref="RemovalMath.EvaluateRemovalFraction"/> itself, over a single
    /// synthetic <see cref="TakeOutLocationTerm"/> built from the representative target's scalar
    /// armor and constitution. Sharing that evaluator is what keeps the standing invariant --
    /// squads must not stand off to plink at a target they cannot damage -- true here by
    /// construction rather than by a second implementation of it.</para>
    ///
    /// <para>A soldier contributes the best of its weapons at each range (max), and a force
    /// contributes the sum over its soldiers -- the same per-soldier-argmax-then-sum aggregation
    /// <see cref="SquadPairRemovalRate"/> uses.</para>
    ///
    /// <para>DIFFERENCE FROM <see cref="SquadPairRemovalRate"/>, and why both exist. That table is
    /// built from REAL targets through the targeting stack and the grid, so it is only available
    /// inside <see cref="BattleSquadPlanner"/> mid-battle. This curve is built from a scalar
    /// representative profile and nothing else, so it is available to
    /// <see cref="BattleEngagementFrameBuilder"/> (RNG-free, grid-free, no planner) and to mission
    /// setup before a grid exists. They agree in form and in currency; this one trades exact
    /// hit-location structure for being computable where the range question is actually asked.</para>
    /// </summary>
    internal sealed class RangedEffectivenessCurve
    {
        /// <summary>
        /// Fraction of its own peak removal a force must still be achieving for a range to count as
        /// inside its useful band: "still doing real work here, even if well off its best".
        ///
        /// <para>0.1 SINCE 2026-08-05, down from 0.5. The consumers of
        /// <see cref="BattleModifiersUtil.CalculateOptimalDistance"/> all ask a CAPABILITY question
        /// -- "can these guns still do something from here" -- and 0.5 answered a PREFERENCE
        /// question instead. Xibarrus Nu (2026-08-05) is the case: on turn 11 the Genestealer force
        /// broke off at 42.9% battle value, and the pursuit gate computed a worthwhile reach of
        /// roughly 736 against a separation of 925, declared no shot available, and ended the battle
        /// -- on the same turn the resolver logged those marines shooting the withdrawers at 925 for
        /// 53 battle value over two rounds. The band was not wrong about 925 being off-peak; it was
        /// being read as though off-peak meant harmless.</para>
        ///
        /// <para>A 0.9 band was tried first, long before that, and pulled degrading weapons in to
        /// near contact, because their damage falloff is linear and 90% of peak is a narrow window.
        /// The direction of travel has been the same both times: every consumer wants the OUTER edge
        /// of usefulness, and each tightening of this fraction has understated it. The floor at
        /// <see cref="NegligibleRemovalFraction"/> is what stops 0.1 from degenerating into
        /// plinking -- a curve whose peak never clears that floor returns 0 here regardless of this
        /// fraction, so "10% of peak" can never mean "10% of nothing".</para>
        ///
        /// <para>The old threshold model had no such knob because it had no curve to take a
        /// fraction of. It asked where the to-hit total crossed a fixed 10.5 -- a hit probability of
        /// exactly 0.5, taking no account of how much a hit is worth -- and, for damage, where a
        /// hand-placed one-third-quantile damage roll stopped penetrating.</para>
        /// </summary>
        internal const float SaturationFraction = 0.2f;

        /// <summary>
        /// SAMPLING BUDGET. 33 coarse samples spanning [1, reach] locate the peak (or the outermost
        /// range still above the saturation threshold) globally -- a coarse GLOBAL sweep rather than
        /// a hill climb, because <c>removal(r) - incoming(r)</c> is not guaranteed unimodal once a
        /// degrading weapon's damage falloff and a melee threat's arrival term are both in play.
        /// 17 more samples refine inside the bracketing coarse interval, giving a resolution of
        /// reach/512 (about 2 yards at a bolter's 1000). 50 evaluations total, each of them closed
        /// form: one ln, one normal CDF, and for a degrading weapon one more CDF pair. This runs
        /// once per squad per turn (Phase 4's standing constraint) and never walks hit locations per
        /// sample -- the location vector is a single term built once at construction.
        /// </summary>
        private const int CoarseSampleCount = 33;
        private const int RefineSampleCount = 17;

        /// <summary>
        /// Below this share of the target's battle value removed per turn, a curve is treated as
        /// flat zero: nothing here is worth standing off for.
        ///
        /// <para>Expressed as a FRACTION of target battle value rather than an absolute, because
        /// the invariant it enforces is about rate, not magnitude: at a thousandth of a target's
        /// battle value per turn a fight takes a thousand turns, which is the definition of the
        /// plinking the design doc's standing invariant forbids. The Gaussian damage model has no
        /// hard impenetrability -- the old <c>EstimateKillDistance</c> got a crisp -1 from
        /// <c>DamageMultiplier * 6 &lt; effectiveArmor</c>, a top-of-the-die cutoff, whereas a curve
        /// gives armour it cannot beat a 1-in-10,000 tail instead of a zero. This floor is where
        /// that tail stops counting as a reason to choose a range.</para>
        /// </summary>
        internal const float NegligibleRemovalFraction = 0.001f;

        private sealed class WeaponCurve
        {
            internal float BaseHitTotal;
            internal RangedWeaponTemplate Template;
            internal float MaximumEffectiveRange;
            internal float TargetBattleValue;
            internal bool Degrades;
            internal IReadOnlyList<TakeOutLocationTerm> Locations;
            internal float FlatRemovalFraction;

            internal float RemovalAt(float range)
            {
                if (range > MaximumEffectiveRange)
                {
                    return 0f;
                }
                float fraction = Degrades
                    ? RemovalMath.EvaluateRemovalFraction(
                        Locations,
                        BattleModifiersUtil.CalculateDamageAtRange(Template, range))
                    : FlatRemovalFraction;
                if (fraction <= 0f)
                {
                    return 0f;
                }
                float total = BaseHitTotal
                    + BattleModifiersUtil.CalculateRangeModifier(range, 0f);
                // The burst model, matching EvaluateRangedTarget and PairRemovalTerm.RemovalAt --
                // this curve is only useful because it is in the same currency as those two.
                // BaseHitTotal already carries the rate-of-fire modifier for the full RateOfFire,
                // so that is the shot count the recoil loop is integrated over here.
                return RemovalMath.ExpectedBurstRemovalFraction(
                        total,
                        Template.RateOfFire,
                        Template.Recoil,
                        fraction)
                    * TargetBattleValue;
            }
        }

        /// <summary>
        /// One or more shooters whose weapon curves are interchangeable, and how many of them there
        /// are.
        ///
        /// <para>WHY. <see cref="RemovalAt"/> is a plain SUM over shooters, and a
        /// <see cref="WeaponCurve"/> depends on the shooter through exactly two scalars --
        /// <c>BaseHitTotal</c> and <c>MaximumEffectiveRange</c>. Everything else in it
        /// (<c>Locations</c>, <c>FlatRemovalFraction</c>, <c>TargetBattleValue</c>) is a function of
        /// the weapon template and the one representative target profile the whole curve is built
        /// against, so two soldiers carrying the same weapons with near-enough skill produce
        /// numerically indistinguishable curves and can be evaluated once and counted twice.</para>
        ///
        /// <para>This is the one place the engagement-range model is APPROXIMATE by choice, and it
        /// pays off in exactly the case that hurt: the incoming curve is built over EVERY soldier in
        /// the opposing force, so a hundred-strong horde of identically-equipped cultists cost a
        /// hundred curve evaluations per sample, times fifty samples per sweep, times several
        /// sweeps per squad per turn. Bucketing collapses that to the number of distinct loadouts.
        /// A hand-built force of individuals buckets to itself and pays nothing.</para>
        /// </summary>
        private sealed class ShooterGroup
        {
            internal List<WeaponCurve> Weapons;
            internal int Count;
        }

        /// <summary>
        /// How far two shooters' pre-roll to-hit totals may differ and still share a bucket.
        ///
        /// <para>The total enters only as <c>Phi((total + rangeModifier - 10.5) / 3)</c>, whose
        /// derivative peaks at <c>phi(0)/3</c> = 0.133, so a quarter point of skill moves a hit
        /// probability by at most 0.033 and the removal it scales by less than that. That sits an
        /// order of magnitude under the 0.5-of-peak and 0.1-of-peak thresholds this curve's
        /// consumers actually test against, and it is a bias-free rounding rather than a systematic
        /// one: members join the bucket of the first shooter within tolerance, in soldier-id order,
        /// so the representative is as likely to be above the group as below.</para>
        /// </summary>
        private const float HitTotalBucket = 0.25f;

        /// <summary>
        /// Reach tolerance for the same bucket, in yards. Reach is a hard gate rather than a smooth
        /// factor -- <see cref="WeaponCurve.RemovalAt"/> returns 0 past it -- so this is kept tight;
        /// it exists only so thrown weapons, whose reach scales with the thrower's Strength, still
        /// bucket among throwers of equal strength.
        /// </summary>
        private const float ReachBucket = 1f;

        private readonly List<ShooterGroup> _shooters;
        private readonly float _negligibleRemoval;

        /// <summary>The furthest range at which any of these shooters can fire at all.</summary>
        internal float Reach { get; }

        internal bool IsEmpty => _shooters.Count == 0;

        private RangedEffectivenessCurve(
            List<ShooterGroup> shooters,
            float reach,
            float targetBattleValue)
        {
            _shooters = shooters;
            Reach = reach;
            _negligibleRemoval = NegligibleRemovalFraction * Math.Max(1f, targetBattleValue);
        }

        /// <summary>
        /// Expected battle value these shooters remove from the representative target per turn at
        /// <paramref name="range"/>. Each shooter contributes its best weapon at that range.
        /// </summary>
        internal float RemovalAt(float range)
        {
            float total = 0f;
            for (int shooter = 0; shooter < _shooters.Count; shooter++)
            {
                ShooterGroup group = _shooters[shooter];
                List<WeaponCurve> weapons = group.Weapons;
                float best = 0f;
                for (int weapon = 0; weapon < weapons.Count; weapon++)
                {
                    float removal = weapons[weapon].RemovalAt(range);
                    if (removal > best)
                    {
                        best = removal;
                    }
                }
                total += best * group.Count;
            }
            return total;
        }

        internal static RangedEffectivenessCurve Build(
            IEnumerable<BattleSoldier> shooters,
            EngagementTargetProfile target)
        {
            List<ShooterGroup> built = [];
            float reach = 0f;
            foreach (BattleSoldier soldier in shooters ?? Array.Empty<BattleSoldier>())
            {
                int freeHands = soldier?.FunctioningHands ?? 0;
                if (freeHands <= 0)
                {
                    continue;
                }
                List<WeaponCurve> weapons = [];
                foreach (RangedWeapon weapon in soldier.EquippedRangedWeapons)
                {
                    if ((int)weapon.Template.Location > freeHands)
                    {
                        continue;
                    }
                    WeaponCurve curve = BuildWeaponCurve(soldier, weapon, target, freeHands);
                    if (curve == null)
                    {
                        continue;
                    }
                    weapons.Add(curve);
                    if (curve.MaximumEffectiveRange > reach)
                    {
                        reach = curve.MaximumEffectiveRange;
                    }
                }
                if (weapons.Count == 0)
                {
                    continue;
                }
                ShooterGroup bucket = null;
                for (int index = 0; index < built.Count; index++)
                {
                    if (Interchangeable(built[index].Weapons, weapons))
                    {
                        bucket = built[index];
                        break;
                    }
                }
                if (bucket == null)
                {
                    built.Add(new ShooterGroup { Weapons = weapons, Count = 1 });
                }
                else
                {
                    bucket.Count++;
                }
            }
            return new RangedEffectivenessCurve(built, reach, target.BattleValue);
        }

        /// <summary>
        /// Whether a shooter's weapon curves may be evaluated as a copy of an existing bucket's.
        /// Linear scan over buckets rather than a hashed key: the bucket count is the number of
        /// distinct loadouts in a force (single digits in practice, and 1 for the single-soldier
        /// curve <see cref="BattleModifiersUtil.CalculateOptimalDistance"/> builds), and an
        /// approximate match on two floats has no exact key to hash anyway.
        /// </summary>
        private static bool Interchangeable(
            List<WeaponCurve> bucket,
            List<WeaponCurve> candidate)
        {
            if (bucket.Count != candidate.Count)
            {
                return false;
            }
            for (int index = 0; index < bucket.Count; index++)
            {
                WeaponCurve left = bucket[index];
                WeaponCurve right = candidate[index];
                // Reference equality on the template settles Locations, FlatRemovalFraction,
                // RateOfFire, Recoil and the degradation branch in one comparison -- all of them
                // are derived from the template and the shared target profile.
                if (!ReferenceEquals(left.Template, right.Template)
                    || MathF.Abs(left.BaseHitTotal - right.BaseHitTotal) > HitTotalBucket
                    || MathF.Abs(left.MaximumEffectiveRange - right.MaximumEffectiveRange)
                        > ReachBucket)
                {
                    return false;
                }
            }
            return true;
        }

        private static WeaponCurve BuildWeaponCurve(
            BattleSoldier soldier,
            RangedWeapon weapon,
            EngagementTargetProfile target,
            int freeHands)
        {
            RangedWeaponTemplate template = weapon.Template;
            float maximumRange = BattleModifiersUtil.GetEffectiveMaxRange(
                soldier.Soldier, template);
            if (maximumRange <= 0)
            {
                return null;
            }
            // Identical assembly to the deleted EstimateHitDistance, minus its inversion step:
            // skill + all-out-attack + accuracy + rate of fire + target size - target evasion,
            // with the range modifier left OUT so it can be added per sample.
            float baseHitTotal = soldier.Soldier.GetTotalSkillValue(template.RelatedSkill)
                + 1f
                + template.Accuracy
                + BattleModifiersUtil.CalculateRateOfFireModifier(template.RateOfFire)
                + BattleModifiersUtil.CalculateSizeModifier(target.Size)
                - target.RangedEvasion;

            float effectiveArmor = target.Armor * template.ArmorMultiplier;
            float woundMultiplier = Math.Max(0.0001f, template.WoundMultiplier);
            // A single synthetic hit location, which is all the scalar representative profile
            // supports: K = effectiveArmor + constitution/woundMultiplier is the damage that
            // disables it, K_zero = effectiveArmor the damage at which it starts taking wounds at
            // all. Same meaning as TakeOutLocationTerm's fields, so the shared evaluator applies.
            TakeOutLocationTerm[] locations =
            [
                new TakeOutLocationTerm(
                    1f,
                    effectiveArmor + (Math.Max(0f, target.Constitution) / woundMultiplier),
                    1f,
                    effectiveArmor)
            ];
            bool degrades = template.DoesDamageDegradeWithRange;
            return new WeaponCurve
            {
                BaseHitTotal = baseHitTotal,
                Template = template,
                MaximumEffectiveRange = maximumRange,
                TargetBattleValue = Math.Max(1f, target.BattleValue),
                Degrades = degrades,
                Locations = locations,
                FlatRemovalFraction = degrades
                    ? 0f
                    : RemovalMath.EvaluateRemovalFraction(
                        locations,
                        BattleModifiersUtil.CalculateDamageAtRange(template, 0f))
            };
        }

        /// <summary>
        /// The outer edge of the band where these shooters are still doing
        /// <paramref name="fraction"/> of their best work: the largest range whose removal is at
        /// least that share of the curve's peak.
        ///
        /// <para>DEGENERATE CASES, all returning 0 -- "there is nothing to stand off for, close" --
        /// which is the meaning <c>CalculateOptimalDistance</c> has always given 0: no usable ranged
        /// weapon (an empty curve), no range at which the target can be reached, and the standing
        /// invariant case of a target that cannot be penetrated at any range, where the removal
        /// fraction is ~0 everywhere and the peak never clears
        /// <see cref="NegligibleRemoval"/>.</para>
        /// </summary>
        internal float SaturationRange(float fraction)
        {
            if (IsEmpty || Reach <= 0f)
            {
                return 0f;
            }
            float lower = Math.Min(1f, Reach);
            float step = (Reach - lower) / (CoarseSampleCount - 1);
            if (step <= 0f)
            {
                return RemovalAt(lower) > _negligibleRemoval ? lower : 0f;
            }
            float peak = 0f;
            float[] samples = new float[CoarseSampleCount];
            for (int index = 0; index < CoarseSampleCount; index++)
            {
                samples[index] = RemovalAt(lower + (step * index));
                if (samples[index] > peak)
                {
                    peak = samples[index];
                }
            }
            if (peak <= _negligibleRemoval)
            {
                return 0f;
            }
            float threshold = peak * fraction;
            int last = -1;
            for (int index = CoarseSampleCount - 1; index >= 0; index--)
            {
                if (samples[index] >= threshold)
                {
                    last = index;
                    break;
                }
            }
            if (last < 0)
            {
                return 0f;
            }
            float best = lower + (step * last);
            if (last == CoarseSampleCount - 1)
            {
                return best;
            }
            // Refine inside the one coarse interval where the curve crosses the threshold.
            float refineStep = step / (RefineSampleCount - 1);
            for (int index = 1; index < RefineSampleCount; index++)
            {
                float range = best + (refineStep * index);
                if (RemovalAt(range) >= threshold)
                {
                    continue;
                }
                return best + (refineStep * (index - 1));
            }
            return best + step;
        }

        /// <summary>
        /// Argmax of an arbitrary closed-form score over [1, <paramref name="upper"/>], on the
        /// sampling budget documented on <see cref="CoarseSampleCount"/>: a global coarse sweep
        /// followed by one refinement pass inside the bracketing interval. Deliberately not a hill
        /// climb -- <c>removal(r) - incoming(r)</c> need not be unimodal.
        /// </summary>
        internal static float Argmax(float upper, Func<float, float> score)
        {
            float lower = Math.Min(1f, upper);
            if (upper <= lower)
            {
                return lower;
            }
            float step = (upper - lower) / (CoarseSampleCount - 1);
            float bestRange = lower;
            float bestScore = score(lower);
            for (int index = 1; index < CoarseSampleCount; index++)
            {
                float range = lower + (step * index);
                float value = score(range);
                if (value > bestScore)
                {
                    bestScore = value;
                    bestRange = range;
                }
            }
            float refineLower = Math.Max(lower, bestRange - step);
            float refineUpper = Math.Min(upper, bestRange + step);
            float refineStep = (refineUpper - refineLower) / (RefineSampleCount - 1);
            if (refineStep <= 0f)
            {
                return bestRange;
            }
            for (int index = 0; index < RefineSampleCount; index++)
            {
                float range = refineLower + (refineStep * index);
                float value = score(range);
                if (value > bestScore)
                {
                    bestScore = value;
                    bestRange = range;
                }
            }
            return bestRange;
        }
    }
}
