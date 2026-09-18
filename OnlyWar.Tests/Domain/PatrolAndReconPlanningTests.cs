using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Operations.Missions;
using OnlyWar.Campaign.Strategy;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Two AI planning decisions that were previously constants, both from
// OnlyWar_TDD.md §6.4: how much of a region's spare force screens it (Q4) and
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

        double fraction = FactionReconPatrolPlanner.CalculatePatrolFraction(faction, planet, State(rf));

        Assert.Equal(FactionReconPatrolPlanner.PolicingPatrolFraction, fraction);
        Assert.True(fraction > 0.0, "a quiet world must not be free to cross");
    }

    // Policing is not screening. The floor must stay well below the threatened-border tiers, or the
    // AI spends its offensive tempo garrisoning against nobody.
    [Fact]
    public void PatrolFraction_PolicingFloor_IsFarBelowTheThreatenedTiers()
    {
        Assert.True(
            FactionReconPatrolPlanner.PolicingPatrolFraction
                < FactionReconPatrolPlanner.PatrolForceFraction,
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
        rf.ListeningPost = FactionReconPatrolPlanner.WorthScreeningWorksLevel;

        double fraction = FactionReconPatrolPlanner.CalculatePatrolFraction(faction, planet, State(rf));

        Assert.Equal(FactionReconPatrolPlanner.PatrolForceFraction, fraction);
    }


    // ----- Recon aggression is doctrine, not awareness -----
    //
    // Four tests were removed here on 2026-09-17 with FactionReconPatrolPlanner.ChooseReconAggression,
    // which returned Cautious on unknown ground and grew bolder as a region became familiar. Their own
    // comments had noticed half the problem - "defaulting to Cautious had the AI permanently penalise
    // the intelligence check whose output is what its own garrison sizing depends on" - and resolved it
    // the wrong way round.
    //
    // Aggression is a difficulty delta on BOTH axes: MissionAggressionModifiers.EffectDifficulty is the
    // exact inverse of ExposureDifficulty, so caution is paid for in intelligence. The old ladder spent
    // most on caution exactly where information was scarcest, and it made the choice identically for
    // every faction - a WAAAGH crept about like a cult. Measured on Grist Nine, the Cautious step alone
    // cost roughly 40% of the weekly observation margin.
    //
    // What replaces it is a per-faction ReconAggression in the FactionDoctrine table, so the trade is
    // stated once per faction instead of derived from how much it already knows.

    [Fact]
    public void ReconAggression_DefaultsToNormalWhenNoDoctrineIsAuthored()
    {
        Assert.Equal(Aggression.Normal, ForceDoctrineWeights.Balanced.ReconAggression);
    }

    // The property the deleted ladder was really protecting, restated where it now lives: a faction
    // that accepts exposure learns more. Whatever the authored values become, a bolder doctrine must
    // never buy LESS intelligence than a timid one.
    [Fact]
    public void ReconAggression_BolderDoctrineNeverLearnsLess()
    {
        Aggression[] ascending =
        [
            Aggression.Avoid,
            Aggression.Cautious,
            Aggression.Normal,
            Aggression.Attritional,
            Aggression.Aggressive
        ];

        float previousDifficulty = float.MaxValue;
        foreach (Aggression aggression in ascending)
        {
            float difficulty = MissionAggressionModifiers.EffectDifficulty(aggression);
            Assert.True(
                difficulty < previousDifficulty,
                $"{aggression} faced observation difficulty {difficulty}, no easier than the timider step");
            previousDifficulty = difficulty;
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
        scoutPlanetFaction.SetRegionAwareness(region, intel);

        return (scout, region);
    }

    private static RegionForceState State(RegionFaction rf) =>
        new(rf, requiredDefensiveBattleValue: 0, assignedDefensiveBattleValue: 0,
            spareTroops: 10_000, defensiveShortfall: 0);

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
            behavior: FactionBehavior.None,
            GrowthType.Conversion,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
}
