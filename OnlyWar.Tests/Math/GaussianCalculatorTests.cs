using System;
using OnlyWar.Helpers;
using Xunit;

namespace OnlyWar.Tests.Math;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
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

    [Fact]
    public void DetermineMarginOfSuccessZvalue_IsDeterministicForSeededRng()
    {
        RNG.Reset(123);
        float first = GaussianCalculator.DetermineMarginOfSuccessZvalue(0.25f);

        RNG.Reset(123);
        float second = GaussianCalculator.DetermineMarginOfSuccessZvalue(0.25f);

        Assert.Equal(first, second);
    }
}
