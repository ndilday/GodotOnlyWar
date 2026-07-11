using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Coverage for the Imperial remnant hide/unhide lifecycle and civilian emigration (PRD §4.24):
// a governing population goes to ground when its garrison falls under a public enemy, surfaces
// again on liberation OR when its armed underground can outfight the occupier, keeps growing while
// hidden (arming that growth into its garrison), and bleeds survivors to adjacent governed regions
// each week. Regions 1 and 2 are the only neighbours of region 0 in the fixture's diamond grid, so
// region 0 is used as the source throughout.
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
        // Region 0 (Alpha) borders three governed regions on the hex board — 1 (Beta),
        // 2 (Gamma) and 4 (Epsilon) — each starting with population 20,000.

        TurnController.ProcessImperialEmigration(fixture.Planet.Regions[0]);

        Assert.Equal(95_000, remnant.Population);
        // 5% of 100,000 = 5,000, split across three equally-populated refuges (the last
        // absorbs the rounding remainder, so shares are within one refugee of even).
        long totalRefuge = fixture.DefaultRegionFaction(1).Population
            + fixture.DefaultRegionFaction(2).Population
            + fixture.DefaultRegionFaction(4).Population;
        Assert.Equal(65_000, totalRefuge);
        foreach (int r in new[] { 1, 2, 4 })
        {
            Assert.InRange(fixture.DefaultRegionFaction(r).Population, 21_666, 21_668);
        }
    }

    [Fact]
    public void Emigration_DistributesByRefugePopulation()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Population = 100_000;
        // Region 0 (Alpha) borders three governed regions: 1 (Beta), 2 (Gamma), 4 (Epsilon).
        fixture.DefaultRegionFaction(1).Population = 30_000;
        fixture.DefaultRegionFaction(2).Population = 10_000;
        fixture.DefaultRegionFaction(4).Population = 10_000;

        TurnController.ProcessImperialEmigration(fixture.Planet.Regions[0]);

        // 5,000 refugees split 3:1:1 by the refuges' 30k/10k/10k populations
        Assert.Equal(33_000, fixture.DefaultRegionFaction(1).Population);
        Assert.Equal(11_000, fixture.DefaultRegionFaction(2).Population);
        Assert.Equal(11_000, fixture.DefaultRegionFaction(4).Population);
    }

    [Fact]
    public void Emigration_DoesNothingWhenSurrounded()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Population = 100_000;
        // all three neighbours (1, 2, 4) are themselves overrun — no governed refuge to flee to
        fixture.DefaultRegionFaction(1).IsPublic = false;
        fixture.DefaultRegionFaction(2).IsPublic = false;
        fixture.DefaultRegionFaction(4).IsPublic = false;

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
    public void HiddenRemnant_GrowsAndArmsItsGarrison()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 10_000_000;
        region.MaximumCarryingCapacity = 10_000_000;
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Population = 1_000_000;
        remnant.Garrison = 0;
        remnant.IsPublic = false; // gone to ground, but still a living resistance

        new TurnController().EndOfTurnRegionFactionsUpdate(remnant, pdfRatio: 0.5f);

        Assert.True(remnant.Population > 1_000_000, "a hidden resistance still has children");
        Assert.True(remnant.Garrison > 0, "and arms that growth into the underground");
        Assert.True(remnant.Garrison <= remnant.Population, "garrison never exceeds population");
    }

    [Fact]
    public void HiddenRemnant_RisesToRetakeWhenLoyalStrengthOutweighsEnemy()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Population = 200_000;
        remnant.Garrison = 100_000; // a strong armed underground
        // a weak public swarm still holds ground in the region
        fixture.AddConsumptionFaction(0, population: 1_000, organization: 100);

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.True(remnant.IsPublic, "the resistance rises to retake the region when it can win");
    }

    [Fact]
    public void HiddenRemnant_StaysHiddenWhenEnemyOutweighsLoyalStrength()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Population = 200_000;
        remnant.Garrison = 100; // a token underground, hopelessly outnumbered
        fixture.AddConsumptionFaction(0, population: 500_000, organization: 100);

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.False(remnant.IsPublic, "a weak remnant stays hidden while the occupier is stronger");
    }

    [Fact]
    public void Remnant_HalvesDefensesWhenGoingToGround()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Garrison = 0;
        remnant.Entrenchment = 8;
        remnant.ListeningPost = 4;
        remnant.AntiAir = 2;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.False(remnant.IsPublic);
        // falling to the occupier wrecks or forfeits half the defensive works
        Assert.Equal(4.0, remnant.Entrenchment);
        Assert.Equal(2.0, remnant.ListeningPost);
        Assert.Equal(1.0, remnant.AntiAir);
    }

    [Fact]
    public void Remnant_KeepsDefensesWhileStillGoverning()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Garrison = 1_000; // besieged but still holding
        remnant.Entrenchment = 8;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        TurnController.UpdateImperialRemnantState(fixture.Planet.Regions[0]);

        Assert.True(remnant.IsPublic);
        Assert.Equal(8.0, remnant.Entrenchment);
    }

    [Fact]
    public void HiddenRemnant_DefensesDecayUnderOccupation()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Garrison = 0;
        remnant.Entrenchment = 4;
        remnant.ListeningPost = 1;
        remnant.AntiAir = 0.2;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        TurnController.DecayUnmannedDefenses(fixture.Planet.Regions[0]);

        // the occupier strips a quarter level per stat per turn, floored at zero
        Assert.Equal(3.75, remnant.Entrenchment);
        Assert.Equal(0.75, remnant.ListeningPost);
        Assert.Equal(0.0, remnant.AntiAir);
    }

    [Fact]
    public void HiddenRemnant_DefensesHoldWithNoOccupierPresent()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.IsPublic = false;
        remnant.Garrison = 0;
        remnant.Entrenchment = 4;

        TurnController.DecayUnmannedDefenses(fixture.Planet.Regions[0]);

        Assert.Equal(4.0, remnant.Entrenchment);
    }

    [Fact]
    public void PublicDefender_DefensesDoNotDecay()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction defender = fixture.DefaultRegionFaction(0);
        defender.Garrison = 1_000; // still fighting for its region — its works stay manned
        defender.Entrenchment = 4;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        TurnController.DecayUnmannedDefenses(fixture.Planet.Regions[0]);

        Assert.Equal(4.0, defender.Entrenchment);
    }

    [Fact]
    public void ProcessTurn_KeepsZeroPopulationPlayerFootholdWithLandedSquad()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        RegionFaction playerFoothold = AddPlayerRegionFaction(fixture, region);
        playerFoothold.LandedSquads.Add(new Squad("Test Squad", null, null));

        fixture.ProcessTurn();

        Assert.Same(playerFoothold, region.RegionFactionMap[fixture.Sector.PlayerForce.Faction.Id]);
    }

    [Fact]
    public void ProcessTurn_RemovesEmptyPlayerFootholdWithoutDefenses()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        AddPlayerRegionFaction(fixture, region);

        fixture.ProcessTurn();

        Assert.False(region.RegionFactionMap.ContainsKey(fixture.Sector.PlayerForce.Faction.Id));
    }

    [Fact]
    public void ProcessTurn_RemovesAbandonedPlayerFootholdAfterDefensesDecayToZero()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Region region = fixture.Planet.Regions[0];
        RegionFaction playerFoothold = AddPlayerRegionFaction(fixture, region);
        playerFoothold.IsPublic = false;
        playerFoothold.Entrenchment = 0.25;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100);

        fixture.ProcessTurn();

        Assert.False(region.RegionFactionMap.ContainsKey(fixture.Sector.PlayerForce.Faction.Id));
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

    private static RegionFaction AddPlayerRegionFaction(SectorSimulationFixture fixture, Region region)
    {
        Faction playerFaction = fixture.Sector.PlayerForce.Faction;
        PlanetFaction playerPlanetFaction = new(playerFaction) { IsPublic = true };
        fixture.Planet.PlanetFactionMap[playerFaction.Id] = playerPlanetFaction;
        RegionFaction playerRegionFaction = new(playerPlanetFaction, region)
        {
            IsPublic = true,
            Organization = 100
        };
        region.RegionFactionMap[playerFaction.Id] = playerRegionFaction;
        return playerRegionFaction;
    }
}
