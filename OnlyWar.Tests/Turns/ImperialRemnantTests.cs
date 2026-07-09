using OnlyWar.Helpers;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Coverage for the Imperial remnant hide/unhide lifecycle and civilian emigration (PRD §4.24):
// a governing population goes to ground when its garrison falls under a public enemy, surfaces
// again on liberation, neither grows nor drafts while hidden, and bleeds survivors to adjacent
// governed regions each week. Regions 1 and 2 are the only neighbours of region 0 in the fixture's
// diamond grid, so region 0 is used as the source throughout.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ImperialRemnantTests
{
    [Fact]
    public void Remnant_HidesWhenGarrisonFallsWithPublicEnemyPresent()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Garrison = 0;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.False(remnant.IsPublic);
    }

    [Fact]
    public void Remnant_StaysPublicWhileGarrisonHolds()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Garrison = 1_000; // besieged but still defending
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.True(remnant.IsPublic);
    }

    [Fact]
    public void Remnant_StaysPublicWithNoEnemyEvenWithoutGarrison()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Garrison = 0; // a peaceful region with no standing garrison does not hide

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.True(remnant.IsPublic);
    }

    [Fact]
    public void Remnant_SurfacesWhenLastPublicEnemyIsCleared()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false; // overrun, in hiding
        remnant.Garrison = 0;
        // no public enemy in the region → liberation

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.True(remnant.IsPublic);
    }

    [Fact]
    public void Remnant_StaysHiddenWhilePublicEnemyRemains()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Garrison = 0;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.False(remnant.IsPublic);
    }

    [Fact]
    public void Emigration_MovesFivePercentToAdjacentGovernedRegions()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Population = 100_000;
        // neighbours (regions 1 and 2) each start governed with population 20,000

        TurnController.ProcessImperialEmigration(fixture.Planet.Regions[0]);

        Assert.Equal(95_000, remnant.Population);
        // 5% of 100,000 = 5,000, split evenly between two equally-populated refuges
        Assert.Equal(22_500, fixture.DefaultRegionFaction(1).Population);
        Assert.Equal(22_500, fixture.DefaultRegionFaction(2).Population);
    }

    [Fact]
    public void Emigration_DistributesByRefugePopulation()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Population = 100_000;
        fixture.DefaultRegionFaction(1).Population = 30_000;
        fixture.DefaultRegionFaction(2).Population = 10_000;

        TurnController.ProcessImperialEmigration(fixture.Planet.Regions[0]);

        // 5,000 refugees split 3:1 by the refuges' 30k/10k populations
        Assert.Equal(33_750, fixture.DefaultRegionFaction(1).Population);
        Assert.Equal(11_250, fixture.DefaultRegionFaction(2).Population);
    }

    [Fact]
    public void Emigration_DoesNothingWhenSurrounded()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Population = 100_000;
        // both neighbours are themselves overrun — no governed refuge to flee to
        fixture.DefaultRegionFaction(1).IsPublic = false;
        fixture.DefaultRegionFaction(2).IsPublic = false;

        TurnController.ProcessImperialEmigration(fixture.Planet.Regions[0]);

        Assert.Equal(100_000, remnant.Population);
    }

    [Fact]
    public void Emigration_SkipsAGoverningRemnant()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction governing = fixture.DefaultRegionFaction(0);
        governing.Population = 100_000; // public: not in hiding, does not flee

        TurnController.ProcessImperialEmigration(fixture.Planet.Regions[0]);

        Assert.Equal(100_000, governing.Population);
        Assert.Equal(20_000, fixture.DefaultRegionFaction(1).Population);
    }

    [Fact]
    public void HiddenRemnant_NeitherGrowsNorAccruesGarrison()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Population = 1_000_000;
        remnant.Garrison = 0;
        remnant.IsPublic = false; // gone to ground

        new TurnController().EndOfTurnRegionFactionsUpdate(remnant, pdfRatio: 0.5f);

        Assert.Equal(1_000_000, remnant.Population);
        Assert.Equal(0, remnant.Garrison);
    }

    [Fact]
    public void GoverningRemnant_GrowsAndDraftsGarrison()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 10_000_000;
        region.MaximumCarryingCapacity = 10_000_000;
        RegionFaction governing = fixture.DefaultRegionFaction(0);
        governing.Population = 1_000_000;
        governing.Garrison = 0;

        new TurnController().EndOfTurnRegionFactionsUpdate(governing, pdfRatio: 0.0f);

        Assert.True(governing.Population > 1_000_000, "a governing population grows organically");
        Assert.True(governing.Garrison > 0, "a governing PDF drafts from its growth");
    }
}
