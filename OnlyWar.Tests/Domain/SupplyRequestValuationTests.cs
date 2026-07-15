using OnlyWar.Helpers.Supply;
using OnlyWar.Models.Supply;
using System;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SupplyRequestValuationTests
{
    [Fact]
    public void Calculate_DerivesEffortFromReadablePackage()
    {
        ForceCommitmentPackage package = CreatePackage(referenceBattleValue: 250, packageCount: 1,
            serviceWeeks: 4, deadlineWeeks: 4);

        RequestValuationResult result = RequestValueCalculator.Calculate(
            package, CreateRules(requisitionPerBvt: 0.1m), null, hazardMultiplier: 1m);

        Assert.Equal(1_000, result.EffortBattleValueTime);
        Assert.Equal(250, result.RequiredBattleValuePerWeek);
        Assert.Equal(100, result.RequisitionValue);
        Assert.Equal("Scout squad", package.DisplayUnitName);
    }

    [Fact]
    public void Calculate_UsesDeadlineToSelectThroughputPremium()
    {
        RequestValuationRules rules = CreateRules(
            requisitionPerBvt: 0.1m,
            new ThroughputPremiumBand(100, 1m),
            new ThroughputPremiumBand(250, 1.1m),
            new ThroughputPremiumBand(500, 1.25m));
        ForceCommitmentPackage package = CreatePackage(referenceBattleValue: 250, packageCount: 2,
            serviceWeeks: 2, deadlineWeeks: 2);

        RequestValuationResult result = RequestValueCalculator.Calculate(package, rules, null, 1m);

        Assert.Equal(500, result.RequiredBattleValuePerWeek);
        Assert.Equal(1.25m, result.ThroughputMultiplier);
        Assert.Equal(125, result.RequisitionValue);
    }

    [Fact]
    public void Calculate_UsesHighestQualificationPremiumWithinEachGroup()
    {
        QualificationPremium[] qualifications =
        {
            new("force", "scout", 1.2m),
            new("force", "vanguard", 1.1m),
            new("operation", "covert", 1.25m),
        };

        RequestValuationResult result = RequestValueCalculator.Calculate(
            CreatePackage(), CreateRules(0.1m), qualifications, hazardMultiplier: 1m);

        Assert.Equal(1.5m, result.QualificationMultiplier);
        Assert.Equal(150, result.RequisitionValue);
    }

    [Fact]
    public void Calculate_MultipliesHazardAndCapsCombinedPremium()
    {
        RequestValuationRules rules = new(
            0.1m,
            new[] { new ThroughputPremiumBand(long.MaxValue, 2m) },
            minimumRequestValue: 0,
            maximumRequestValue: 10_000,
            maximumCombinedPremium: 3m);

        RequestValuationResult result = RequestValueCalculator.Calculate(
            CreatePackage(), rules,
            new[] { new QualificationPremium("force", "scout", 2m) },
            hazardMultiplier: 2m);

        Assert.Equal(300, result.RequisitionValue);
    }

    [Fact]
    public void Calculate_RoundsHalfAwayFromZeroAndClampsResult()
    {
        RequestValuationResult rounded = RequestValueCalculator.Calculate(
            CreatePackage(referenceBattleValue: 5, serviceWeeks: 1),
            CreateRules(0.1m), null, 1m);
        RequestValuationResult clamped = RequestValueCalculator.Calculate(
            CreatePackage(referenceBattleValue: 1_000, serviceWeeks: 1),
            new RequestValuationRules(1m,
                new[] { new ThroughputPremiumBand(long.MaxValue, 1m) }, 10, 200),
            null, 1m);

        Assert.Equal(1, rounded.RequisitionValue);
        Assert.Equal(200, clamped.RequisitionValue);
    }

    [Fact]
    public void Calculate_CopiesAndDeduplicatesQualificationTags()
    {
        string[] tags = { "Scout", "scout", "Covert" };
        ForceCommitmentPackage package = CreatePackage(tags: tags);

        tags[0] = "Changed";

        Assert.Equal(new[] { "Scout", "Covert" }, package.QualificationTags);
    }

    [Fact]
    public void GovernorOffer_AppliesWillingnessAfterRequestIsPriced()
    {
        GovernorWillingness willingness = new(
            DesperationMultiplier: 1.5m,
            RelationshipMultiplier: 1.2m,
            AuthorityMultiplier: 1.1m);

        int offer = GovernorOfferCalculator.Calculate(
            100, willingness, new GovernorOfferRules(10, 1_000));

        Assert.Equal(198, offer);
    }

    [Fact]
    public void GovernorOffer_ClampsWillingnessAndOfferDeterministically()
    {
        GovernorOfferRules rules = new(
            MinimumOffer: 25,
            MaximumOffer: 250,
            MinimumWillingnessMultiplier: 0.5m,
            MaximumWillingnessMultiplier: 2m);

        int low = GovernorOfferCalculator.Calculate(
            10, new GovernorWillingness(0m, 0m, 0m), rules);
        int high = GovernorOfferCalculator.Calculate(
            200, new GovernorWillingness(10m, 10m, 10m), rules);

        Assert.Equal(25, low);
        Assert.Equal(250, high);
    }

    [Fact]
    public void CommitmentPackage_RejectsInvalidQuantities()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePackage(packageCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePackage(serviceWeeks: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePackage(deadlineWeeks: 0));
    }

    private static ForceCommitmentPackage CreatePackage(
        long referenceBattleValue = 250,
        int packageCount = 1,
        int serviceWeeks = 4,
        int deadlineWeeks = 4,
        string[] tags = null)
    {
        return new ForceCommitmentPackage(
            "scout_squad",
            "Scout investigation detail",
            "Scout squad",
            packageCount,
            serviceWeeks,
            deadlineWeeks,
            referenceBattleValue,
            tags);
    }

    private static RequestValuationRules CreateRules(
        decimal requisitionPerBvt,
        params ThroughputPremiumBand[] bands)
    {
        if (bands.Length == 0)
            bands = new[] { new ThroughputPremiumBand(long.MaxValue, 1m) };

        return new RequestValuationRules(
            requisitionPerBvt,
            bands,
            minimumRequestValue: 0,
            maximumRequestValue: 10_000);
    }
}
