using System;
using System.Collections.Generic;

namespace OnlyWar.Diagnostics.Battles.Calibration;

/// <summary>
/// Small special functions the probit fit needs: the normal distribution and the chi-square tail,
/// both built on the regularized incomplete gamma function. Kept self-contained so the calibration
/// tools need no numerics package.
/// </summary>
internal static class ProbitMath
{
    private const double Epsilon = 1e-16;
    private const double Tiny = 1e-300;

    public static double NormalPdf(double x) => System.Math.Exp(-0.5 * x * x) / System.Math.Sqrt(2.0 * System.Math.PI);

    /// <summary>The standard normal CDF, accurate in the tails (it uses the complementary form).</summary>
    public static double NormalCdf(double x)
    {
        // erfc(t) = Q(1/2, t^2), and 1 - Phi(x) = erfc(x / sqrt 2) / 2 for x >= 0.
        double tail = 0.5 * GammaQ(0.5, 0.5 * x * x);
        return x >= 0 ? 1.0 - tail : tail;
    }

    /// <summary>P(X &gt; x) for a chi-square variable with <paramref name="degrees"/> degrees of freedom.</summary>
    public static double ChiSquareSurvival(double x, double degrees)
    {
        if (x <= 0)
        {
            return 1.0;
        }
        return GammaQ(degrees / 2.0, x / 2.0);
    }

    /// <summary>P(X &lt;= x) for a chi-square variable.</summary>
    public static double ChiSquareCdf(double x, double degrees) =>
        x <= 0 ? 0.0 : GammaP(degrees / 2.0, x / 2.0);

    /// <summary>Natural log of the gamma function (Lanczos approximation, g = 7).</summary>
    public static double LogGamma(double x)
    {
        double[] c =
        [
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7
        ];
        if (x < 0.5)
        {
            return System.Math.Log(System.Math.PI / System.Math.Abs(System.Math.Sin(System.Math.PI * x))) - LogGamma(1.0 - x);
        }
        x -= 1.0;
        double sum = c[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; i++)
        {
            sum += c[i] / (x + i);
        }
        return 0.5 * System.Math.Log(2.0 * System.Math.PI) + (x + 0.5) * System.Math.Log(t) - t + System.Math.Log(sum);
    }

    /// <summary>Regularized lower incomplete gamma function P(a, x).</summary>
    public static double GammaP(double a, double x)
    {
        if (x <= 0)
        {
            return 0.0;
        }
        return x < a + 1.0 ? GammaSeries(a, x) : 1.0 - GammaContinuedFraction(a, x);
    }

    /// <summary>Regularized upper incomplete gamma function Q(a, x) = 1 - P(a, x).</summary>
    public static double GammaQ(double a, double x)
    {
        if (x <= 0)
        {
            return 1.0;
        }
        return x < a + 1.0 ? 1.0 - GammaSeries(a, x) : GammaContinuedFraction(a, x);
    }

    private static double GammaSeries(double a, double x)
    {
        double term = 1.0 / a;
        double sum = term;
        double ap = a;
        for (int n = 0; n < 1000; n++)
        {
            ap += 1.0;
            term *= x / ap;
            sum += term;
            if (System.Math.Abs(term) < System.Math.Abs(sum) * Epsilon)
            {
                break;
            }
        }
        return sum * System.Math.Exp(-x + a * System.Math.Log(x) - LogGamma(a));
    }

    // Modified Lentz evaluation of the continued fraction for Q(a, x).
    private static double GammaContinuedFraction(double a, double x)
    {
        double b = x + 1.0 - a;
        double c = 1.0 / Tiny;
        double d = 1.0 / b;
        double h = d;
        for (int i = 1; i < 1000; i++)
        {
            double an = -i * (i - a);
            b += 2.0;
            d = an * d + b;
            if (System.Math.Abs(d) < Tiny) d = Tiny;
            c = b + an / c;
            if (System.Math.Abs(c) < Tiny) c = Tiny;
            d = 1.0 / d;
            double delta = d * c;
            h *= delta;
            if (System.Math.Abs(delta - 1.0) < Epsilon)
            {
                break;
            }
        }
        return System.Math.Exp(-x + a * System.Math.Log(x) - LogGamma(a)) * h;
    }
}

/// <summary>
/// One observation group of a probit fit: a design row, the mean response of the battles at that
/// design point (0 to 1, where an undecided battle counts one half) and how many battles it averages.
/// </summary>
internal sealed record FitPoint(double[] Row, double Response, double Weight);

/// <summary>The outcome of <see cref="ProbitFit.Fit"/>.</summary>
internal sealed class ProbitFitResult
{
    public double[] Coefficients { get; init; }

    /// <summary>The inverse Fisher information. Not scaled by any dispersion estimate.</summary>
    public double[,] Covariance { get; init; }

    public double[] StandardErrors { get; init; }

    /// <summary>
    /// Binomial quasi-deviance against the saturated model that gives every point its own mean. Only
    /// differences between two fits of the same points mean anything.
    /// </summary>
    public double Deviance { get; init; }

    /// <summary>Pearson chi-square over the points. Divide by (points - parameters) for a dispersion estimate.</summary>
    public double PearsonChiSquare { get; init; }

    public int Points { get; init; }
    public double TotalWeight { get; init; }
    public int Iterations { get; init; }
    public bool Converged { get; init; }

    /// <summary>
    /// True when the data are one-sided enough that the likelihood has no finite maximum, which shows
    /// as a standard error too large to be real. The coefficients are then only a lower bound.
    /// </summary>
    public bool Separated { get; init; }
}

/// <summary>
/// Probit regression for fractional responses by Fisher scoring (iteratively reweighted least
/// squares). The response of a battle is 1 when side A holds the field, 0 when B does and 0.5 when
/// neither does, so the likelihood is the binomial quasi-likelihood: y ln(mu) + (1 - y) ln(1 - mu).
/// The score equations are those of a binomial fit, and the standard errors come from the inverse
/// Fisher information (dispersion 1).
///
/// A tiny ridge keeps one-sided data from sending the solver to infinity; it moves a well-determined
/// coefficient by far less than its standard error.
/// </summary>
internal static class ProbitFit
{
    private const double Ridge = 1e-6;
    private const int MaximumIterations = 200;
    private const double ProbabilityFloor = 1e-12;
    private const double SeparationStandardError = 50.0;

    public static ProbitFitResult Fit(IReadOnlyList<FitPoint> points, int parameters)
    {
        double[] beta = new double[parameters];
        double objective = Objective(points, beta);
        bool converged = false;
        int iteration = 0;
        for (; iteration < MaximumIterations; iteration++)
        {
            double[,] info = new double[parameters, parameters];
            double[] rhs = new double[parameters];
            Accumulate(points, beta, info, rhs);
            for (int i = 0; i < parameters; i++)
            {
                info[i, i] += Ridge;
            }
            double[,] inverse = Invert(info);
            if (inverse == null)
            {
                break;
            }

            double[] target = new double[parameters];
            for (int i = 0; i < parameters; i++)
            {
                for (int j = 0; j < parameters; j++)
                {
                    target[i] += inverse[i, j] * rhs[j];
                }
            }

            // Halve the step while the penalized deviance gets worse.
            double[] next = target;
            double nextObjective = Objective(points, next);
            for (int halvings = 0; halvings < 40 && !(nextObjective <= objective + 1e-10); halvings++)
            {
                next = new double[parameters];
                for (int i = 0; i < parameters; i++)
                {
                    next[i] = 0.5 * (beta[i] + target[i]);
                }
                target = next;
                nextObjective = Objective(points, next);
            }

            double change = 0;
            for (int i = 0; i < parameters; i++)
            {
                change = System.Math.Max(change, System.Math.Abs(next[i] - beta[i]));
            }
            beta = next;
            objective = nextObjective;
            if (change < 1e-9)
            {
                converged = true;
                iteration++;
                break;
            }
        }

        double[,] finalInfo = new double[parameters, parameters];
        Accumulate(points, beta, finalInfo, new double[parameters]);
        for (int i = 0; i < parameters; i++)
        {
            finalInfo[i, i] += Ridge;
        }
        double[,] covariance = Invert(finalInfo);
        double[] errors = new double[parameters];
        bool separated = !converged || covariance == null;
        for (int i = 0; i < parameters; i++)
        {
            errors[i] = covariance == null ? double.NaN : System.Math.Sqrt(System.Math.Max(0, covariance[i, i]));
            if (double.IsNaN(errors[i]) || errors[i] > SeparationStandardError)
            {
                separated = true;
            }
        }

        double pearson = 0;
        double totalWeight = 0;
        foreach (FitPoint point in points)
        {
            double mu = Probability(Eta(point.Row, beta));
            pearson += point.Weight * (point.Response - mu) * (point.Response - mu) / (mu * (1 - mu));
            totalWeight += point.Weight;
        }

        return new ProbitFitResult
        {
            Coefficients = beta,
            Covariance = covariance,
            StandardErrors = errors,
            Deviance = Deviance(points, beta),
            PearsonChiSquare = pearson,
            Points = points.Count,
            TotalWeight = totalWeight,
            Iterations = iteration,
            Converged = converged,
            Separated = separated
        };
    }

    /// <summary>The quasi-deviance of fixed coefficients against the saturated per-point model.</summary>
    public static double Deviance(IReadOnlyList<FitPoint> points, double[] beta)
    {
        double sum = 0;
        foreach (FitPoint point in points)
        {
            double mu = Probability(Eta(point.Row, beta));
            double y = point.Response;
            double term = 0;
            if (y > 0) term += y * System.Math.Log(y / mu);
            if (y < 1) term += (1 - y) * System.Math.Log((1 - y) / (1 - mu));
            sum += 2.0 * point.Weight * term;
        }
        return sum;
    }

    private static double Objective(IReadOnlyList<FitPoint> points, double[] beta)
    {
        double penalty = 0;
        foreach (double value in beta)
        {
            penalty += value * value;
        }
        return Deviance(points, beta) + Ridge * penalty;
    }

    private static double Eta(double[] row, double[] beta)
    {
        double eta = 0;
        for (int i = 0; i < row.Length; i++)
        {
            eta += row[i] * beta[i];
        }
        return eta;
    }

    private static double Probability(double eta) =>
        System.Math.Clamp(ProbitMath.NormalCdf(eta), ProbabilityFloor, 1 - ProbabilityFloor);

    // Builds X'WX and X'Wz for the current coefficients, where z is the working response.
    private static void Accumulate(IReadOnlyList<FitPoint> points, double[] beta, double[,] info, double[] rhs)
    {
        int parameters = beta.Length;
        foreach (FitPoint point in points)
        {
            double eta = Eta(point.Row, beta);
            double mu = Probability(eta);
            double density = System.Math.Max(ProbitMath.NormalPdf(eta), 1e-300);
            double weight = point.Weight * density * density / (mu * (1 - mu));
            double working = eta + (point.Response - mu) / density;
            for (int i = 0; i < parameters; i++)
            {
                double xi = point.Row[i];
                if (xi == 0) continue;
                rhs[i] += weight * xi * working;
                for (int j = 0; j < parameters; j++)
                {
                    info[i, j] += weight * xi * point.Row[j];
                }
            }
        }
    }

    /// <summary>Gauss-Jordan inverse with partial pivoting. Returns null for a singular matrix.</summary>
    internal static double[,] Invert(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        double[,] a = (double[,])matrix.Clone();
        double[,] inverse = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            inverse[i, i] = 1.0;
        }

        for (int column = 0; column < n; column++)
        {
            int pivot = column;
            for (int row = column + 1; row < n; row++)
            {
                if (System.Math.Abs(a[row, column]) > System.Math.Abs(a[pivot, column]))
                {
                    pivot = row;
                }
            }
            if (System.Math.Abs(a[pivot, column]) < 1e-14)
            {
                return null;
            }
            if (pivot != column)
            {
                for (int k = 0; k < n; k++)
                {
                    (a[column, k], a[pivot, k]) = (a[pivot, k], a[column, k]);
                    (inverse[column, k], inverse[pivot, k]) = (inverse[pivot, k], inverse[column, k]);
                }
            }

            double scale = a[column, column];
            for (int k = 0; k < n; k++)
            {
                a[column, k] /= scale;
                inverse[column, k] /= scale;
            }
            for (int row = 0; row < n; row++)
            {
                if (row == column) continue;
                double factor = a[row, column];
                if (factor == 0) continue;
                for (int k = 0; k < n; k++)
                {
                    a[row, k] -= factor * a[column, k];
                    inverse[row, k] -= factor * inverse[column, k];
                }
            }
        }
        return inverse;
    }
}
