using OnlyWar.Helpers.Simulation;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class CivilUnrestRulesTests
{
    [Fact]
    public void StructuralBaseline_CombinesTaxCompetenceAndCurvedHighSeverityPenalty()
    {
        Assert.Equal(70.0, CivilUnrestRules.CalculateStructuralBaseline(0.0, 0.5, 0.4), 8);
        Assert.Equal(45.0, CivilUnrestRules.CalculateStructuralBaseline(1.0, 0.5, 0.4), 8);
        Assert.Equal(80.0, CivilUnrestRules.CalculateStructuralBaseline(0.0, 1.0, 0.4), 8);
        Assert.Equal(60.0, CivilUnrestRules.CalculateStructuralBaseline(0.0, 0.5, 1.0), 8);
        Assert.Equal(67.5, CivilUnrestRules.CalculateStructuralBaseline(0.0, 0.5, 0.7), 8);
    }

    [Fact]
    public void SecurityBenefit_SaturatesAtThreePercentLoyalPdf()
    {
        Assert.Equal(0.0, CivilUnrestRules.CalculateSecurityBenefit(0, 10_000), 8);
        Assert.Equal(5.0, CivilUnrestRules.CalculateSecurityBenefit(150, 10_000), 8);
        Assert.Equal(10.0, CivilUnrestRules.CalculateSecurityBenefit(300, 10_000), 8);
        Assert.Equal(10.0, CivilUnrestRules.CalculateSecurityBenefit(3_000, 10_000), 8);
    }

    [Theory]
    [InlineData(9_000, 10_000, 0.0)]
    [InlineData(10_000, 10_000, 5.0)]
    [InlineData(11_000, 10_000, 10.0)]
    [InlineData(12_000, 10_000, 15.0)]
    [InlineData(20_000, 10_000, 15.0)]
    public void OvercrowdingPenalty_HasGentleOnsetAndCap(double population, double capacity, double expected)
    {
        Assert.Equal(expected, CivilUnrestRules.CalculateOvercrowdingPenalty(population, capacity), 8);
    }

    [Fact]
    public void ContentmentTarget_AppliesSecurityAndOvercrowdingAndClamps()
    {
        Assert.Equal(75.0, CivilUnrestRules.CalculateContentmentTarget(70, 300, 10_000, 10_000, 10_000), 8);
        Assert.Equal(100.0, CivilUnrestRules.CalculateContentmentTarget(98, 300, 10_000, 9_000, 10_000), 8);
    }

    [Fact]
    public void Contentment_ClosesThreePercentOfGapEachWeekInEitherDirection()
    {
        Assert.Equal(51.5, CivilUnrestRules.DriftContentment(50, 100), 8);
        Assert.Equal(48.5, CivilUnrestRules.DriftContentment(50, 0), 8);
    }

    [Theory]
    [InlineData(55, 0.0)]
    [InlineData(40, 0.0427281519)]
    [InlineData(25, 0.1208534639)]
    [InlineData(10, 0.2220219901)]
    [InlineData(0, 0.30)]
    public void TargetUnrestShare_FollowsContentmentCurve(double contentment, double expected)
    {
        Assert.Equal(expected, CivilUnrestRules.CalculateTargetUnrestShare(contentment), 8);
    }

    [Theory]
    [InlineData(55, 0.10)]
    [InlineData(27.5, 0.30)]
    [InlineData(0, 0.50)]
    public void ArmedCivilianFraction_GrowsAsContentmentFalls(double contentment, double expected)
    {
        Assert.Equal(expected, CivilUnrestRules.CalculateTargetArmedCivilianFraction(contentment), 8);
    }

    [Fact]
    public void UnrestAllegiance_ClosesFivePercentOfGapInEitherDirection()
    {
        Assert.Equal(0.11, CivilUnrestRules.DriftUnrestShare(0.10, 0.30), 8);
        Assert.Equal(0.19, CivilUnrestRules.DriftUnrestShare(0.20, 0.00), 8);
    }

    [Fact]
    public void PdfRecruitSelection_UsesSeventyPercentSusceptibilityWeight()
    {
        double expected = 700.0 / 9_700.0;

        Assert.Equal(expected, CivilUnrestRules.CalculatePdfRecruitSelectionChance(1_000, 9_000), 8);
        Assert.Equal(1.0, CivilUnrestRules.CalculatePdfRecruitSelectionChance(1_000, 0), 8);
        Assert.Equal(0.0, CivilUnrestRules.CalculatePdfRecruitSelectionChance(0, 9_000), 8);
    }

    [Fact]
    public void PublicThreshold_IsInclusiveAtTwoToOne_AndBlockedByExternalEnemy()
    {
        Assert.False(CivilUnrestRules.ShouldGoPublic(199, 100, false));
        Assert.True(CivilUnrestRules.ShouldGoPublic(200, 100, false));
        Assert.False(CivilUnrestRules.ShouldGoPublic(200, 100, true));
        Assert.True(CivilUnrestRules.ShouldGoPublic(1, 0, false));
        Assert.False(CivilUnrestRules.ShouldGoPublic(0, 0, false));
    }

    [Fact]
    public void HidingThreshold_IsExclusiveBelowOneToTwo()
    {
        Assert.True(CivilUnrestRules.ShouldReturnToHiding(49, 100));
        Assert.False(CivilUnrestRules.ShouldReturnToHiding(50, 100));
        Assert.False(CivilUnrestRules.ShouldReturnToHiding(200, 100));
    }

    [Fact]
    public void PublicAndHidingThresholds_ProvideHysteresis()
    {
        const double loyalStrength = 100;

        Assert.False(CivilUnrestRules.ShouldReturnToHiding(100, loyalStrength));
        Assert.False(CivilUnrestRules.ShouldGoPublic(100, loyalStrength, false));
    }

    [Fact]
    public void Migration_MovesFivePercentOfEligibleHiddenPopulation()
    {
        Assert.Equal(500.0, CivilUnrestRules.CalculateWeeklyMigration(10_000), 8);
        Assert.Equal(0.0, CivilUnrestRules.CalculateWeeklyMigration(-10), 8);
    }
}
