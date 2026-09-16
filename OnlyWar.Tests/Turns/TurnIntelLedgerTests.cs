using OnlyWar.Campaign.Turns;
using OnlyWar.Domain.Planets;
using OnlyWar.Operations.Missions.Recon;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class TurnIntelligenceLedgerTests
{
    [Fact]
    public void ReconAdjustment_NetsThePoolsBeforeApplyingTheCurve()
    {
        // Ten good and nine bad is a net of one, not a large positive minus a large negative.
        // Diminishing each pool on its own used to make a mixed week read far worse than it was.
        Assert.Equal(
            ReconIntelligenceRules.AwarenessDelta(1f),
            TurnIntelligenceLedger.CalculateReconAdjustment(10f, 9f),
            precision: 5);
    }

    [Fact]
    public void ReconAdjustment_DiminishesWithTheSizeOfTheResult()
    {
        float small = TurnIntelligenceLedger.CalculateReconAdjustment(4f, 0f);
        float large = TurnIntelligenceLedger.CalculateReconAdjustment(16f, 0f);

        // Four times the margin is worth twice the awareness, before the cap.
        Assert.True(large > small);
        Assert.True(large < small * 4f);
    }

    [Fact]
    public void ReconAdjustment_BoundsABadWeekAtOnePoint()
    {
        // A sweep can bring back wrong intelligence, but it cannot unlearn the ground.
        Assert.Equal(
            ReconIntelligenceRules.MinimumAwarenessDelta,
            TurnIntelligenceLedger.CalculateReconAdjustment(0f, 50f));
        Assert.Equal(
            ReconIntelligenceRules.MaximumAwarenessDelta,
            TurnIntelligenceLedger.CalculateReconAdjustment(50f, 0f));
    }

    [Fact]
    public void Apply_ReconEvidenceIsPackagingInvariantAndCannotLowerIntelBelowZero()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        PlanetFaction observer = fixture.DefaultPlanetFaction;
        observer.SetRegionAwareness(region, 0.5f);
        TurnIntelligenceLedger ledger = new();

        for (int i = 0; i < 5; i++)
        {
            ledger.RecordReconEvidence(observer, region, -2f);
        }
        ledger.Apply(fixture.Planet);

        Assert.Equal(0f, observer.GetRegionAwareness(region));
    }

    [Fact]
    public void Apply_MultipleReconReportsUseOneCombinedCurve()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        PlanetFaction observer = fixture.DefaultPlanetFaction;
        TurnIntelligenceLedger ledger = new();

        for (int i = 0; i < 5; i++)
        {
            ledger.RecordReconEvidence(observer, region, 2f);
        }
        ledger.Apply(fixture.Planet);

        Assert.Equal(
            ReconIntelligenceRules.AwarenessDelta(10f),
            observer.GetRegionAwareness(region),
            precision: 5);
    }

    [Fact]
    public void Apply_IntelSharingFactionsPoolReconBeforeDiminishingIt()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        PlanetFaction defaultObserver = fixture.DefaultPlanetFaction;
        PlanetFaction playerObserver = new(fixture.Sector.PlayerForce.Faction);
        fixture.Planet.PlanetFactionMap[playerObserver.Faction.Id] = playerObserver;
        TurnIntelligenceLedger ledger = new();

        ledger.RecordReconEvidence(defaultObserver, region, 5f);
        ledger.RecordReconEvidence(playerObserver, region, 5f);
        ledger.Apply(fixture.Planet);

        float expected = ReconIntelligenceRules.AwarenessDelta(10f);
        Assert.Equal(expected, defaultObserver.GetRegionAwareness(region), precision: 5);
        Assert.Equal(expected, playerObserver.GetRegionAwareness(region), precision: 5);
    }
}
