using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Two AI planning decisions that were previously constants, both from
// Design/Active/DailyMissionResolution.md §8: how much of a region's spare force screens it (Q4) and
// how boldly the faction scouts (Q5).
public class PatrolAndReconPlanningTests
{
    // ----- Q4: the policing floor -----

    // The hole this closes: CalculatePatrolFraction returned a hard 0 without a public enemy anywhere
    // on the world, and interception now requires a screen at parity, so a covert campaign - exactly
    // the phase before the Chapter reveals itself - met no opposition in any region of the planet.
    // A faction with a garrison is doing basic policing whether or not it has a declared enemy.
    [Fact]
    public void PatrolFraction_NoPublicEnemyOnPlanet_StillPolices()
    {
        Faction faction = CreateFaction(2, "Test Cult");
        Planet planet = CreatePlanet();
        RegionFaction rf = AddRegionFaction(planet, planet.Regions[0], faction, population: 10_000);

        double fraction = new FactionStrategyController()
            .CalculatePatrolFraction(faction, planet, State(rf));

        Assert.Equal(FactionStrategyController.PolicingPatrolFraction, fraction);
        Assert.True(fraction > 0.0, "a quiet world must not be free to cross");
    }

    // Policing is not screening. The floor must stay well below the threatened-border tiers, or the
    // AI spends its offensive tempo garrisoning against nobody.
    [Fact]
    public void PatrolFraction_PolicingFloor_IsFarBelowTheThreatenedTiers()
    {
        Assert.True(
            FactionStrategyController.PolicingPatrolFraction
                < FactionStrategyController.PatrolForceFraction,
            "the policing floor must be lighter than a works-based screen");
    }

    // Works are worth watching whether or not anyone has declared themselves: a region with something
    // on it a saboteur would come for gets the full screening fraction even on a quiet world.
    [Fact]
    public void PatrolFraction_NoPublicEnemy_ButRegionHasWorks_ScreensProperly()
    {
        Faction faction = CreateFaction(2, "Test Cult");
        Planet planet = CreatePlanet();
        RegionFaction rf = AddRegionFaction(planet, planet.Regions[0], faction, population: 10_000);
        rf.ListeningPost = FactionStrategyController.WorthScreeningWorksLevel;

        double fraction = new FactionStrategyController()
            .CalculatePatrolFraction(faction, planet, State(rf));

        Assert.Equal(FactionStrategyController.PatrolForceFraction, fraction);
    }

    // ----- Q5: recon aggression as a decision -----

    // Unfamiliar ground: go quietly, accept learning less, bring the scouts home. This is the case the
    // old flat default got right by accident.
    [Fact]
    public void ReconAggression_UnknownRegion_IsCautious()
    {
        (Faction faction, Region region) = ReconTarget(intel: 0f);

        Assert.Equal(
            Aggression.Cautious,
            FactionStrategyController.ChooseReconAggression(faction, region));
    }

    // Ground it has scouted before: press in. This is the half the old default got wrong - defaulting
    // to Cautious had the AI permanently penalise the intelligence check whose output is what its own
    // garrison sizing depends on.
    [Fact]
    public void ReconAggression_WellKnownRegion_PressesIn()
    {
        (Faction faction, Region region) = ReconTarget(
            intel: FactionStrategyController.GarrisonFullSightIntel);

        Assert.Equal(
            Aggression.Attritional,
            FactionStrategyController.ChooseReconAggression(faction, region));
    }

    // The band between the two, so the progression is graded rather than a single cliff.
    [Fact]
    public void ReconAggression_PartiallyKnownRegion_IsNormal()
    {
        (Faction faction, Region region) = ReconTarget(
            intel: FactionStrategyController.UnfamiliarGroundIntel);

        Assert.Equal(
            Aggression.Normal,
            FactionStrategyController.ChooseReconAggression(faction, region));
    }

    // The property that matters more than any individual band: knowing more never makes the AI scout
    // more timidly. A future retune of the thresholds must not invert this.
    [Fact]
    public void ReconAggression_NeverBecomesMoreTimidAsIntelRises()
    {
        Aggression previous = Aggression.Avoid;
        foreach (float intel in new[] { 0f, 0.5f, 1f, 1.5f, 2f, 4f })
        {
            (Faction faction, Region region) = ReconTarget(intel);
            Aggression chosen = FactionStrategyController.ChooseReconAggression(faction, region);
            Assert.True(
                chosen >= previous,
                $"intel {intel} chose {chosen}, timider than the {previous} chosen at less intel");
            previous = chosen;
        }
    }

    // --- fixtures ---

    // A region held by an enemy, with the scouting faction holding `intel` about it. Region intel is
    // per-PlanetFaction, so the scouting faction needs a PlanetFaction on the same planet to hold it.
    private static (Faction scout, Region target) ReconTarget(float intel)
    {
        Faction scout = CreateFaction(2, "Test Cult");
        Faction defender = CreateFaction(3, "Test Defender");
        Planet planet = CreatePlanet();
        Region region = planet.Regions[0];
        AddRegionFaction(planet, region, defender, population: 10_000);

        PlanetFaction scoutPlanetFaction = new(scout) { IsPublic = true };
        planet.PlanetFactionMap[scout.Id] = scoutPlanetFaction;
        scoutPlanetFaction.SetRegionIntel(region, intel);

        return (scout, region);
    }

    private static FactionStrategyController.RegionForceState State(RegionFaction rf) =>
        new(rf, requiredDefensiveBattleValue: 0, spareTroops: 10_000, defensiveShortfall: 0);

    private static RegionFaction AddRegionFaction(
        Planet planet, Region region, Faction faction, long population)
    {
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        planet.PlanetFactionMap[faction.Id] = planetFaction;
        RegionFaction regionFaction = new(planetFaction, region)
        {
            Population = population,
            Organization = 100,
            IsPublic = true
        };
        region.RegionFactionMap[faction.Id] = regionFaction;
        return regionFaction;
    }

    private static Planet CreatePlanet()
    {
        Planet planet = new(1, "Policing Test World", new Coordinate(1, 1), 1, null, 1, 0);
        for (int i = 0; i < planet.Regions.Length; i++)
        {
            planet.Regions[i] = new Region(
                i, planet, 0, $"Region {i}",
                RegionExtensions.GetCoordinatesFromRegionNumber(i), 0);
        }
        return planet;
    }

    private static Faction CreateFaction(int id, string name) =>
        new(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.Conversion,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
}
