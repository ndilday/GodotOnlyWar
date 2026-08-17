using OnlyWar.Helpers.Turns;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class TurnIntelligenceLedgerTests
{
    [Fact]
    public void ReconEvidence_UsesSixPointSoftCap()
    {
        Assert.Equal(0f, TurnIntelligenceLedger.DiminishEvidence(0f));
        Assert.Equal(6f * (1f - (float)System.Math.Exp(-1f)),
            TurnIntelligenceLedger.DiminishEvidence(6f), precision: 5);
        Assert.True(TurnIntelligenceLedger.DiminishEvidence(100f) < 6f);
    }

    [Fact]
    public void ReconEvidence_DiminishesPositiveAndNegativePoolsSeparately()
    {
        float actual = TurnIntelligenceLedger.CalculateReconAdjustment(10f, 9f);
        float netFirst = TurnIntelligenceLedger.DiminishEvidence(1f);

        Assert.Equal(
            TurnIntelligenceLedger.DiminishEvidence(10f) - TurnIntelligenceLedger.DiminishEvidence(9f),
            actual,
            precision: 5);
        Assert.True(actual < netFirst);
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
            TurnIntelligenceLedger.DiminishEvidence(10f),
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

        float expected = TurnIntelligenceLedger.DiminishEvidence(10f);
        Assert.Equal(expected, defaultObserver.GetRegionAwareness(region), precision: 5);
        Assert.Equal(expected, playerObserver.GetRegionAwareness(region), precision: 5);
    }
}
