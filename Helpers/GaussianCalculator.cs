using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Helpers
{
    public static class GaussianCalculator
    {

        public static float DetermineMarginOfSuccessZvalue(float zValue, IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            double roll = random.NextRandomZValue();
            return (float)(zValue - roll);
        }

        /// <summary>
        /// 1/sqrt(2*pi), written out rather than as <c>Math.Sqrt(2 * Math.PI)</c>. Roslyn folds the
        /// multiply, but it does not evaluate library calls at compile time, and RyuJIT expands
        /// Math.Sqrt to a bare sqrtsd without noticing its operand is constant -- so the inline form
        /// cost a real square root on every call at every optimization level. Battle planning makes
        /// ~1.5e9 of these per seed.
        /// </summary>
        internal const float InvSqrt2Pi = 0.3989422804014327f;

        // =====================================================================================
        // TABULATED UPPER TAIL
        //
        // Battle planning evaluates this function ~6e8 times per seed, and roughly half of its
        // cost is the single MathF.Exp. A precomputed table of the upper tail with linear
        // interpolation replaces that with two array loads and a multiply-add.
        //
        // WHY INTERPOLATE RATHER THAN ROUND TO THE NEAREST ENTRY. Not accuracy -- smoothness.
        // RangedEffectivenessCurve.Argmax and SaturationRange do not merely evaluate these curves,
        // they SEARCH them: 33 coarse samples then 17 refining ones, resolving to about 2 yards at
        // a bolter's reach. Nearest-entry lookup makes the curve a staircase, so adjacent samples
        // come out exactly equal and the peak is decided by whichever index wins a strict `>`.
        // BattleSquadPlanner.HasNoViableRangedOption documents where that leads: options scoring
        // within 0.06 of each other and a squad walking backwards for thirty turns on a tie-break.
        // Interpolation costs one multiply-add and keeps the curve differentiable.
        //
        // SIZING. The binding constraint is L1 residency, not memory -- the planner is walking
        // soldier and grid data through the same 32KB. 4096 intervals over [0, 6] is 16KB, and
        // linear interpolation error is (h^2/8)*max|Phi''| = (1.46e-3)^2/8 * 0.242 = 6.5e-8, which
        // sits at the same order as A&S's own 7.5e-8 absolute bound. Doubling the table to halve
        // that error would cost a full L1 and buy nothing any consumer can resolve.
        //
        // The table stores only the tail, only for x >= 0; the sign branch below is what the rest
        // of the domain costs. If the wound-progress fusion later needs the DENSITY at the same
        // points, add it as a second interleaved column rather than a second array, so one cache
        // line carries both.
        // =====================================================================================

        private const float TableMaxZ = 6f;
        private const int TableIntervals = 4096;
        private const float TableScale = TableIntervals / TableMaxZ;

        /// <summary>
        /// Q(x) = 1 - Phi(x) sampled at <c>x = index / TableScale</c>.
        ///
        /// <para>Length is <c>TableIntervals + 2</c>, one node past the end of the domain. The
        /// guard below admits x strictly less than <see cref="TableMaxZ"/>, but <c>x * TableScale</c>
        /// is a float multiply that can round UP to exactly <see cref="TableIntervals"/> for an x a
        /// hair under the limit, and the interpolation then reads index + 1. The sentinel node makes
        /// that read in-bounds instead of a rare, input-dependent crash.</para>
        /// </summary>
        private static readonly float[] UpperTailTable = BuildUpperTailTable();

        private static float[] BuildUpperTailTable()
        {
            float[] table = new float[TableIntervals + 2];
            for (int index = 0; index < table.Length; index++)
            {
                table[index] = (float)UpperTailExact(
                    index * (double)TableMaxZ / TableIntervals);
            }
            return table;
        }

        /// <summary>
        /// The Abramowitz and Stegun 26.2.17 upper tail in double precision: the generator for
        /// <see cref="UpperTailTable"/> and the fallback past the end of it.
        ///
        /// <para>The table is deliberately generated from the SAME approximation the function used
        /// before it existed, not from a more accurate erfc. That keeps this change a pure cache of
        /// existing behaviour -- the difference between table and formula is bounded by
        /// interpolation error alone, which is a property a test can assert directly by sweeping
        /// the domain and comparing the two. (.NET has no built-in erf/erfc, so a more accurate
        /// generator would mean hand-rolling Cody's rational approximations, which is a much larger
        /// thing to get right and to review for a gain no consumer can resolve.)</para>
        ///
        /// <para>Relative accuracy in the tail is better than the 7.5e-8 ABSOLUTE bound suggests,
        /// and that matters here: at x = 6 this returns 9.902e-10 against a true 9.8659e-10, so
        /// 0.37% relative, degrading to a few percent by x = 12. Because the form is
        /// phi(x) * poly(t) and the polynomial approximates the Mills ratio relatively, the tail
        /// stays meaningful -- which is what ExpectedBurstRemovalFraction's ~1e-7 "barely worth
        /// shooting" rate depends on.</para>
        /// </summary>
        private static double UpperTailExact(double x)
        {
            const double a1 = 0.319381530;
            const double a2 = -0.356563782;
            const double a3 = 1.781477937;
            const double a4 = -1.821255978;
            const double a5 = 1.330274429;
            const double k = 0.2316419;

            double t = 1.0 / (1.0 + (k * x));
            double poly = t * (a1 + (t * (a2 + (t * (a3 + (t * (a4 + (t * a5))))))));
            return InvSqrt2Pi * Math.Exp(-0.5 * x * x) * poly;
        }

        public static float ApproximateNormalCDF(float zScore)
        {
            // Math.Abs is a call; the ternary is a sign-bit clear the JIT emits inline.
            float x = zScore < 0f ? -zScore : zScore;

            // The UPPER tail Q(x) = 1 - Phi(x), which is what A&S approximates and what the table
            // stores. Forming it directly and branching on the sign -- rather than building Phi and
            // taking 1 - Phi for negative z -- is what makes the single-precision path safe. Float
            // carries ~6e-8 of headroom under 1.0, so `1 - Q` collapses to exactly 1f once |z|
            // passes about 5.3, and recovering Q from it would hand back a hard zero.
            // ExpectedBurstRemovalFraction breaks its recoil loop on `reachesK <= 0f` and its
            // comment is explicit that a ~1e-7 rate must stay distinguishable from "cannot shoot at
            // all", so that zero would be a behaviour change rather than a rounding difference.
            // Neither branch below cancels.
            float upperTail;
            if (x < TableMaxZ)
            {
                float scaled = x * TableScale;
                int index = (int)scaled;
                float frac = scaled - index;
                float lower = UpperTailTable[index];
                upperTail = lower + (frac * (UpperTailTable[index + 1] - lower));
            }
            else
            {
                // Past the table: Q is under 1e-9 and falling. Kept on the exact formula rather
                // than clamped, because this is precisely the deep tail the burst model reads.
                upperTail = (float)UpperTailExact(x);
            }

            return zScore < 0f ? upperTail : 1f - upperTail;
        }

        /// <summary>
        /// Approximates the inverse cumulative distribution function (quantile function)
        /// of the standard normal distribution using a polynomial approximation.
        /// </summary>
        /// <param name="probability">The cumulative probability (between 0 and 1).</param>
        /// <returns>The approximate Z-score corresponding to the given probability.</returns>
        public static float ApproximateInverseNormalCDF(float probability)
        {
            if (probability <= 0 || probability >= 1)
            {
                throw new ArgumentOutOfRangeException("probability", "Probability must be between 0 and 1.");
            }

            // Constants for the approximation
            double c0 = 2.515517;
            double c1 = 0.802853;
            double c2 = 0.010328;
            double d1 = 1.432788;
            double d2 = 0.189269;
            double d3 = 0.001308;

            double p = probability;

            if (probability > 0.5)
            {
                p = 1 - probability;
            }

            double t = Math.Sqrt(Math.Log(1 / (p * p)));

            double z = t - (c0 + c1 * t + c2 * t * t) / (1 + d1 * t + d2 * t * t + d3 * t * t * t);

            if (probability < 0.5)
            {
                z = -z;
            }

            return (float)z;
        }
    }
}
