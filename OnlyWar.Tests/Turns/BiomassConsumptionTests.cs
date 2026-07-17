using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Coverage for the Tyranid biomass model (PRD §4.24): Predate (headcount) and Consume (carrying
// capacity) both feed the swarm at half efficiency, kills distribute proportionally across prey,
// consumers are excluded from the crowding that limits ordinary growth, and stripped land slowly
// heals toward its natural ceiling. The biomass methods are exercised directly (not through a full
// ProcessTurn) so the conservation arithmetic can be asserted exactly.
public class BiomassConsumptionTests
{
    [Fact]
    public void ResolveBiomassConsumption_FeedsHalfOfAllBiomassEatenToTheSwarm()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 1_000_000;
        region.MaximumCarryingCapacity = 1_000_000;
        RegionFaction prey = fixture.DefaultRegionFaction(0);
        prey.Population = 500_000;
        // A large force, so the equilibrium drives both pools past their crossover and exercises
        // predation and consumption together rather than settling into one.
        RegionFaction tyranids = fixture.AddConsumptionFaction(0, population: 2_000_000, organization: 100);

        long preyBefore = prey.Population;
        long capacityBefore = region.CarryingCapacity;
        long tyranidsBefore = tyranids.Population;

        TurnController.ResolveBiomassConsumption(region);

        long killed = preyBefore - prey.Population;
        long stripped = capacityBefore - region.CarryingCapacity;
        long gained = tyranids.Population - tyranidsBefore;

        // A rich region draws force into both actions, and half the total biomass eaten (kills +
        // capacity stripped) becomes new Tyranid population.
        Assert.True(killed > 0, "predation should kill some prey");
        Assert.True(stripped > 0, "consumption should strip some capacity");
        Assert.Equal((killed + stripped) / 2, gained);
    }

    [Fact]
    public void ResolveBiomassConsumption_WithNoPrey_OnlyStripsCapacity()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 1_000_000;
        region.MaximumCarryingCapacity = 1_000_000;
        // Empty the region of prey so all force must go to consuming the land.
        fixture.DefaultRegionFaction(0).Population = 0;
        RegionFaction tyranids = fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);
        long tyranidsBefore = tyranids.Population;

        TurnController.ResolveBiomassConsumption(region);

        long stripped = 1_000_000 - region.CarryingCapacity;
        Assert.Equal(0, fixture.DefaultRegionFaction(0).Population);
        Assert.True(stripped > 0, "capacity should be consumed when there is no prey");
        Assert.Equal(stripped / 2, tyranids.Population - tyranidsBefore);
    }

    [Fact]
    public void ResolveBiomassConsumption_WithNoBiomass_OnlyPredates()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        // A region whose land is already stripped bare: only headcount is left to eat.
        region.CarryingCapacity = 0;
        region.MaximumCarryingCapacity = 0;
        RegionFaction prey = fixture.DefaultRegionFaction(0);
        prey.Population = 500_000;
        RegionFaction tyranids = fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);
        long tyranidsBefore = tyranids.Population;

        TurnController.ResolveBiomassConsumption(region);

        long killed = 500_000 - prey.Population;
        Assert.Equal(0, region.CarryingCapacity);
        Assert.True(killed > 0, "prey should be predated when there is no land to consume");
        Assert.Equal(killed / 2, tyranids.Population - tyranidsBefore);
    }

    [Fact]
    public void ResolveBiomassConsumption_DistributesKillsAcrossPreyByPopulationShare()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 0; // force all effort into predation for a clean split check
        region.MaximumCarryingCapacity = 0;
        RegionFaction big = fixture.DefaultRegionFaction(0);
        big.Population = 900_000;
        RegionFaction small = fixture.AddHiddenFaction(0, OnlyWar.Models.GrowthType.Logistic, population: 100_000);
        fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);

        TurnController.ResolveBiomassConsumption(region);

        long bigKilled = 900_000 - big.Population;
        long smallKilled = 100_000 - small.Population;
        // The 90/10 prey split should be culled in roughly 9:1 proportion.
        Assert.True(bigKilled > 0 && smallKilled > 0);
        Assert.Equal(9.0, bigKilled / (double)smallKilled, precision: 1);
    }

    [Fact]
    public void NonConsumerPopulation_ExcludesBiomassConsumers()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        fixture.DefaultRegionFaction(0).Population = 20_000;
        fixture.AddConsumptionFaction(0, population: 1_000_000, organization: 100);

        // The Tyranid headcount counts toward the raw population but not toward the crowding-
        // relevant NonConsumerPopulation, so it cannot starve the region's ordinary inhabitants.
        Assert.Equal(1_020_000, region.Population);
        Assert.Equal(20_000, region.NonConsumerPopulation);
    }

    [Fact]
    public void RecoverCarryingCapacity_HealsFractionOfGapTowardMaximum()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.MaximumCarryingCapacity = 1_000_000;
        region.CarryingCapacity = 500_000;

        TurnController.RecoverCarryingCapacity(region);

        // 1% of the 500,000 gap.
        Assert.Equal(505_000, region.CarryingCapacity);
    }

    [Fact]
    public void RecoverCarryingCapacity_AtMaximum_IsNoOp()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.MaximumCarryingCapacity = 1_000_000;
        region.CarryingCapacity = 1_000_000;

        TurnController.RecoverCarryingCapacity(region);

        Assert.Equal(1_000_000, region.CarryingCapacity);
    }

    [Fact]
    public void RecoverCarryingCapacity_TinyGap_StillHealsByAtLeastOne()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.MaximumCarryingCapacity = 100;
        region.CarryingCapacity = 99; // 1% of a gap of 1 rounds to zero; must still heal

        TurnController.RecoverCarryingCapacity(region);

        Assert.Equal(100, region.CarryingCapacity);
    }

    // The land must not regrow while a public swarm is still grazing it, or the swarm would have a
    // renewable food source and never starve on its finite stranded-biomass budget.
    [Fact]
    public void RecoverCarryingCapacity_DoesNotHealWhilePublicSwarmPresent()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.MaximumCarryingCapacity = 1_000_000;
        region.CarryingCapacity = 500_000;
        fixture.AddConsumptionFaction(0, population: 50_000, organization: 100); // swarm grazing

        TurnController.RecoverCarryingCapacity(region);

        Assert.Equal(500_000, region.CarryingCapacity);
    }

    // Once the swarm is gone from the region, the land heals again as before.
    [Fact]
    public void RecoverCarryingCapacity_HealsOnceSwarmIsGone()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.MaximumCarryingCapacity = 1_000_000;
        region.CarryingCapacity = 500_000;
        // no Consumption faction present

        TurnController.RecoverCarryingCapacity(region);

        Assert.Equal(505_000, region.CarryingCapacity);
    }

    // --- Forced expansion (PRD §4.24 Tyranid Troop AI, step 2) ---

    [Fact]
    public void ResolveTyranidExpansion_StrippedRegion_PushesForceToARicherNeighbor()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region home = fixture.Planet.Regions[0];
        // Home is bare: no land and no prey left to eat.
        home.CarryingCapacity = 0;
        home.MaximumCarryingCapacity = 1_000_000;
        fixture.DefaultRegionFaction(0).Population = 0;

        Region neighbor = home.GetAdjacentRegions().First();
        neighbor.CarryingCapacity = 1_000_000; // fresh biomass next door
        neighbor.MaximumCarryingCapacity = 1_000_000;

        RegionFaction swarm = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        int swarmFactionId = swarm.PlanetFaction.Faction.Id;
        long swarmBefore = swarm.Population;

        TurnController.ResolveTyranidExpansion(fixture.Planet);

        long moved = swarmBefore - swarm.Population;
        Assert.True(moved > 0, "a stripped swarm should spread toward fresh biomass");
        Assert.True(neighbor.RegionFactionMap.TryGetValue(swarmFactionId, out RegionFaction spread)
                    && spread.Population == moved,
            "the force that leaves establishes in the richer neighbor");
    }

    [Fact]
    public void ResolveTyranidExpansion_RichRegion_KeepsTheSwarmHomeToGorge()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region home = fixture.Planet.Regions[0];
        // Home is untouched and richer than its (poorer, default) neighbors.
        home.CarryingCapacity = 1_000_000;
        home.MaximumCarryingCapacity = 1_000_000;
        RegionFaction swarm = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        long swarmBefore = swarm.Population;

        TurnController.ResolveTyranidExpansion(fixture.Planet);

        Assert.Equal(swarmBefore, swarm.Population);
    }
}
