using OnlyWar.Helpers.Turns;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

public class TurnIntelLedgerTests
{
    [Fact]
    public void ReconEvidence_UsesSixPointSoftCap()
    {
        Assert.Equal(0f, TurnIntelLedger.DiminishEvidence(0f));
        Assert.Equal(6f * (1f - (float)System.Math.Exp(-1f)),
            TurnIntelLedger.DiminishEvidence(6f), precision: 5);
        Assert.True(TurnIntelLedger.DiminishEvidence(100f) < 6f);
    }

    [Fact]
    public void ReconEvidence_DiminishesPositiveAndNegativePoolsSeparately()
    {
        float actual = TurnIntelLedger.CalculateReconAdjustment(10f, 9f);
        float netFirst = TurnIntelLedger.DiminishEvidence(1f);

        Assert.Equal(
            TurnIntelLedger.DiminishEvidence(10f) - TurnIntelLedger.DiminishEvidence(9f),
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
        observer.SetRegionIntel(region, 0.5f);
        TurnIntelLedger ledger = new();

        for (int i = 0; i < 5; i++)
        {
            ledger.RecordReconEvidence(observer, region, -2f);
        }
        ledger.Apply(fixture.Planet);

        Assert.Equal(0f, observer.GetRegionIntel(region));
    }

    [Fact]
    public void Apply_MultipleReconReportsUseOneCombinedCurve()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        PlanetFaction observer = fixture.DefaultPlanetFaction;
        TurnIntelLedger ledger = new();

        for (int i = 0; i < 5; i++)
        {
            ledger.RecordReconEvidence(observer, region, 2f);
        }
        ledger.Apply(fixture.Planet);

        Assert.Equal(
            TurnIntelLedger.DiminishEvidence(10f),
            observer.GetRegionIntel(region),
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
        TurnIntelLedger ledger = new();

        ledger.RecordReconEvidence(defaultObserver, region, 5f);
        ledger.RecordReconEvidence(playerObserver, region, 5f);
        ledger.Apply(fixture.Planet);

        float expected = TurnIntelLedger.DiminishEvidence(10f);
        Assert.Equal(expected, defaultObserver.GetRegionIntel(region), precision: 5);
        Assert.Equal(expected, playerObserver.GetRegionIntel(region), precision: 5);
    }
}
