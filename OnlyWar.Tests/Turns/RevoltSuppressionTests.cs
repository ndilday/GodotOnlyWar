using OnlyWar.Helpers;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class RevoltSuppressionTests
{
    [Fact]
    public void Suppression_DoesNotHideWeakenedConsumptionFaction()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction tyranids = fixture.AddConsumptionFaction(0, population: 100, organization: 100);
        MakeImperialControllerOverwhelming(fixture);

        fixture.ProcessTurn();

        Assert.True(tyranids.PlanetFaction.IsPublic);
        Assert.True(tyranids.IsPublic);
    }

    [Fact]
    public void Suppression_HidesWeakenedPublicConversionFaction()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddPublicCult(0, population: 100, organization: 100);
        MakeImperialControllerOverwhelming(fixture);

        fixture.ProcessTurn();

        Assert.False(cult.PlanetFaction.IsPublic);
        Assert.False(cult.IsPublic);
    }

    private static void MakeImperialControllerOverwhelming(SectorSimulationFixture fixture)
    {
        foreach (Region region in fixture.Planet.Regions)
        {
            fixture.DefaultRegionFaction(region.Id).Garrison = 10_000;
        }
    }
}
