using System;
using System.Collections.Generic;
using OnlyWar.Domain.Math;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Battles
{
    /// <summary>
    /// The removal currency every battle scorer is denominated in: how much of a target's battle
    /// value one landed hit, or one burst, is credited with taking off the board.
    ///
    /// <para>Stateless by construction. Nothing here reads the grid, the soldier map, the RNG or
    /// any planner state -- it is a closed-form mirror of what the resolver's attack actions do at
    /// execution time, so planning and execution cannot drift. That is why it lives outside
    /// <see cref="BattleSquadPlanner"/>: <see cref="SquadPairRemovalRate"/>,
    /// <see cref="RangedEffectivenessCurve"/>, <see cref="BattleEngagementFrameBuilder"/> and the
    /// planner itself are all peers of this math rather than clients of the planner.</para>
    /// </summary>
    internal static class RemovalMath
    {
        // The execution-time damage roll, shared by every attack resolver in the engine
        // (ShootAction, AreaAttackAction, MeleeAttackAction, BlastAttackAction): the weapon's
        // damage coefficient is scaled by (mean + z * stdDev). The planner's wound estimates
        // integrate over this roll so armored figures carry their real armor-penetrating tail
        // instead of being scored invulnerable at the mean.
        internal const float DamageRollMean = 3.5f;
        internal const float DamageRollStdDev = 1.75f;
        // The success roll every to-hit estimate is measured against: hit = Phi((total - 10.5)/3).
        // Named so the Phase 4 closed-form range rescaling reads the same numbers the direct
        // estimate does. See Design/Reference/BattleLogic.md.
        internal const float HitRollMean = 10.5f;
        internal const float HitRollStdDev = 3f;

        /// <summary>
        /// Standardized cutoff past which a SUBSEQUENT shot in a burst stops being evaluated:
        /// Phi(-6) is under 1e-9. Applies only to shots after the first -- see
        /// <see cref="ExpectedBurstRemovalFraction"/> for why the opening shot's tail is
        /// load-bearing and must not be cut off at all.
        /// </summary>
        private const float DeepTailZ = -6f;

        /// <summary>
        /// The mirror of <see cref="DeepTailZ"/> on the certain side: past it
        /// <see cref="GaussianCalculator.ApproximateNormalCDF"/> returns exactly 1f, because its
        /// upper tail is under 1e-9 there and float spacing below 1.0 is 6e-8. Testing z against
        /// this instead of calling the CDF to be handed a 1 back is bit-identical, not an
        /// approximation -- see <see cref="ExpectedBurstRemovalFraction"/>.
        /// </summary>
        private const float CertainZ = 6f;

        /// <summary>
        /// The damage roll at <see cref="CertainZ"/> standard deviations above the mean: past this
        /// multiple of the damage coefficient a threshold is beyond anything the roll produces.
        /// A location whose take-out AND wound-onset thresholds both sit out here contributes
        /// nothing to either term, which is the standing "cannot damage it at all" case -- see
        /// <see cref="SkipLocation"/>.
        /// </summary>
        private const float DeepTailRoll = DamageRollMean + (CertainZ * DamageRollStdDev);

        // ===================================================================================
        // GRADED CREDIT FROM THE WOUND LADDER (2026-10-06; replaces Phase 5's lambda).
        //
        //   removal per hit = P(takeout) + combined location softening per hit
        //
        // Phase 5 credited a hit with lambda (0.5) times the fraction of the remaining wound RATIO
        // it closed, summed over hit locations. Two errors made that several times too generous
        // for many weak hits against armour: the engine's wound counter is a base-6 number, not a
        // ratio scale -- a Moderate wound (ratio 0.25) is 1/36 of a Critical, not a quarter of one --
        // and the sum over locations ignored that a disable needs the wounds in ONE location.
        // Measured 2026-10-06 (Design/Reference/BattleLogic.md §5.3): Neophyte
        // autoguns against Scouts at 150 yards were credited about 22 hits per Scout; the engine
        // takes 89 (HardnessDiagnostics).
        //
        // Now each location's fill per hit is the expected wound value of the hit in Negligible
        // units (6^level, capped at the gap) over the gap left to its cripple or sever value --
        // exact for the counter, from eight normal CDFs of the damage roll. A disabling hit counts
        // as the whole gap, i.e. as the take-out chance; the rest of the fill is softening. Expected
        // hits to disable is about 1 / (take-out + combined softening), so removal per hit is that
        // sum. How the softening combines across locations is LocationFillCombineExponent below.
        //
        // The invariant Phase 5 kept still holds: a target the roll cannot penetrate fills nothing,
        // so a squad is never paid for firing at it.
        //
        /// <summary>
        /// How the locations' SOFTENING combines into one per-hit removal fraction: the q-norm
        /// <c>(sum rate_i^q)^(1/q)</c> of each location's fill per hit beyond its one-hit take-out
        /// chance. One-hit take-out chances simply add (they are separate outcomes of the same hit);
        /// softening competes, because the first location to fill decides -- q = 1 would add it like
        /// independent chances, q = infinity would keep only the fastest location. Chosen by
        /// matching expected hits to disable against the engine's own, simulated hit by hit
        /// (Tools/OnlyWar.Diagnostics HardnessDiagnostics, which prints both; 2026-10-06, seven
        /// squads against each other at 60 yards, 187 weapon-target cells, the estimate averaged
        /// over the same generated targets): root-mean-square error factor 1.39 at q = 1, 1.25 at
        /// q = 2, 1.27 at q = 4. The worst cells at q = 2 are hits that almost never penetrate (an
        /// autopistol against power armour, about 20,000 hits measured), where the estimate is
        /// about 5x pessimistic and no decision turns on it.
        /// </summary>
        internal const float LocationFillCombineExponent = 2f;
        // ===================================================================================

        /// <summary>
        /// Probability that one landed hit removes the target from the fight. This mirrors the
        /// resolver's location lottery, armor and normal damage roll, wound-level conversion,
        /// accumulated wound carry, motive/vital thresholds, and last-functioning-hand rule.
        /// <paramref name="wornArmor"/> is the target's worn armor before penetration; each
        /// location adds its natural armor to it and the sum is scaled by
        /// <paramref name="armorMultiplier"/>, as the attack actions do.
        /// </summary>
        internal static float CalculateTakeOutProbabilityOnHit(
            BattleSoldier target,
            float damageCoefficient,
            float wornArmor,
            float armorMultiplier,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return 0f;
            }
            return AccumulateTakeOutTerms(
                target, wornArmor, armorMultiplier, weaponWoundMultiplier, damageCoefficient, null)
                .TakeOut;
        }

        /// <summary>
        /// The fraction of a target's battle value one landed hit is credited with removing: about
        /// 1 / (expected hits to disable it from its current wounds), and never less than the
        /// chance this hit takes it out.
        ///
        /// <para>WHY. <see cref="CalculateTakeOutProbabilityOnHit"/> is wound-state aware --
        /// <c>FindMinimumDisablingWoundRatio</c> reads the wounds a location already carries, so
        /// take-out rises as a target is softened. Scoring only that finishing blow priced a squad
        /// that cannot one-shot anything at ~0 for shooting (Phase 5); the graded half credits the
        /// hits that soften the target, measured on the engine's wound counter (see the block at
        /// the top of this class).</para>
        ///
        /// <para>INVARIANT (Design doc "Invariants"): squads must not fire at targets they cannot
        /// damage. A hit that cannot beat the armor adds no wound, so its fill is 0 and so is
        /// its take-out.</para>
        /// </summary>
        internal static float CalculateRemovalFractionOnHit(
            BattleSoldier target,
            float damageCoefficient,
            float wornArmor,
            float armorMultiplier,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return 0f;
            }
            (float takeOut, float progress) = AccumulateTakeOutTerms(
                target, wornArmor, armorMultiplier, weaponWoundMultiplier, damageCoefficient, null);
            return CombineRemovalFraction(takeOut, progress);
        }

        /// <summary>
        /// Both halves of the graded fraction from ONE hit-location walk, for callers that need the
        /// raw take-out probability (shot count) and the wound progress (battle value) from the same
        /// hit. Equivalent to calling <see cref="CalculateTakeOutProbabilityOnHit"/> and taking the
        /// progress term separately, but without walking the locations twice.
        /// </summary>
        internal static (float TakeOut, float WoundProgress) CalculateRemovalTermsOnHit(
            BattleSoldier target,
            float damageCoefficient,
            float wornArmor,
            float armorMultiplier,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return (0f, 0f);
            }
            return AccumulateTakeOutTerms(
                target, wornArmor, armorMultiplier, weaponWoundMultiplier, damageCoefficient, null);
        }

        /// <summary>
        /// The per-hit removal fraction from its two halves: the take-out chance and the graded
        /// credit beyond it (<c>woundProgress</c>, which the location walk already computes as
        /// combined fill minus take-out, so the sum is the combined fill).
        /// </summary>
        internal static float CombineRemovalFraction(float takeOut, float woundProgress) =>
            Math.Clamp(takeOut + Math.Max(0f, woundProgress), 0f, 1f);

        /// <summary>
        /// Expected fraction of a target's battle value removed by ONE burst, over the joint
        /// distribution of the to-hit roll and the resulting hit count.
        ///
        /// <para>WHY. Scoring used to be <c>P(hit) * removalFractionPerHit</c> -- the probability
        /// the ROLL succeeds times what a SINGLE hit is worth. But <see cref="Actions.ShootAction"/>
        /// resolves a recoil loop: the first hit needs margin &gt; 0, and each further hit needs the
        /// margin, less one Recoil per shot already fired, to stay above 1. A nine-round bolt burst
        /// against a large target at close range lands three to five hits and was priced as one, so
        /// every comparison against an option scored over MULTIPLE bodies -- a grenade, a flamer
        /// cone -- was rigged against the rifle.</para>
        ///
        /// <para>THE FORM. Hit k requires margin &gt; <c>t_k</c>, with <c>t_1 = 0</c> and
        /// <c>t_k = 1 + (k-1)*recoil</c>; margin is normal with mean
        /// <c>preRollHitTotal - HitRollMean</c> and deviation <c>HitRollStdDev</c>, so
        /// <c>q_k = P(H &gt;= k)</c> is one CDF each. Since
        /// <c>1 - (1-f)^H = f * sum_{j&lt;H} (1-f)^j</c>, taking expectations gives
        /// <c>E[removed] = f * sum_k q_k * (1-f)^(k-1)</c> -- compounding, so hits saturate at the
        /// target's full battle value instead of summing past it, and NOT
        /// <c>1 - (1-f)^E[H]</c>, which would credit a coin-flip shot from a certain-kill weapon
        /// with a certain kill. At one shot this is exactly the old <c>q_1 * f</c>, so a
        /// single-shot weapon's score is unchanged to the bit.</para>
        ///
        /// <para>Shared by all three consumers of the removal currency -- immediate action scoring
        /// in <see cref="BattleSquadPlanner.EvaluateRangedTarget"/>, the Phase 4 lookahead table in
        /// <see cref="PairRemovalTerm.RemovalAt"/>, and the Phase 6 engagement-range model in
        /// <see cref="RangedEffectivenessCurve"/> -- because those three must stay commensurable.
        /// </para>
        /// </summary>
        internal static float ExpectedBurstRemovalFraction(
            float preRollHitTotal,
            int shotsToFire,
            float recoil,
            float removalFractionPerHit)
        {
            float perHit = Math.Clamp(removalFractionPerHit, 0f, 1f);
            if (perHit <= 0f)
            {
                return 0f;
            }
            int shots = Math.Max(1, shotsToFire);
            float mean = preRollHitTotal - HitRollMean;
            float survive = 1f - perHit;
            float expected = 0f;
            float weight = 1f;
            for (int k = 1; k <= shots; k++)
            {
                float threshold = k == 1 ? 0f : 1f + ((k - 1) * recoil);
                float z = (mean - threshold) / HitRollStdDev;
                // q_k is non-increasing in k (threshold rises with k and recoil is non-negative),
                // and so is the survival weight, so once a term stops mattering no later one can.
                //
                // THE FIRST SHOT IS EXEMPT, and that is the whole point of the k > 1 guard. A
                // hopeless long-range shot's rate is ~1e-7 rather than 0, and the engagement-range
                // model reads exactly that tail to tell "barely worth shooting" from "cannot shoot
                // at all" -- RangedEffectivenessCurve's saturation band is built on the difference.
                // So q_1 is still evaluated in full and still tested against a hard zero.
                //
                // No such argument covers the NINTH bolt round of a burst at z < -6. That term is
                // under 1e-9 before the survival weight scales it down further, it is a term of a
                // sum whose other terms are O(0.1), and nothing downstream can resolve it. Testing
                // z before the call skips the CDF outright instead of computing it to discard it.
                if (k > 1 && z < DeepTailZ)
                {
                    break;
                }
                // The other end of the same argument. A cone bearer's reference total is
                // PairRemovalRateTable.ConeCertainHitTotal (1000), and a point-blank shot is not
                // far off, so this loop was spending one CDF per shot to be told the shot lands --
                // nine of them for a bolt burst. Above CertainZ the CDF returns exactly 1f, so
                // substituting the constant is a bitwise-identical shortcut rather than a
                // simplification of the model.
                float reachesK = z > CertainZ
                    ? 1f
                    : GaussianCalculator.ApproximateNormalCDF(z);
                if (reachesK <= 0f || weight <= 0f)
                {
                    break;
                }
                expected += reachesK * weight;
                weight *= survive;
            }
            return Math.Clamp(perHit * expected, 0f, 1f);
        }

        /// <summary>
        /// The range-INDEPENDENT half of <see cref="CalculateTakeOutProbabilityOnHit"/>: one
        /// <c>(w_loc, K_loc)</c> pair per hit location that can take the target out, where
        /// <c>K_loc = (wornArmor + naturalArmor_loc) * armorMultiplier + requiredPenetratingDamage</c>. Combined with
        /// <see cref="EvaluateTakeOutProbability"/> this reproduces the full function at any damage
        /// coefficient without walking hit locations or wound state again -- the Phase 4 lookahead
        /// path (Design/Reference/BattleLogic.md). Both entry points run the SAME loop
        /// (<see cref="AccumulateTakeOutTerms"/>), so the two paths cannot drift.
        /// </summary>
        internal static IReadOnlyList<TakeOutLocationTerm> BuildTakeOutLocationTerms(
            BattleSoldier target,
            float wornArmor,
            float armorMultiplier,
            float weaponWoundMultiplier)
        {
            List<TakeOutLocationTerm> terms = [];
            AccumulateTakeOutTerms(
                target, wornArmor, armorMultiplier, weaponWoundMultiplier, null, terms);
            return terms;
        }

        /// <summary>
        /// takeOut(r) = sum over locations of w_loc * Phi((DamageRollMean - K_loc/damageCoefficient(r))
        /// / DamageRollStdDev). A fixed-size sum of normal CDFs; no allocation, no traversal.
        /// </summary>
        internal static float EvaluateTakeOutProbability(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient)
        {
            if (terms == null || damageCoefficient <= 0)
            {
                return 0f;
            }
            float probability = 0f;
            for (int index = 0; index < terms.Count; index++)
            {
                if (SkipLocation(terms[index], damageCoefficient))
                {
                    continue;
                }
                probability += terms[index].Weight
                    * EvaluateTakeOutLocationTail(terms[index], damageCoefficient);
            }
            return Math.Clamp(probability, 0f, 1f);
        }

        private static float EvaluateTakeOutLocationTail(
            TakeOutLocationTerm term,
            float damageCoefficient)
        {
            float requiredRoll = term.PenetrationThreshold / damageCoefficient;
            return GaussianCalculator.ApproximateNormalCDF(
                (DamageRollMean - requiredRoll) / DamageRollStdDev);
        }

        /// <summary>
        /// Both halves of the per-hit removal from ONE pass over the location vector, combined:
        /// the take-out chance and the location fills (see the block at the top of this class).
        /// The engagement scorer calls this for every (option, enemy, separation) it projects.
        /// </summary>
        internal static float EvaluateRemovalFraction(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient)
        {
            (float takeOut, float progress) = EvaluateRemovalTerms(
                terms, damageCoefficient, LocationFillCombineExponent);
            return CombineRemovalFraction(takeOut, progress);
        }

        /// <summary>
        /// The graded half alone: combined fill per hit beyond the take-out chance.
        /// </summary>
        internal static float EvaluateWoundProgress(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient) =>
            EvaluateRemovalTerms(terms, damageCoefficient, LocationFillCombineExponent).Progress;

        /// <summary>
        /// Take-out and graded credit for a location vector at one damage coefficient, with the
        /// location-combining exponent as a parameter so the calibration can compare settings.
        /// </summary>
        internal static (float TakeOut, float Progress) EvaluateRemovalTerms(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient,
            float combineExponent)
        {
            if (terms == null || damageCoefficient <= 0)
            {
                return (0f, 0f);
            }
            float takeOut = 0f;
            float fillNorm = 0f;
            float fillMax = 0f;
            bool maximum = float.IsPositiveInfinity(combineExponent);
            for (int index = 0; index < terms.Count; index++)
            {
                TakeOutLocationTerm term = terms[index];
                if (SkipLocation(term, damageCoefficient))
                {
                    continue;
                }
                float locationTakeOut = EvaluateTakeOutLocationTail(term, damageCoefficient);
                takeOut += term.Weight * locationTakeOut;
                float rate = term.Weight * GradualFill(term, damageCoefficient, locationTakeOut);
                if (maximum)
                {
                    fillMax = Math.Max(fillMax, rate);
                }
                else
                {
                    fillNorm += MathF.Pow(rate, combineExponent);
                }
            }
            takeOut = Math.Clamp(takeOut, 0f, 1f);
            float gradual = maximum
                ? fillMax
                : fillNorm > 0f ? MathF.Pow(fillNorm, 1f / combineExponent) : 0f;
            return (takeOut, Math.Clamp(gradual, 0f, 1f - takeOut));
        }

        // The part of a location's fill per hit that is NOT a one-hit disable: the capped
        // expectation counts a disabling hit as the whole gap, i.e. as its take-out chance, so the
        // rest is softening. One-hit chances add across locations (they are separate chances on
        // the same hit); softening competes, because the first location to fill decides -- which
        // is why only this part goes through the location-combining norm.
        private static float GradualFill(
            TakeOutLocationTerm term, float damageCoefficient, float locationTakeOut) =>
            Math.Max(0f, EvaluateLocationFill(term, damageCoefficient) - locationTakeOut);

        // Wound ratio at which levels 1..7 (Minor .. Unsurvivable) begin, and each level's worth in
        // Negligible wounds: six of a level make one of the next (Body.WOUND_MAX = 5).
        private static readonly float[] LevelRatios = [0.125f, 0.25f, 0.5f, 1f, 2f, 4f, 8f];
        private static readonly float[] LevelValues = [1f, 6f, 36f, 216f, 1296f, 7776f, 46656f, 279936f];

        /// <summary>
        /// The value of a normalized wound counter in Negligible units: its nibbles read as base-6
        /// digits. Every severity comparison in the engine reads the normalized counter as a plain
        /// magnitude, which orders exactly as this value does.
        /// </summary>
        internal static float WoundCounterValue(uint wounds)
        {
            float value = 0f;
            for (int nibble = 0; nibble < LevelValues.Length; nibble++)
            {
                value += ((wounds >> (nibble * 4)) & 0xfu) * LevelValues[nibble];
            }
            return value;
        }

        /// <summary>
        /// Expected share of one location's remaining gap that one hit landing on it fills:
        /// <c>E[min(6^level, gap)] / gap</c> over the damage roll. A hit that beats the armor
        /// wounds at least Negligible; each higher level starts where the affine damage-to-ratio
        /// map reaches its ratio. Exact for the engine's counter, so a location that needs one
        /// Critical credits a Moderate hit with 1/36 of it, not a quarter.
        /// </summary>
        private static float EvaluateLocationFill(TakeOutLocationTerm term, float damageCoefficient)
        {
            float gap = term.Gap;
            if (gap <= 0f)
            {
                return 0f;
            }
            float atLeast = DamageTail(term.EffectiveArmor, damageCoefficient);
            if (atLeast <= 0f)
            {
                return 0f;
            }
            float expected = 0f;
            int level = 0;
            for (; level < LevelRatios.Length; level++)
            {
                float next = DamageTail(
                    term.ZeroProgressThreshold + (term.DamagePerRatio * LevelRatios[level]),
                    damageCoefficient);
                expected += (atLeast - next) * Math.Min(LevelValues[level], gap);
                atLeast = next;
                if (atLeast <= 0f)
                {
                    break;
                }
            }
            if (atLeast > 0f)
            {
                expected += atLeast * Math.Min(LevelValues[LevelValues.Length - 1], gap);
            }
            return Math.Clamp(expected / gap, 0f, 1f);
        }

        // P(damage roll * c >= threshold); 0 once the threshold is past anything the roll makes.
        private static float DamageTail(float threshold, float damageCoefficient)
        {
            float requiredRoll = threshold / damageCoefficient;
            if (requiredRoll >= DeepTailRoll)
            {
                return 0f;
            }
            return GaussianCalculator.ApproximateNormalCDF(
                (DamageRollMean - requiredRoll) / DamageRollStdDev);
        }

        /// <summary>
        /// True when neither term of the per-hit removal can be nonzero at this damage coefficient,
        /// so the whole location can be skipped without evaluating a single tail: the roll cannot
        /// beat the worn armor, so no hit wounds it at all. The take-out threshold is armor plus a
        /// penetration requirement, never below the armor, so it is out of reach too.
        ///
        /// <para>There is deliberately no mirror guard for a certain take-out. Thresholds are
        /// non-negative (they are armor plus a penetration requirement), so the take-out z is at
        /// most <c>DamageRollMean / DamageRollStdDev</c> = 2 and the tail is never saturated.</para>
        /// </summary>
        private static bool SkipLocation(TakeOutLocationTerm term, float damageCoefficient)
        {
            return term.EffectiveArmor >= DeepTailRoll * damageCoefficient;
        }

        /// <summary>
        /// The hit-location walk, evaluated at the UNIT weapon: no armor and
        /// <c>weaponWoundMultiplier = 1</c>. This is the whole expensive half of the removal
        /// estimate -- the body traversal, the motive/vital/last-hand filter, the wound-ladder
        /// search -- and NONE of it depends on the shooter, the weapon, or the range.
        ///
        /// <para>WHY THIS SPLIT EXISTS. Look at what the walk produces per location:
        /// <c>K_loc = A_loc + X/m</c> and <c>K_zero = A_loc</c>, where
        /// <c>A_loc = (wornArmor + naturalArmor_loc) * armorMultiplier</c>, <c>m</c> is the
        /// weapon's wound multiplier, and <c>X = constitution * ratio / woundMultiplier</c>. The
        /// weapon enters as one armor multiplier, one additive offset and one divisor, so a single
        /// vector captured with no armor at <c>m = 1</c> (carrying each location's natural armor
        /// unscaled) rescales to any weapon in a few multiply-adds -- see
        /// <see cref="RescaleToWeapon"/>.
        /// Before this split every (shooter, weapon, range, candidate option) combination re-walked
        /// the same body: an instrumented seed-1 sector generation ran the walk 9.0e6 times, for
        /// 7.0e8 <c>HitLocation.Template</c> reads and 3.7e8 wound-ladder carry steps, all of it
        /// recomputing identical numbers.</para>
        ///
        /// <para>The vector is memoized per soldier by
        /// <see cref="BattleSoldier.UnitTakeOutTerms"/>, keyed on body identity,
        /// <see cref="Body.InjuryRevision"/> and <see cref="BattleSoldier.Stance"/> -- the three
        /// things it actually varies with. That keeps this class stateless (the memo belongs to
        /// the soldier being described, not to the math) and keeps the estimate wound-state aware
        /// outside a frozen planning pass, where a per-turn planning memo would not be valid.</para>
        /// </summary>
        internal static TakeOutLocationTerm[] BuildUnitTakeOutTerms(BattleSoldier target)
        {
            if (target == null || !target.IsCombatEffective)
            {
                return [];
            }

            Body body = target.Soldier.Body;
            int totalLocationWeight = body.TotalProbabilityMap[(int)target.Stance];
            if (totalLocationWeight <= 0)
            {
                return [];
            }

            IReadOnlyList<int> functioningHands = target.FunctioningHandGroupIds;
            int? lastFunctioningHand = functioningHands.Count == 1
                ? functioningHands[0]
                : null;
            float perceivedConstitution = PerceivedConstitution(target);
            List<TakeOutLocationTerm> terms = [];
            foreach (HitLocation location in body.HitLocations)
            {
                // One local instead of the nine repeated `location.Template.` reads this loop used
                // to make. The property is a plain field read, but at 7.0e8 calls a seed it was the
                // single largest line in an instrumented profile.
                HitLocationTemplate template = location.Template;
                int locationWeight = template.HitProbabilityMap[(int)target.Stance];
                if (locationWeight <= 0 || location.IsSevered)
                {
                    continue;
                }

                // The wound total at which this location takes the soldier out of the fight, as
                // Soldier.IsCombatEffective decides it (2026-10-06; until then every motive
                // location counted at cripple and an arm only when it was the last hand):
                //  - vital: crippled or severed (CanFight);
                //  - the last functioning hand group: crippled or severed (CanFight);
                //  - any other hand-group location: severed (an untreated severed limb);
                //  - a principal motive location: crippled, severed, or the Massive band that
                //    stops it moving (MotiveImpairment);
                //  - a motive extremity (a foot): severed only -- it slows, never stops.
                uint severOrCripple = Math.Min(template.CrippleWound, template.SeverWound);
                uint disableAt;
                if (template.IsVital)
                {
                    disableAt = severOrCripple;
                }
                else if (template.HandGroupId.HasValue)
                {
                    disableAt = template.HandGroupId == lastFunctioningHand
                        ? severOrCripple
                        : template.SeverWound;
                }
                else if (template.IsMotive)
                {
                    disableAt = MotiveImpairment.IsPrincipal(body, template)
                        ? Math.Min(severOrCripple, (uint)WoundLevel.Massive)
                        : template.SeverWound;
                }
                else
                {
                    continue;
                }
                float requiredRatio = FindMinimumDisablingWoundRatio(
                    location.Wounds.WoundTotal,
                    disableAt);
                if (float.IsPositiveInfinity(requiredRatio))
                {
                    continue;
                }

                // What a shooter can judge, not the part's real values (OnlyWar_TDD.md §5.3,
                // settled 2026-10-12): a bionic limb inside power armor is not
                // visible, so every location -- cybernetic or not -- is estimated with the species
                // median constitution and its template's natural armor. Only WoundResolver and the
                // attack actions read a cybernetic part's own constitution and armor.
                float constitution = perceivedConstitution;
                float naturalArmor = location.PerceivedNaturalArmor;

                // Execution first requires penetration of the worn plus natural armor, both scaled
                // by the weapon's armor multiplier; the resolver then applies the hit-location
                // multiplier before classifying the wound. A carried Negligible wound only
                // requires positive penetration.
                //
                // X, the unit-weapon numerator. The division by the weapon's wound multiplier and
                // the addition of the scaled armor are what RescaleToWeapon puts back, in that
                // order.
                float unitPenetratingDamage = requiredRatio <= 0f
                    ? 0f
                    : (constitution * requiredRatio)
                        / Math.Max(0.0001f, template.WoundMultiplier);
                // K_loc at the unit weapon with no armor. Range-independent: the numerator of
                // requiredRoll carries no range term, which is what lets the Phase 4 table rescale
                // take-out in closed form. K_zero (Phase 5): the same expression evaluated at
                // ratio -> 0+, i.e. the damage at which this location first takes a wound at all --
                // nothing beyond the armor. The gap between the two is the graded band the
                // woundProgress term integrates over.
                terms.Add(new TakeOutLocationTerm(
                    locationWeight / (float)totalLocationWeight,
                    unitPenetratingDamage,
                    requiredRatio,
                    0f,
                    0f,
                    constitution / Math.Max(0.0001f, template.WoundMultiplier),
                    WoundCounterValue(disableAt) - WoundCounterValue(location.Wounds.WoundTotal),
                    naturalArmor));
            }
            return terms.ToArray();
        }

        /// <summary>
        /// A target's toughness as a shooter can judge it: the median Constitution of its species,
        /// not the soldier's own (2026-10-12, product owner). What can be seen across a battlefield
        /// is the species, the size (the hit chance reads each soldier's real size: a bigger target
        /// may fairly draw more fire), the armour and the weapons -- an ork nob looks different from
        /// a boy -- and the wounds a soldier has visibly taken, which this estimate still reads. It
        /// cannot tell which marine of ten has the weakest constitution. Before, target selection
        /// chose exactly that marine, and the engagement forecast then priced the whole fight at
        /// the softest target's rate: autoguns against Tactical Marines were credited about 357 hits
        /// a kill where the engine averages 629 (Design/Reference/BattleLogic.md §5.3).
        /// The engine still resolves every wound with the soldier's real Constitution.
        /// </summary>
        internal static float PerceivedConstitution(BattleSoldier target) =>
            target.Soldier.Template?.Species?.Constitution?.BaseValue
                ?? target.Soldier.Constitution;

        /// <summary>
        /// One constitution for the whole soldier, for the scalar estimates that have no hit
        /// location to ask: the squad and force target profiles behind
        /// <see cref="RangedEffectivenessCurve"/> (<see cref="BattleSquad"/> statistics,
        /// <see cref="BattleEngagementFrameBuilder"/>'s representative profile, and the withdrawal
        /// reach forecast). Those profiles also carry worn armor only, with no natural armor.
        /// </summary>
        /// <remarks>
        /// Decided 2026-10-12 (OnlyWar_TDD.md §5.3): the whole-soldier value ignores
        /// cybernetic parts. It is the soldier's organic constitution, and the scalar squad and
        /// force profiles take no cybernetic armor. Only the per-location paths (resolution, and
        /// the removal walk's perceived values) see individual locations.
        /// </remarks>
        internal static float RepresentativeConstitution(BattleSoldier soldier) =>
            soldier.Soldier.Constitution;

        /// <summary>
        /// The armor a hit on one location must beat: worn armor plus the location's natural armor
        /// (a cybernetic part's own armor replaces the template's -- see
        /// <see cref="HitLocation.NaturalArmor"/>), both scaled by the weapon's armor multiplier.
        /// Every attack action and the removal estimate's rescaling call this one expression.
        /// </summary>
        internal static float EffectiveArmor(
            float wornArmor, float naturalArmor, float armorMultiplier) =>
            (wornArmor + naturalArmor) * armorMultiplier;

        /// <summary>
        /// One unit-weapon term put back into a specific weapon's currency:
        /// <c>K = effectiveArmor + unit / weaponWoundMultiplier</c> for both thresholds, where
        /// <c>effectiveArmor = (wornArmor + naturalArmor) * armorMultiplier</c> -- the same
        /// expression, in the same association, as the attack actions use at execution.
        /// </summary>
        private static TakeOutLocationTerm RescaleToWeapon(
            in TakeOutLocationTerm unitTerm,
            float wornArmor,
            float armorMultiplier,
            float weaponWoundMultiplier)
        {
            float effectiveArmor = EffectiveArmor(wornArmor, unitTerm.NaturalArmor, armorMultiplier);
            return new TakeOutLocationTerm(
                unitTerm.Weight,
                effectiveArmor + (unitTerm.PenetrationThreshold / weaponWoundMultiplier),
                unitTerm.RequiredRatio,
                effectiveArmor + (unitTerm.ZeroProgressThreshold / weaponWoundMultiplier),
                effectiveArmor,
                unitTerm.DamagePerRatio / weaponWoundMultiplier,
                unitTerm.Gap,
                unitTerm.NaturalArmor);
        }

        /// <summary>
        /// The single evaluation behind both <see cref="CalculateTakeOutProbabilityOnHit"/> and
        /// <see cref="BuildTakeOutLocationTerms"/>. Pass a damage coefficient to get the
        /// probability, a collector to capture the range-independent terms, or both. When
        /// <paramref name="collector"/> is null nothing is allocated, so the hot scoring path is
        /// unchanged.
        /// </summary>
        private static (float TakeOut, float WoundProgress) AccumulateTakeOutTerms(
            BattleSoldier target,
            float wornArmor,
            float armorMultiplier,
            float weaponWoundMultiplier,
            float? damageCoefficient,
            List<TakeOutLocationTerm> collector)
        {
            if (target == null || weaponWoundMultiplier <= 0)
            {
                return (0f, 0f);
            }

            // Empty for a target that is not combat effective or carries no hit lottery at this
            // stance -- the two early returns the walk used to make.
            IReadOnlyList<TakeOutLocationTerm> unitTerms = target.UnitTakeOutTerms;
            float probability = 0f;
            float fillNorm = 0f;
            float fillMax = 0f;
            bool maximum = float.IsPositiveInfinity(LocationFillCombineExponent);
            for (int index = 0; index < unitTerms.Count; index++)
            {
                TakeOutLocationTerm term = RescaleToWeapon(
                    unitTerms[index], wornArmor, armorMultiplier, weaponWoundMultiplier);
                collector?.Add(term);
                if (damageCoefficient.HasValue
                    && !SkipLocation(term, damageCoefficient.Value))
                {
                    float locationTakeOut =
                        EvaluateTakeOutLocationTail(term, damageCoefficient.Value);
                    probability += term.Weight * locationTakeOut;
                    float rate = term.Weight
                        * GradualFill(term, damageCoefficient.Value, locationTakeOut);
                    if (maximum)
                    {
                        fillMax = Math.Max(fillMax, rate);
                    }
                    else
                    {
                        fillNorm += MathF.Pow(rate, LocationFillCombineExponent);
                    }
                }
            }
            probability = Math.Clamp(probability, 0f, 1f);
            float gradual = maximum
                ? fillMax
                : fillNorm > 0f ? MathF.Pow(fillNorm, 1f / LocationFillCombineExponent) : 0f;
            return (probability, Math.Clamp(gradual, 0f, 1f - probability));
        }

        /// <summary>
        /// The wound ladder, hoisted to a static. As a span literal local this was rebuilt on every
        /// call: Roslyn can only point a <c>ReadOnlySpan&lt;T&gt;</c> literal at a blittable
        /// read-only data blob for primitives whose byte layout it knows at compile time, and a
        /// <c>ValueTuple&lt;WoundLevel, float&gt;</c> does not qualify, so it emitted a stackalloc
        /// plus eight tuple constructions per call -- 4.0e8 of them per seed, one instrumented
        /// profile's entire <c>ValueTuple`2..ctor</c> line. That is a Roslyn lowering decision baked
        /// into the IL, so a Release build does not fix it.
        /// </summary>
        /// <remarks>
        /// <c>Nibble</c> is the band's index in the packed <see cref="Wounds.WoundTotal"/> layout --
        /// each level is 16x the one below, so <c>Negligible</c> occupies nibble 0, <c>Minor</c>
        /// nibble 1, and so on. Carried here rather than derived per call because it is exactly
        /// where <see cref="AddWoundForEstimate"/>'s carry has to start.
        /// </remarks>
        private static readonly (WoundLevel Level, float Ratio, int Nibble)[] WoundRatioLadder =
        [
            (WoundLevel.Negligible, 0f, 0),
            (WoundLevel.Minor, 0.125f, 1),
            (WoundLevel.Moderate, 0.25f, 2),
            (WoundLevel.Major, 0.5f, 3),
            (WoundLevel.Critical, 1f, 4),
            (WoundLevel.Massive, 2f, 5),
            (WoundLevel.Mortal, 4f, 6),
            (WoundLevel.Unsurvivable, 8f, 7)
        ];

        /// <summary>
        /// The smallest wound ratio that would push <paramref name="currentWounds"/> to or past
        /// <paramref name="disableThreshold"/>, or +infinity when no single wound can. Internal
        /// rather than private for <c>WoundLadderTests</c>, which pins
        /// <see cref="AddWoundForEstimate"/>'s bounded carry against a full-scan reference.
        /// </summary>
        internal static float FindMinimumDisablingWoundRatio(
            uint currentWounds,
            uint disableThreshold)
        {
            foreach ((WoundLevel level, float ratio, int nibble) in WoundRatioLadder)
            {
                if (AddWoundForEstimate(currentWounds, level, nibble) >= disableThreshold)
                {
                    return ratio;
                }
            }
            return float.PositiveInfinity;
        }

        // Pure mirror of Wounds.AddWound's six-per-level carry. Keeping this local lets scoring
        // inspect hypothetical hits without mutating the frozen battle state.
        //
        // PRECONDITION: currentWounds is normalized -- every nibble at or below Wounds.WOUND_MAX.
        // Wounds.Normalize establishes that on every mutation path, and this method only ever reads
        // HitLocation.Wounds.WoundTotal.
        //
        // Under that precondition the scan is bounded at both ends rather than sweeping all seven
        // nibbles. It STARTS at woundNibble because adding one wound of one level touches that band
        // and nothing below it, so every lower nibble is still the normalized input. It STOPS at the
        // first band that does not overflow, because the only thing that can push a higher band over
        // is the carry this loop deposits, and a band that did not overflow deposited none.
        //
        // Wounds.Normalize itself scans from 0 and uses `continue` because it also has to repair
        // state it did not create -- a Wounds built from a raw uint, or a healing step-down onto an
        // occupied band. This mirror never sees that state, and already could not have repaired it:
        // it carries 1, not count / (WOUND_MAX + 1).
        private static uint AddWoundForEstimate(uint currentWounds, WoundLevel wound, int woundNibble)
        {
            uint total = currentWounds + (uint)wound;
            for (int nibble = woundNibble; nibble < 7; nibble++)
            {
                int shift = nibble * 4;
                if (((total >> shift) & 0xfu) <= Wounds.WOUND_MAX)
                {
                    break;
                }
                total &= ~(0xfu << shift);
                total += 1u << (shift + 4);
            }
            return total;
        }

        /// <summary>
        /// Standard normal density. Internal because the planner's blast-delivery quadrature is
        /// built from the same curve.
        /// </summary>
        internal static float NormalPdf(float z)
        {
            // See GaussianCalculator.InvSqrt2Pi for why the constant is written out: the divisor
            // was a live square root on all ~6.6e8 calls a seed makes. Single precision throughout
            // for the same reason it is safe in the CDF -- no cancellation here at all, and the
            // float return already rounded the double result at the boundary.
            return GaussianCalculator.InvSqrt2Pi * MathF.Exp(-0.5f * z * z);
        }
    }
}
