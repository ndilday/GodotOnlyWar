using OnlyWar.Helpers.Supply;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using System;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SupplyRequestValuationTests
{
    [Fact]
    public void CodeOwnedDefaults_PreserveShippedSupplyCalibration()
    {
        SupplyEconomyRules rules = SupplyEconomyRules.CreateDefault();

        Assert.Equal(0.25m, rules.RequestValuation.RequisitionPerBattleValueTime);
        Assert.Equal(25, rules.RequestValuation.MinimumRequestValue);
        Assert.Equal(1000, rules.RequestValuation.MaximumRequestValue);
        Assert.Equal(4.0m, rules.RequestValuation.MaximumCombinedPremium);
        Assert.Equal(5, rules.RequestValuation.ThroughputBands.Count);
        Assert.Equal(1.5m, rules.RequestValuation.ThroughputBands[3].Multiplier);
        Assert.Equal(25, rules.GovernorOffers.MinimumOffer);
        Assert.Equal(1500, rules.GovernorOffers.MaximumOffer);
        Assert.Equal(0.5m, rules.GovernorOffers.MinimumWillingnessMultiplier);
        Assert.Equal(2.0m, rules.GovernorOffers.MaximumWillingnessMultiplier);
        Assert.Equal(4, rules.DefaultServiceWeeks);
        Assert.Equal(8, rules.DefaultDeadlineWeeks);
        Assert.Equal(4, rules.DefaultDeliveryWeeks);
        Assert.Equal(52, rules.StandingCadenceWeeks);
        Assert.Equal(0.2m, rules.StandingDeliveryFraction);
        Assert.Equal(300, rules.StandingMinimumOffer);
        Assert.Equal(8, rules.RequestCooldownWeeks);
        Assert.Equal(0.006m, rules.RequestGenerationRate);
        Assert.Equal(39, rules.SeverityDeadlineWeeks[RequestSeverity.Concerned]);
        Assert.Equal(26, rules.SeverityDeadlineWeeks[RequestSeverity.Serious]);
        Assert.Equal(13, rules.SeverityDeadlineWeeks[RequestSeverity.Desperate]);
        Assert.Equal(13, rules.SeverityDeadlineWeeks[RequestSeverity.Existential]);
        Assert.Equal(1.8m, rules.HazardMultipliers[RequestHazard.Extreme]);
        Assert.Equal(1.25m, rules.AuthorityMultipliers[GovernanceTier.SectorCapital]);
        Assert.Equal(2.0m, rules.DesperationMultipliers[RequestSeverity.Existential]);
        Assert.Equal(1.2m, rules.WorldRequisitionMultipliers[SupplyWorldArchetype.Forge]);
        Assert.Equal(0.75m, rules.RelationshipBaseMultiplier);
        Assert.Equal(0.5m, rules.RelationshipOpinionScale);
        Assert.Equal(1.0m, rules.GetWorldRequisitionMultiplier(
            new OnlyWar.Models.Planets.PlanetTemplate(
                999,
                "Unknown",
                1,
                new OnlyWar.Models.LogNormalValueTemplate { Floor = 0, Scale = 1 },
                new OnlyWar.Models.LogNormalValueTemplate { Floor = 0, Scale = 1 },
                new OnlyWar.Models.NormalizedValueTemplate { BaseValue = 0, StandardDeviation = 1 },
                new OnlyWar.Models.LinearValueTemplate { MinValue = 0, MaxValue = 1 })));
    }

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
