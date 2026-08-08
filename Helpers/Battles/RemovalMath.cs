using System;
using System.Collections.Generic;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
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
        // estimate does. See Design/Reference/EngagementScoringOverhaul.md.
        internal const float HitRollMean = 10.5f;
        internal const float HitRollStdDev = 3f;

        // ===================================================================================
        // TUNABLE -- lambda, the graded-damage credit weight (Phase 5,
        // Design/Reference/EngagementScoringOverhaul.md).
        //
        //   removal = BV * [ P(takeout) + lambda * E[woundProgress; no takeout] ]
        //
        // 0 reproduces pre-Phase-5 behaviour exactly (only the finishing blow scores). 1 credits
        // every hit with the full fraction of the disable threshold it closes. It cannot conjure
        // value against an impenetrable target at ANY setting -- see CalculateRemovalFractionOnHit.
        //
        // SWEEP, reference scenario (GradedRemovalCalibrationTests, which regenerates this table):
        // 30 bolter marines (Gun skill bonus ~1.4, Dex 15.4, BV 9/11) at 200 yards from 1 Hive
        // Tyrant (BV 84), 1 Lictor (BV 37) and 2 melee Carnifexes (BV 30), all melee-only. Reported
        // for the lead marine squad; margin = Hold score - CloseToContact score, so positive means
        // "stand and shoot". Measured with the Phase 5c/5d lookahead in place.
        //
        //   lambda | chosen      | outgoing | future | Hold - Close
        //   -------+-------------+----------+--------+--------------
        //     0.00 | StepForward |    0.009 |   1.84 |        2.334
        //     0.05 | Hold        |    0.170 |  3.742 |        2.471
        //     0.10 | Hold        |    0.330 |  5.643 |        2.608
        //     0.15 | Hold        |    0.491 |  7.544 |        2.746
        //     0.20 | Hold        |    0.652 |  9.445 |        2.883
        //     0.25 | Hold        |    0.812 | 11.346 |        3.020
        //     0.35 | Hold        |    1.133 | 15.148 |        3.295
        //     0.50 | Hold        |    1.615 | 20.851 |        3.706
        //     0.75 | Hold        |    1.935 | 33.626 |        4.304
        //     1.00 | Hold        |    2.577 | 44.102 |        4.813
        //
        // WHY 0.5, in order of weight:
        //  1. lambda = 0 COLLAPSES. With `future` now built from the same honest rate, a squad that
        //     cannot one-shot anything scores ~0 on both halves and the decision falls to
        //     ChooseEngagementOption's tie-break -- StepForward, above. Every positive lambda fixes
        //     the reported behaviour; the sweep is choosing a magnitude, not a direction.
        //  2. Rate of resolution. 30 marines remove 3 * outgoing BV/turn against 181 BV of
        //     Tyranids: ~75 turns at 0.25, ~38 at 0.5, ~23 at 1.0. The design doc's stated
        //     calibration target is "tens of turns, not 183".
        //  3. woundProgress SUMS across hit locations, but a disable requires the damage to
        //     concentrate in ONE of them, so the summed figure systematically over-states real
        //     progress toward a kill. That is an argument for a value clearly below 1, and it is
        //     the only one of the three that is about physics rather than about tuning.
        //  4. It leaves `future` (20.9) at nearly the magnitude the surrounding score terms were
        //     tuned against pre-Phase-5 (22.6), so this phase does not silently re-scale
        //     commitment, role and readiness costs along with it.
        // Provisional pending the user's manual Godot verification; the sweep above is recorded
        // here so revisiting it is a one-line change, not a re-derivation.
        // The SHIPPED value, and a const like every other tunable in this codebase
        // (MoraleConstants is all `public const`). Phase 5 shipped this as a settable static so the
        // sweep could re-run in one process, and said so plainly; Phase 7 removed that. There is no
        // writable surface on this constant at all.
        internal const float WoundProgressCreditWeight = 0.5f;

        // TEST SEAM (internal; the assembly grants InternalsVisibleTo("OnlyWar.Tests") in
        // Properties/AssemblyInfo.cs). The sweep genuinely needs lambda to vary in one process --
        // ten planner runs, otherwise ten rebuilds -- but nothing else does, and Phase 5's settable
        // property let any caller leave the whole battle engine mis-tuned. This is the narrowest
        // shape that keeps the capability: no setter, one scoped override that always restores, so
        // the value cannot be left changed even by a test that throws. Shipping code never calls it
        // (grep OverrideWoundProgressCreditWeight -- the only caller is
        // OnlyWar.Tests/Battles/GradedRemovalCalibrationTests.cs).
        private static float _woundProgressCreditWeight = WoundProgressCreditWeight;

        /// <summary>Lambda as the scoring stack actually reads it: the shipped constant unless a
        /// calibration sweep currently holds an override scope.</summary>
        internal static float EffectiveWoundProgressCreditWeight => _woundProgressCreditWeight;

        internal static IDisposable OverrideWoundProgressCreditWeight(float value) =>
            new WoundProgressCreditWeightScope(value);

        private sealed class WoundProgressCreditWeightScope : IDisposable
        {
            private readonly float _previous;

            internal WoundProgressCreditWeightScope(float value)
            {
                _previous = _woundProgressCreditWeight;
                _woundProgressCreditWeight = value;
            }

            public void Dispose() => _woundProgressCreditWeight = _previous;
        }
        // ===================================================================================

        /// <summary>
        /// Probability that one landed hit removes the target from the fight. This mirrors the
        /// resolver's location lottery, armor and normal damage roll, wound-level conversion,
        /// accumulated wound carry, motive/vital thresholds, and last-functioning-hand rule.
        /// </summary>
        internal static float CalculateTakeOutProbabilityOnHit(
            BattleSoldier target,
            float damageCoefficient,
            float effectiveArmor,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return 0f;
            }
            return AccumulateTakeOutTerms(
                target, effectiveArmor, weaponWoundMultiplier, damageCoefficient, null).TakeOut;
        }

        /// <summary>
        /// PHASE 5 (Design/Reference/EngagementScoringOverhaul.md). The fraction of a target's battle
        /// value one landed hit is credited with removing:
        /// <c>P(takeout) + lambda * E[woundProgress; no takeout]</c>.
        ///
        /// <para>WHY. <see cref="CalculateTakeOutProbabilityOnHit"/> was already wound-state aware
        /// -- <c>FindMinimumDisablingWoundRatio</c> reads the wounds a location already carries, so
        /// take-out rises as a target is softened. What was missing was credit for CREATING that
        /// state: the planner scored only the finishing blow, never the twenty hits that made it
        /// possible, so a squad that could not one-shot anything scored ~0 for shooting and the
        /// decision fell entirely to the lookahead. This is a credit-assignment fix, not a new
        /// accumulator.</para>
        ///
        /// <para>The two terms decompose <c>E[progress]</c> exactly:
        /// <c>E[progress] = P(takeout)*1 + E[progress; no takeout]</c>, where progress is the
        /// fraction of the remaining gap to the disable threshold that the hit closes. lambda
        /// therefore interpolates between "only kills count" (0) and "all expected progress counts"
        /// (1), and the result is bounded by 1 for lambda in [0, 1].
        ///
        /// NOTE: the second term is the PARTIAL expectation (integrated over the no-takeout mass),
        /// not the conditional one the design doc's notation suggests. The conditional form
        /// diverges as P(takeout) approaches 1 -- a target that is certain to die would be scored
        /// as worth MORE than its battle value -- and it does not telescope with the first term.
        /// </para>
        ///
        /// <para>INVARIANT (Design doc "Invariants"): squads must not fire at targets they cannot
        /// damage. When penetration is impossible both the take-out threshold and the
        /// wound-onset threshold sit far out in the damage roll's tail, so the Gaussian mass
        /// between them vanishes and BOTH terms go to ~0. lambda cannot buy value against an
        /// impenetrable target; it only grades the penetrable-but-not-lethal middle.</para>
        /// </summary>
        internal static float CalculateRemovalFractionOnHit(
            BattleSoldier target,
            float damageCoefficient,
            float effectiveArmor,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return 0f;
            }
            (float takeOut, float progress) = AccumulateTakeOutTerms(
                target, effectiveArmor, weaponWoundMultiplier, damageCoefficient, null);
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
            float effectiveArmor,
            float weaponWoundMultiplier)
        {
            if (damageCoefficient <= 0)
            {
                return (0f, 0f);
            }
            return AccumulateTakeOutTerms(
                target, effectiveArmor, weaponWoundMultiplier, damageCoefficient, null);
        }

        internal static float CombineRemovalFraction(float takeOut, float woundProgress)
        {
            float lambda = EffectiveWoundProgressCreditWeight;
            return lambda <= 0f
                ? Math.Clamp(takeOut, 0f, 1f)
                : Math.Clamp(takeOut + (lambda * woundProgress), 0f, 1f);
        }

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
                float reachesK = GaussianCalculator.ApproximateNormalCDF(
                    (mean - threshold) / HitRollStdDev);
                // q_k is non-increasing in k, so once it or the survival weight underflows no later
                // term can contribute. Deliberately tested against zero rather than a small epsilon:
                // a hopeless long-range shot's rate is ~1e-7, not 0, and the engagement-range model
                // reads exactly that tail to tell "barely worth shooting" from "cannot shoot at
                // all". The loop is bounded by RateOfFire regardless.
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
        /// <c>K_loc = effectiveArmor + requiredPenetratingDamage</c>. Combined with
        /// <see cref="EvaluateTakeOutProbability"/> this reproduces the full function at any damage
        /// coefficient without walking hit locations or wound state again -- the Phase 4 lookahead
        /// path (Design/Reference/EngagementScoringOverhaul.md). Both entry points run the SAME loop
        /// (<see cref="AccumulateTakeOutTerms"/>), so the two paths cannot drift.
        /// </summary>
        internal static IReadOnlyList<TakeOutLocationTerm> BuildTakeOutLocationTerms(
            BattleSoldier target,
            float effectiveArmor,
            float weaponWoundMultiplier)
        {
            List<TakeOutLocationTerm> terms = [];
            AccumulateTakeOutTerms(
                target, effectiveArmor, weaponWoundMultiplier, null, terms);
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
        /// PHASE 5. <c>E[woundProgress; no takeout]</c> from the same <c>(w_loc, K_loc)</c> vector
        /// take-out uses -- see <see cref="CalculateRemovalFractionOnHit"/> for the semantics.
        /// </summary>
        /// <summary>
        /// PHASE 5. Both halves of the graded fraction from ONE pass over the location vector. The
        /// lookahead calls this for every (policy, ply, enemy) triple, and running
        /// <see cref="EvaluateTakeOutProbability"/> and <see cref="EvaluateWoundProgress"/> back to
        /// back walked the vector twice and cost ~3x on the degrading-weapon path.
        /// </summary>
        internal static float EvaluateRemovalFraction(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient)
        {
            if (terms == null || damageCoefficient <= 0)
            {
                return 0f;
            }
            bool graded = EffectiveWoundProgressCreditWeight > 0f;
            float takeOut = 0f;
            float progress = 0f;
            for (int index = 0; index < terms.Count; index++)
            {
                TakeOutLocationTerm term = terms[index];
                takeOut += term.Weight * EvaluateTakeOutLocationTail(term, damageCoefficient);
                if (graded)
                {
                    progress += term.Weight
                        * EvaluateWoundProgressTail(term, damageCoefficient);
                }
            }
            return CombineRemovalFraction(
                Math.Clamp(takeOut, 0f, 1f), Math.Clamp(progress, 0f, 1f));
        }

        internal static float EvaluateWoundProgress(
            IReadOnlyList<TakeOutLocationTerm> terms,
            float damageCoefficient)
        {
            if (terms == null || damageCoefficient <= 0)
            {
                return 0f;
            }
            float progress = 0f;
            for (int index = 0; index < terms.Count; index++)
            {
                progress += terms[index].Weight
                    * EvaluateWoundProgressTail(terms[index], damageCoefficient);
            }
            return Math.Clamp(progress, 0f, 1f);
        }

        /// <summary>
        /// One location's partial expectation of fractional progress toward its disable threshold,
        /// integrated over the sub-take-out part of the damage roll.
        ///
        /// <para>The resolver's damage-to-wound-ratio map is affine in the damage roll, so the
        /// fraction of the remaining gap closed by a roll <c>R</c> is
        /// <c>(R - r0) / (r1 - r0)</c> between the wound-onset roll <c>r0 = K_zero / c</c> and the
        /// disabling roll <c>r1 = K_loc / c</c>. With <c>A</c> and <c>B</c> the standardized forms
        /// of those two rolls, the partial expectation has the closed form
        /// <c>[phi(A) - phi(B) - A*(Phi(B) - Phi(A))] / (B - A)</c> -- one exp and two CDFs, no
        /// quadrature, so the lookahead can afford it.</para>
        /// </summary>
        private static float EvaluateWoundProgressTail(
            TakeOutLocationTerm term,
            float damageCoefficient)
        {
            float disablingRoll = term.PenetrationThreshold / damageCoefficient;
            float onsetRoll = term.ZeroProgressThreshold / damageCoefficient;
            // A location already at (or past) its threshold on any wound at all has no partial
            // credit to give: every penetrating hit either disables it or does nothing.
            if (disablingRoll - onsetRoll <= 0.0001f)
            {
                return 0f;
            }
            float low = (onsetRoll - DamageRollMean) / DamageRollStdDev;
            float high = (disablingRoll - DamageRollMean) / DamageRollStdDev;
            float span = high - low;
            if (span <= 0.0001f)
            {
                return 0f;
            }
            float mass = GaussianCalculator.ApproximateNormalCDF(high)
                - GaussianCalculator.ApproximateNormalCDF(low);
            float partial = NormalPdf(low) - NormalPdf(high) - (low * mass);
            return Math.Clamp(partial / span, 0f, 1f);
        }

        /// <summary>
        /// The single hit-location walk behind both <see cref="CalculateTakeOutProbabilityOnHit"/>
        /// and <see cref="BuildTakeOutLocationTerms"/>. Pass a damage coefficient to get the
        /// probability, a collector to capture the range-independent terms, or both. When
        /// <paramref name="collector"/> is null nothing is allocated, so the hot scoring path is
        /// unchanged.
        /// </summary>
        private static (float TakeOut, float WoundProgress) AccumulateTakeOutTerms(
            BattleSoldier target,
            float effectiveArmor,
            float weaponWoundMultiplier,
            float? damageCoefficient,
            List<TakeOutLocationTerm> collector)
        {
            if (target == null || !target.IsCombatEffective || weaponWoundMultiplier <= 0)
            {
                return (0f, 0f);
            }

            Body body = target.Soldier.Body;
            int totalLocationWeight = body.TotalProbabilityMap[target.Stance];
            if (totalLocationWeight <= 0)
            {
                return (0f, 0f);
            }

            IReadOnlyList<int> functioningHands = target.FunctioningHandGroupIds;
            int? lastFunctioningHand = functioningHands.Count == 1
                ? functioningHands[0]
                : null;
            float probability = 0f;
            float woundProgress = 0f;
            foreach (HitLocation location in body.HitLocations)
            {
                int locationWeight = location.Template.HitProbabilityMap[(int)target.Stance];
                if (locationWeight <= 0 || location.IsSevered)
                {
                    continue;
                }

                bool canTakeOut =
                    location.Template.IsMotive
                    || location.Template.IsVital
                    || (lastFunctioningHand.HasValue
                        && location.Template.HandGroupId == lastFunctioningHand);
                if (!canTakeOut)
                {
                    continue;
                }

                float requiredRatio = FindMinimumDisablingWoundRatio(
                    location.Wounds.WoundTotal,
                    Math.Min(location.Template.CrippleWound, location.Template.SeverWound));
                if (float.IsPositiveInfinity(requiredRatio))
                {
                    continue;
                }

                // Execution first requires weapon penetration, then the resolver subtracts
                // natural armor and applies the hit-location multiplier before classifying the
                // wound. A carried Negligible wound only requires positive weapon penetration.
                float requiredPenetratingDamage = requiredRatio <= 0f
                    ? 0f
                    : ((target.Soldier.Constitution * requiredRatio)
                        / Math.Max(0.0001f, location.Template.WoundMultiplier)
                        + location.Template.NaturalArmor)
                        / weaponWoundMultiplier;
                // K_loc. Range-independent: the numerator of requiredRoll carries no range term,
                // which is what lets the Phase 4 table rescale take-out in closed form.
                // K_zero (Phase 5): the same expression evaluated at ratio -> 0+, i.e. the damage
                // at which this location first takes a wound at all. The gap between the two is
                // the graded band the woundProgress term integrates over.
                TakeOutLocationTerm term = new(
                    locationWeight / (float)totalLocationWeight,
                    effectiveArmor + requiredPenetratingDamage,
                    requiredRatio,
                    effectiveArmor
                        + (location.Template.NaturalArmor / weaponWoundMultiplier));
                collector?.Add(term);
                if (damageCoefficient.HasValue)
                {
                    probability += term.Weight
                        * EvaluateTakeOutLocationTail(term, damageCoefficient.Value);
                    // Only computed when lambda can actually use it; at lambda = 0 this walk is
                    // bitwise identical to the pre-Phase-5 one.
                    if (EffectiveWoundProgressCreditWeight > 0f)
                    {
                        woundProgress += term.Weight
                            * EvaluateWoundProgressTail(term, damageCoefficient.Value);
                    }
                }
            }
            return (
                Math.Clamp(probability, 0f, 1f),
                Math.Clamp(woundProgress, 0f, 1f));
        }

        private static float FindMinimumDisablingWoundRatio(
            uint currentWounds,
            uint disableThreshold)
        {
            ReadOnlySpan<(WoundLevel Level, float Ratio)> candidates =
            [
                (WoundLevel.Negligible, 0f),
                (WoundLevel.Minor, 0.125f),
                (WoundLevel.Moderate, 0.25f),
                (WoundLevel.Major, 0.5f),
                (WoundLevel.Critical, 1f),
                (WoundLevel.Massive, 2f),
                (WoundLevel.Mortal, 4f),
                (WoundLevel.Unsurvivable, 8f)
            ];
            foreach ((WoundLevel level, float ratio) in candidates)
            {
                if (AddWoundForEstimate(currentWounds, level) >= disableThreshold)
                {
                    return ratio;
                }
            }
            return float.PositiveInfinity;
        }

        // Pure mirror of Wounds.AddWound's six-per-level carry. Keeping this local lets scoring
        // inspect hypothetical hits without mutating the frozen battle state.
        private static uint AddWoundForEstimate(uint currentWounds, WoundLevel wound)
        {
            uint total = currentWounds + (uint)wound;
            for (int nibble = 0; nibble < 7; nibble++)
            {
                int shift = nibble * 4;
                if (((total >> shift) & 0xfu) <= Wounds.WOUND_MAX)
                {
                    continue;
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
            return (float)(Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI));
        }
    }
}
