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

        public static float ApproximateNormalCDF(float zScore)
        {
            // Abramowitz and Stegun approximation constants
            const float a1 = 0.319381530f;
            const float a2 = -0.356563782f;
            const float a3 = 1.781477937f;
            const float a4 = -1.821255978f;
            const float a5 = 1.330274429f;
            const float k = 0.2316419f;

            // Math.Abs is a call; the ternary is a sign-bit clear the JIT emits inline.
            float x = zScore < 0f ? -zScore : zScore;
            float t = 1f / (1f + (k * x));

            float poly = t * (a1 + (t * (a2 + (t * (a3 + (t * (a4 + (t * a5))))))));
            // The UPPER tail Q(x) = 1 - Phi(x), which is what A&S actually approximates. Forming it
            // directly and branching -- rather than building Phi and taking 1 - Phi for negative z
            // -- is what makes the single-precision body safe. The old double body could round trip
            // through `1 - (1 - Q)` and recover Q, because double carries ~2.2e-16 of headroom
            // under 1.0; float carries ~6e-8, so `1 - Q` collapses to exactly 1f once |z| passes
            // about 5.3 and the tail would come back a hard zero. ExpectedBurstRemovalFraction
            // breaks its recoil loop on `reachesK <= 0f` and its comment is explicit that a ~1e-7
            // rate must stay distinguishable from "cannot shoot at all", so that zero would have
            // been a behaviour change rather than a rounding difference. Neither branch here
            // cancels, and the negative tail is strictly more accurate than what it replaces.
            float upperTail = InvSqrt2Pi * MathF.Exp(-x * x / 2f) * poly;

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
