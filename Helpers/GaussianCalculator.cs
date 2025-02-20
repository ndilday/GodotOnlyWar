using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Helpers
{
    public static class GaussianCalculator
    {

        public static float DetermineMarginOfSuccessZvalue(float zValue)
        {

            double roll = RNG.NextRandomZValue();
            return (float)(zValue - roll);
        }

        public static float ApproximateNormalCDF(float zScore)
        {
            // Abramowitz and Stegun approximation constants
            const double a1 = 0.319381530;
            const double a2 = -0.356563782;
            const double a3 = 1.781477937;
            const double a4 = -1.821255978;
            const double a5 = 1.330274429;
            const double k = 0.2316419;

            double x = Math.Abs(zScore);
            double t = 1.0 / (1.0 + k * x);

            double poly = t * (a1 + t * (a2 + t * (a3 + t * (a4 + t * a5))));
            double prob = 1.0 - 1.0 / Math.Sqrt(2 * Math.PI) * Math.Exp(-x * x / 2.0) * poly;

            if (zScore < 0)
                prob = 1.0 - prob;

            return (float)prob;
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

            if (probability > 0.5)
            {
                z = -z;
            }

            return (float)z;
        }
    }
}
