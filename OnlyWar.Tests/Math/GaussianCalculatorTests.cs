using System;
using OnlyWar.Helpers;
using Xunit;

namespace OnlyWar.Tests.Math;

public class GaussianCalculatorTests
{
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1, 0.8413)]
    [InlineData(2, 0.9772)]
    [InlineData(-1, 0.1587)]
    public void ApproximateNormalCdf_MatchesKnownReferencePoints(float zScore, double expected)
    {
        float actual = GaussianCalculator.ApproximateNormalCDF(zScore);

        Assert.InRange(actual, expected - 0.0015, expected + 0.0015);
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.75f)]
    [InlineData(0.9f)]
    [InlineData(0.99f)]
    public void ApproximateNormalCdf_RoundTripsWithApproximateInverse(float probability)
    {
        float z = GaussianCalculator.ApproximateInverseNormalCDF(probability);
        float cdf = GaussianCalculator.ApproximateNormalCDF(z);

        Assert.InRange(cdf, probability - 0.005f, probability + 0.005f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void ApproximateInverseNormalCdf_RejectsInvalidProbability(float probability)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianCalculator.ApproximateInverseNormalCDF(probability));
    }

    // ---------------------------------------------------------------------------------------
    // The CDF is a 16KB interpolated table over |z| < 6 with the closed form beyond it. These
    // tests pin the two properties the battle scorers actually rely on -- agreement with the
    // formula the table caches, and a tail that stays nonzero -- plus the structural invariants
    // (monotone, symmetric, continuous at the seam) that a table can break in ways a handful of
    // reference points would not catch.
    //
    // ReferenceUpperTail is a deliberate SECOND implementation of Abramowitz and Stegun 26.2.17
    // rather than a call into the production one, so the sweep compares two independent paths.
    // ---------------------------------------------------------------------------------------

    private static double ReferenceUpperTail(double x)
    {
        double t = 1.0 / (1.0 + (0.2316419 * x));
        double poly = t * (0.319381530
            + (t * (-0.356563782
                + (t * (1.781477937
                    + (t * (-1.821255978 + (t * 1.330274429))))))));
        return 0.3989422804014327 * System.Math.Exp(-0.5 * x * x) * poly;
    }

    private static double ReferenceCdf(double z) =>
        z < 0 ? ReferenceUpperTail(-z) : 1.0 - ReferenceUpperTail(z);

    [Fact]
    public void ApproximateNormalCdf_MatchesClosedFormAcrossTheDomain()
    {
        double worst = 0;
        float worstAt = 0;
        for (float z = -8f; z <= 8f; z += 0.001f)
        {
            double error = System.Math.Abs(
                GaussianCalculator.ApproximateNormalCDF(z) - ReferenceCdf(z));
            if (error > worst)
            {
                worst = error;
                worstAt = z;
            }
        }

        // Linear interpolation error is (h^2/8)*max|Phi''| = 6.5e-8 at h = 6/4096; the rest is
        // float rounding on a value near 1.
        Assert.True(worst < 2e-7, $"worst absolute error {worst:E3} at z={worstAt}");
    }

    [Fact]
    public void ApproximateNormalCdf_KeepsRelativeAccuracyInTheTail()
    {
        // The band the burst model reads: ExpectedBurstRemovalFraction distinguishes a ~1e-7
        // "barely worth shooting" rate from "cannot shoot at all", so absolute error is not the
        // relevant measure out here -- a table that is accurate to 1e-8 absolutely is still
        // useless if it reports 1e-9 as 0.
        double worst = 0;
        float worstAt = 0;
        for (float z = -7f; z <= -0.5f; z += 0.001f)
        {
            double expected = ReferenceCdf(z);
            double relative = System.Math.Abs(
                (GaussianCalculator.ApproximateNormalCDF(z) - expected) / expected);
            if (relative > worst)
            {
                worst = relative;
                worstAt = z;
            }
        }

        Assert.True(worst < 1e-4, $"worst relative error {worst:E3} at z={worstAt}");
    }

    [Theory]
    [InlineData(-6f)]
    [InlineData(-8f)]
    [InlineData(-12f)]
    public void ApproximateNormalCdf_TailIsNeverFlushedToZero(float zScore)
    {
        Assert.True(GaussianCalculator.ApproximateNormalCDF(zScore) > 0f);
    }

    [Fact]
    public void ApproximateNormalCdf_IsMonotonic()
    {
        float previous = GaussianCalculator.ApproximateNormalCDF(-9f);
        for (float z = -9f; z <= 9f; z += 0.0005f)
        {
            float current = GaussianCalculator.ApproximateNormalCDF(z);
            // Tested rather than passed to Assert.True: the interpolated message would otherwise
            // be built on all 36,000 iterations instead of the one that fails.
            if (current < previous)
            {
                Assert.Fail($"decreased at z={z}: {previous} -> {current}");
            }
            previous = current;
        }
    }

    [Fact]
    public void ApproximateNormalCdf_IsContinuousAcrossTheTableBoundary()
    {
        // The seam at |z| = 6, where the interpolated table hands off to the closed form. Both
        // sides are generated from the same approximation, so the step should be nothing but
        // float rounding -- an obvious way to get the table's domain or scale off by one.
        foreach (float edge in new[] { 6f, -6f })
        {
            float inside = GaussianCalculator.ApproximateNormalCDF(
                edge > 0 ? edge - 1e-4f : edge + 1e-4f);
            float outside = GaussianCalculator.ApproximateNormalCDF(edge);

            Assert.True(
                System.Math.Abs(inside - outside) < 1e-7f,
                $"discontinuity at {edge}: {inside} vs {outside}");
        }
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.5f)]
    [InlineData(3f)]
    [InlineData(5.5f)]
    [InlineData(7f)]
    public void ApproximateNormalCdf_IsSymmetricAboutZero(float zScore)
    {
        float positive = GaussianCalculator.ApproximateNormalCDF(zScore);
        float negative = GaussianCalculator.ApproximateNormalCDF(-zScore);

        Assert.InRange(positive + negative, 1f - 1e-6f, 1f + 1e-6f);
    }

    [Fact]
    public void DetermineMarginOfSuccessZvalue_IsDeterministicForSeededRng()
    {
        float first = GaussianCalculator.DetermineMarginOfSuccessZvalue(
            0.25f,
            new SeededRNG(123));

        float second = GaussianCalculator.DetermineMarginOfSuccessZvalue(
            0.25f,
            new SeededRNG(123));

        Assert.Equal(first, second);
    }
}
