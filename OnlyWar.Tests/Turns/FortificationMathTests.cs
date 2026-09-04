using OnlyWar.Helpers.Fortifications;
using System;
using Xunit;

namespace OnlyWar.Tests.Turns;

// The combining curve is not a free choice: it is the inverse of the build economy's 10x-per-band
// cost (FactionDevelopmentPlanner.DefenseBuildCost). These tests pin the properties that make
// shared fortifications honest - that allies cannot buy a cheap high level by splitting the work,
// and that a handover between allies does not change what the side holds.
public class FortificationMathTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    // The repunit sequence: reaching level n costs 1, 11, 111 ... points, because each band costs
    // ten times the one below it.
    [InlineData(2.0, 11.0)]
    [InlineData(3.0, 111.0)]
    public void LevelToPoints_FollowsTheCumulativeBuildCost(double level, double expectedPoints)
    {
        Assert.Equal(expectedPoints, FortificationMath.LevelToPoints(level), 4);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(6.25)]
    public void PointsToLevel_InvertsLevelToPoints(double level)
    {
        Assert.Equal(
            level,
            FortificationMath.PointsToLevel(FortificationMath.LevelToPoints(level)),
            6);
    }

    [Fact]
    public void Combine_TwoEqualAllies_IsFarBelowTheirSum()
    {
        // Two factions that have each paid for one level do not hold a level-2 position: that
        // would cost eleven points, and between them they have spent two.
        Assert.Equal(1.2788, FortificationMath.Combine(1.0, 1.0), 4);
    }

    [Fact]
    public void Combine_ASmallAllyBesideALargeOne_BarelyMovesThePosition()
    {
        Assert.Equal(3.0039, FortificationMath.Combine(3.0, 1.0), 4);
    }

    [Fact]
    public void Combine_DoublingTheInvestment_BuysLog10Of2Levels()
    {
        // The defining property: because each level costs 10x the last, twice the investment is
        // worth log10(2) = 0.301 of a level, wherever on the curve you are. Exact in the limit -
        // the "-1" in the repunit still shows at low levels (1 + 1 gives 1.279, not 1.301).
        foreach (double level in new[] { 4.0, 6.0, 8.0 })
        {
            Assert.Equal(level + System.Math.Log10(2.0), FortificationMath.Combine(level, level), 3);
        }
    }

    [Fact]
    public void Combine_IsAssociative_SoCustodyDoesNotChangeThePosition()
    {
        // What makes transferring works between allies safe: pooling A and B then handing the lot
        // to C is the same position as A holding everything from the start.
        double viaTransfer = FortificationMath.Combine(FortificationMath.Combine(2.0, 1.0), 0.5);
        double allAtOnce = FortificationMath.Combine(2.0, FortificationMath.Combine(1.0, 0.5));

        Assert.Equal(allAtOnce, viaTransfer, 9);
    }

    [Fact]
    public void AddPoints_FlatWeeklyEffort_DeceleratesAcrossLevels()
    {
        // A squad's output does not depend on how good the works already are, so a fixed number of
        // points per week must climb fast at first and then flatten. This is what stops marines
        // buying a level of Massive works for the week of labour that bought their first Minimal.
        const double weekly = 0.6;
        double firstWeek = FortificationMath.AddPoints(0.0, weekly);
        double atLevelTwo = FortificationMath.AddPoints(2.0, weekly) - 2.0;
        double atLevelThree = FortificationMath.AddPoints(3.0, weekly) - 3.0;

        Assert.True(firstWeek > 0.75, $"first week should clear 0.75, was {firstWeek:F3}");
        Assert.True(atLevelTwo < 0.03, $"level 2 gain should be marginal, was {atLevelTwo:F4}");
        Assert.True(atLevelThree < atLevelTwo);
    }

    [Fact]
    public void SharedContributionEfficiency_IsOneWhenNoAllyHoldsWorks()
    {
        Assert.Equal(1.0, FortificationMath.SharedContributionEfficiency(2.0, 2.0), 9);
        Assert.Equal(1.0, FortificationMath.SharedContributionEfficiency(2.0, 1.5), 9);
    }

    [Fact]
    public void SharedContributionEfficiency_FallsAwayWhenAnAllyIsAhead()
    {
        // A faction at level 1 adding to a shared position of 3 buys a hundredth of the position
        // per point, so a planner must discount the benefit or it will keep buying cheap nothings.
        Assert.Equal(0.01, FortificationMath.SharedContributionEfficiency(1.0, 3.0), 9);
    }
}
