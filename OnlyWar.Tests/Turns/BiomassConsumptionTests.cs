using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Intelligence;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

// Coverage for the Consumption biomass model (PRD §4.24): Predate (headcount) and Consume (carrying
// capacity) both feed the consumer at half efficiency, kills distribute proportionally across prey,
// consumers are excluded from the crowding that limits ordinary growth, and stripped land slowly
// heals toward its natural ceiling. The biomass methods are exercised directly (not through a full
// ProcessTurn) so the conservation arithmetic can be asserted exactly.
public class BiomassConsumptionTests
{
    [Fact]
    public void ResolveFeeding_FeedsHalfOfAllBiomassEatenToTheConsumer()
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

        ConsumptionTurnProcessor.ResolveFeeding(region);

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
    public void ResolveFeeding_WithNoPrey_OnlyStripsCapacity()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 1_000_000;
        region.MaximumCarryingCapacity = 1_000_000;
        // Empty the region of prey so all force must go to consuming the land.
        fixture.DefaultRegionFaction(0).Population = 0;
        RegionFaction tyranids = fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);
        long tyranidsBefore = tyranids.Population;

        ConsumptionTurnProcessor.ResolveFeeding(region);

        long stripped = 1_000_000 - region.CarryingCapacity;
        Assert.Equal(0, fixture.DefaultRegionFaction(0).Population);
        Assert.True(stripped > 0, "capacity should be consumed when there is no prey");
        Assert.Equal(stripped / 2, tyranids.Population - tyranidsBefore);
    }

    [Fact]
    public void ResolveFeeding_WithNoBiomass_OnlyPredates()
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

        ConsumptionTurnProcessor.ResolveFeeding(region);

        long killed = 500_000 - prey.Population;
        Assert.Equal(0, region.CarryingCapacity);
        Assert.True(killed > 0, "prey should be predated when there is no land to consume");
        Assert.Equal(killed / 2, tyranids.Population - tyranidsBefore);
    }

    [Fact]
    public void ResolveFeeding_DistributesKillsAcrossPreyByPopulationShare()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 0; // force all effort into predation for a clean split check
        region.MaximumCarryingCapacity = 0;
        RegionFaction big = fixture.DefaultRegionFaction(0);
        big.Population = 900_000;
        RegionFaction small = fixture.AddHiddenFaction(0, OnlyWar.Domain.GrowthType.Logistic, population: 100_000);
        fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);

        ConsumptionTurnProcessor.ResolveFeeding(region);

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

        ConsumptionTurnProcessor.RecoverCarryingCapacity(region);

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

        ConsumptionTurnProcessor.RecoverCarryingCapacity(region);

        Assert.Equal(1_000_000, region.CarryingCapacity);
    }

    [Fact]
    public void RecoverCarryingCapacity_TinyGap_StillHealsByAtLeastOne()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.MaximumCarryingCapacity = 100;
        region.CarryingCapacity = 99; // 1% of a gap of 1 rounds to zero; must still heal

        ConsumptionTurnProcessor.RecoverCarryingCapacity(region);

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

        ConsumptionTurnProcessor.RecoverCarryingCapacity(region);

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

        ConsumptionTurnProcessor.RecoverCarryingCapacity(region);

        Assert.Equal(505_000, region.CarryingCapacity);
    }

    // --- Feeding as a budgeted mission (Design/Reference/ConsumptionFeedingAsMission.md) ---

    // The whole point of the change: feeding eats with the force it was GIVEN, not with the swarm's
    // whole deployed strength re-derived from population. A swarm that has committed most of itself
    // to defence, offensives and patrols must feed proportionately less.
    [Fact]
    public void ResolveFeeding_WithACommittedBudget_EatsOnlyWithThatForce()
    {
        long StripWith(double troops)
        {
            SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
            Region region = fixture.Planet.Regions[0];
            region.CarryingCapacity = 1_000_000;
            region.MaximumCarryingCapacity = 1_000_000;
            fixture.DefaultRegionFaction(0).Population = 0;
            RegionFaction swarm = fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);

            ConsumptionTurnProcessor.ResolveFeeding(swarm, troops);
            return 1_000_000 - region.CarryingCapacity;
        }

        long wholeSwarm = StripWith(200_000);
        long quarterCommitted = StripWith(50_000);

        Assert.True(quarterCommitted > 0, "a committed quarter should still feed");
        Assert.True(quarterCommitted < wholeSwarm,
            "committing a quarter of the swarm must strip less than committing all of it");
    }

    // A feed order is squad-less and resolves instantly, on the ConstructionMission precedent: the
    // committed battle value on the mission is what reaches the allocator.
    [Fact]
    public void ProcessFeedOrders_ResolvesTheCommittedStrategicValueWithoutSquads()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 1_000_000;
        region.MaximumCarryingCapacity = 1_000_000;
        fixture.DefaultRegionFaction(0).Population = 0;
        RegionFaction swarm = fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);
        long swarmBefore = swarm.Population;

        Order order = new Order(
            new List<Squad>(), true, false, Aggression.Cautious, new FeedMission(50_000, swarm));
        MissionTurnProcessor.ProcessFeedOrders(new[] { order });

        long stripped = 1_000_000 - region.CarryingCapacity;
        Assert.True(stripped > 0, "the order should feed the swarm");
        Assert.Equal(stripped / 2, swarm.Population - swarmBefore);
    }

    // Planning only ever sees IsPublic region-factions, so a hidden swarm has no planner to allocate
    // its force. The planet update keeps feeding it at full strength rather than letting it silently
    // starve - and must leave a public swarm alone, or that one would eat twice.
    [Fact]
    public void ResolveHiddenFeeding_FeedsAHiddenConsumerAndSkipsAPublicOne()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        region.CarryingCapacity = 1_000_000;
        region.MaximumCarryingCapacity = 1_000_000;
        fixture.DefaultRegionFaction(0).Population = 0;

        RegionFaction publicSwarm = fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);
        RegionFaction hiddenSwarm = fixture.AddConsumptionFaction(0, population: 200_000, organization: 100);
        hiddenSwarm.IsPublic = false;
        long publicBefore = publicSwarm.Population;
        long hiddenBefore = hiddenSwarm.Population;

        ConsumptionTurnProcessor.ResolveHiddenFeeding(region);

        Assert.Equal(publicBefore, publicSwarm.Population);
        Assert.True(hiddenSwarm.Population > hiddenBefore,
            "a hidden swarm has no planner to budget it and must keep feeding");
    }

    // Same split for spreading: the planner moves a public swarm out of its spare troops, so the
    // planet update must only push the swarms it never saw.
    [Fact]
    public void ResolveHiddenExpansion_MovesAHiddenConsumerAndLeavesAPublicOne()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region home = fixture.Planet.Regions[0];
        home.CarryingCapacity = 0;
        home.MaximumCarryingCapacity = 1_000_000;
        fixture.DefaultRegionFaction(0).Population = 0;

        Region neighbor = home.GetAdjacentRegions().First();
        neighbor.CarryingCapacity = 1_000_000;
        neighbor.MaximumCarryingCapacity = 1_000_000;

        RegionFaction publicSwarm = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        RegionFaction hiddenSwarm = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        hiddenSwarm.IsPublic = false;
        long publicBefore = publicSwarm.Population;
        long hiddenBefore = hiddenSwarm.Population;

        ConsumptionTurnProcessor.ResolveHiddenExpansion(fixture.Planet);

        Assert.Equal(publicBefore, publicSwarm.Population);
        Assert.True(hiddenSwarm.Population < hiddenBefore,
            "a hidden swarm on stripped ground should still spread toward fresh biomass");
    }

    // --- Forced expansion (PRD §4.24 Tyranid Troop AI, step 2) ---

    [Fact]
    public void ResolveConsumptionExpansion_StrippedRegion_PushesForceToARicherNeighbor()
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

        ConsumptionTurnProcessor.ResolveExpansion(fixture.Planet);

        long moved = swarmBefore - swarm.Population;
        Assert.True(moved > 0, "a stripped swarm should spread toward fresh biomass");
        Assert.True(neighbor.RegionFactionMap.TryGetValue(swarmFactionId, out RegionFaction spread)
                    && spread.Population == moved,
            "the force that leaves establishes in the richer neighbor");
    }

    [Fact]
    public void ResolveConsumptionExpansion_RichRegion_KeepsTheConsumerHomeToFeed()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region home = fixture.Planet.Regions[0];
        // Home is untouched and richer than its (poorer, default) neighbors.
        home.CarryingCapacity = 1_000_000;
        home.MaximumCarryingCapacity = 1_000_000;
        RegionFaction swarm = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        long swarmBefore = swarm.Population;

        ConsumptionTurnProcessor.ResolveExpansion(fixture.Planet);

        Assert.Equal(swarmBefore, swarm.Population);
    }

    // --- Resistance in the expansion choice (PRD §4.24 Tyranid Troop AI; TDD §6.2 *Swarm operations*) ---
    //
    // Region 0 borders regions 1, 2 and 4. Home is stripped bare (depletion 1), so a swarm of 100,000
    // moves 50,000. Each default neighbour holds 20,000 prey on no land, so the tests raise the land of
    // the neighbours they compare and leave region 4 as the poor third choice.

    [Fact]
    public void PlanExpansion_EqualBiomass_PrefersResistanceNearerTheSwarmsOwnStrength()
    {
        SectorSimulationFixture fixture = CreateStrippedHome(out RegionFaction swarm);
        SetLand(fixture, 1, 1_000_000);
        SetLand(fixture, 2, 1_000_000);
        // Beta: 10,000 believed (r = 0.2, A = 1.1). Gamma: 30,000 PDF plus 20,000 of a second
        // faction (r = 1.0, A = 1.5), so resistance sums across factions.
        RegionFaction cult = fixture.AddHiddenFaction(2, GrowthType.Conversion, population: 0);
        Believe(fixture, swarm, 1, fixture.Default, 10_000);
        Believe(fixture, swarm, 2, fixture.Default, 30_000);
        Believe(fixture, swarm, 2, cult.PlanetFaction.Faction, 20_000);

        (Region destination, long movers) =
            ConsumptionTurnProcessor.PlanExpansion(swarm, swarm.GetDeployedStrength());

        Assert.Equal(50_000, movers);
        Assert.Same(fixture.Planet.Regions[2], destination);
    }

    [Fact]
    public void PlanExpansion_EqualBiomass_AvoidsResistanceAboveTwiceItsStrength()
    {
        SectorSimulationFixture fixture = CreateStrippedHome(out RegionFaction swarm);
        SetLand(fixture, 1, 1_000_000);
        SetLand(fixture, 2, 1_000_000);
        // Beta: 150,000 believed (r = 3, A = 1.5 / 9). Gamma: 20,000 (r = 0.4, A = 1.2).
        Believe(fixture, swarm, 1, fixture.Default, 150_000);
        Believe(fixture, swarm, 2, fixture.Default, 20_000);

        (Region destination, _) =
            ConsumptionTurnProcessor.PlanExpansion(swarm, swarm.GetDeployedStrength());

        Assert.Same(fixture.Planet.Regions[2], destination);
    }

    [Fact]
    public void PlanExpansion_ANeighbourWithNoBeliefIsValuedByBiomassAlone()
    {
        SectorSimulationFixture fixture = CreateStrippedHome(out RegionFaction swarm);
        // Beta is richer and unknown: a belief with no measured strength counts as none.
        SetLand(fixture, 1, 1_000_000);
        SetLand(fixture, 2, 900_000);
        swarm.PlanetFaction.SeedTargetBelief(
            fixture.Planet.Regions[1], fixture.Default,
            FactionIntelligenceRules.ConfirmedThreshold, null, null, 0);

        (Region destination, _) =
            ConsumptionTurnProcessor.PlanExpansion(swarm, swarm.GetDeployedStrength());

        Assert.Equal(0, ConsumptionTurnProcessor.BelievedResistance(
            swarm.PlanetFaction, fixture.Planet.Regions[1]));
        Assert.Same(fixture.Planet.Regions[1], destination);
    }

    [Fact]
    public void PlanExpansion_NeverChoosesANeighbourPoorerThanHome_WhateverItsResistance()
    {
        SectorSimulationFixture fixture = CreateStrippedHome(out RegionFaction swarm);
        fixture.Planet.Regions[0].CarryingCapacity = 500_000; // home biomass 500,000
        // Beta is poorer than home but holds the ideal fight (r = 1); Gamma is richer but hopeless.
        SetLand(fixture, 1, 400_000);
        SetLand(fixture, 2, 600_000);
        Believe(fixture, swarm, 1, fixture.Default, 37_500);
        Believe(fixture, swarm, 2, fixture.Default, 1_000_000);

        (Region destination, long movers) =
            ConsumptionTurnProcessor.PlanExpansion(swarm, swarm.GetDeployedStrength());

        Assert.Equal(37_500, movers); // depletion 0.75 × 100,000 × 0.5, unchanged by resistance
        Assert.Same(fixture.Planet.Regions[2], destination);
    }

    // One expansion rule for both callers: ForceTaskBuilder.AddConsumptionTasks calls PlanExpansion with the same deployed strength the
    // hidden pass uses, so checking the hidden pass against PlanExpansion covers both callers.
    [Fact]
    public void ResolveHiddenExpansion_MovesWherePlanExpansionChose()
    {
        SectorSimulationFixture fixture = CreateStrippedHome(out RegionFaction swarm);
        swarm.IsPublic = false;
        SetLand(fixture, 1, 1_000_000);
        SetLand(fixture, 2, 1_000_000);
        Believe(fixture, swarm, 1, fixture.Default, 10_000);
        Believe(fixture, swarm, 2, fixture.Default, 50_000);
        (Region planned, long movers) =
            ConsumptionTurnProcessor.PlanExpansion(swarm, swarm.GetDeployedStrength());

        ConsumptionTurnProcessor.ResolveHiddenExpansion(fixture.Planet);

        Assert.Same(fixture.Planet.Regions[2], planned);
        int swarmId = swarm.PlanetFaction.Faction.Id;
        Assert.True(planned.RegionFactionMap.TryGetValue(swarmId, out RegionFaction arrived));
        Assert.Equal(movers, arrived.Population);
        Assert.False(fixture.Planet.Regions[1].RegionFactionMap.ContainsKey(swarmId));
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 1.25)]
    [InlineData(1.0, 1.5)]
    [InlineData(2.0, 0.375)]
    public void ResistanceAttraction_RisesToTheSwarmsStrengthThenFalls(double ratio, double expected)
    {
        Assert.Equal(expected, ConsumptionTurnProcessor.ResistanceAttraction(ratio), 10);
    }

    private static SectorSimulationFixture CreateStrippedHome(out RegionFaction swarm)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region home = fixture.Planet.Regions[0];
        home.CarryingCapacity = 0;
        home.MaximumCarryingCapacity = 1_000_000;
        fixture.DefaultRegionFaction(0).Population = 0;
        swarm = fixture.AddConsumptionFaction(0, population: 100_000, organization: 100);
        return fixture;
    }

    private static void SetLand(SectorSimulationFixture fixture, int region, long capacity)
    {
        fixture.Planet.Regions[region].CarryingCapacity = capacity;
        fixture.Planet.Regions[region].MaximumCarryingCapacity = capacity;
    }

    private static void Believe(
        SectorSimulationFixture fixture, RegionFaction observer, int region, Faction target, long strength)
    {
        observer.PlanetFaction.SeedTargetBelief(
            fixture.Planet.Regions[region], target,
            FactionIntelligenceRules.ConfirmedThreshold, null, strength, 0);
    }
}
