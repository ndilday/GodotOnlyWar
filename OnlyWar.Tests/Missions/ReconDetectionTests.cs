using System;
using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using Xunit;

namespace OnlyWar.Tests.Missions;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class ReconDetectionTests
{
    [Fact]
    public void CalculateStealthDifficulty_ZeroGarrisonHorde_ProducesFinitePositiveTroopTerm()
    {
        // A PopulationIsMilitary horde carries no Garrison, so the old log10(Garrison) term was
        // log10(0) = -infinity and the region was trivially infiltrable. Deployed strength
        // (Population x Organization/100) now drives a finite, positive troop term.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Swarm"), population: 100_000, organization: 100, intel: 0f);

        float difficulty = ReconStealthMissionStep.CalculateStealthDifficulty(
            region, scoutHeadcount: 5, scout: null,
            out _, out _, out float troopMod, out _, out int enemyCount);

        Assert.Equal(1, enemyCount);
        Assert.True(troopMod > 0f);
        Assert.False(float.IsNegativeInfinity(troopMod));
        Assert.True(float.IsFinite(difficulty));
        Assert.True(difficulty > 0f);
    }

    [Fact]
    public void SelectSpotter_IsProportionalToRegionIntel()
    {
        // Two enemy factions, one with three times the region-intel of the other. Over many rolls the
        // higher-intel faction should be picked ~3x as often (weight = intel, strength ignored).
        Region region = CreateRegion();
        RegionFaction watcher = AddEnemy(region, CreateFaction(20, "Watchers"),
            population: 1_000, organization: 100, intel: 3f);
        RegionFaction sleeper = AddEnemy(region, CreateFaction(21, "Sleepers"),
            population: 1_000, organization: 100, intel: 1f);

        RNG.Reset(1234);
        (int watcherHits, int sleeperHits) = TallySpotters(region, watcher, sleeper, 4000);

        double watcherShare = watcherHits / (double)(watcherHits + sleeperHits);
        Assert.True(watcherHits > sleeperHits);
        // Expected 0.75; allow a generous band for RNG variance.
        Assert.InRange(watcherShare, 0.70, 0.80);
    }

    [Fact]
    public void SelectSpotter_ZeroIntel_FallsBackToDeployedStrength()
    {
        // No faction has any awareness (intel 0), so the intruder walks into a patrol: the spotter is
        // drawn in proportion to deployed strength (here 3:1) instead, without dividing by zero.
        Region region = CreateRegion();
        RegionFaction strong = AddEnemy(region, CreateFaction(20, "Legion"),
            population: 3_000, organization: 100, intel: 0f);
        RegionFaction weak = AddEnemy(region, CreateFaction(21, "Cell"),
            population: 1_000, organization: 100, intel: 0f);

        RNG.Reset(4321);
        (int strongHits, int weakHits) = TallySpotters(region, strong, weak, 4000);

        double strongShare = strongHits / (double)(strongHits + weakHits);
        Assert.True(strongHits > weakHits);
        Assert.InRange(strongShare, 0.70, 0.80);
    }

    [Fact]
    public void SelectSpotter_ZeroIntelAndZeroDeployedStrength_ReturnsAnEnemyWithoutDividingByZero()
    {
        // Present factions (they have population) but neither eyes (intel 0) nor deployable troops
        // (organization 0 => deployed strength 0). Both weighting totals are zero; SelectSpotter must
        // still return a present enemy rather than throw or divide by zero.
        Region region = CreateRegion();
        RegionFaction a = AddEnemy(region, CreateFaction(20, "Alpha"),
            population: 1_000, organization: 0, intel: 0f);
        AddEnemy(region, CreateFaction(21, "Beta"), population: 1_000, organization: 0, intel: 0f);

        RNG.Reset(7);
        RegionFaction spotter = region.SelectSpotter(StaticRNG.Instance);

        Assert.NotNull(spotter);
        Assert.Contains(spotter, region.RegionFactionMap.Values);
    }

    private static (int first, int second) TallySpotters(
        Region region, RegionFaction first, RegionFaction second, int iterations)
    {
        int firstHits = 0;
        int secondHits = 0;
        for (int i = 0; i < iterations; i++)
        {
            RegionFaction spotter = region.SelectSpotter(StaticRNG.Instance);
            if (spotter == first) firstHits++;
            else if (spotter == second) secondHits++;
        }
        return (firstHits, secondHits);
    }

    private static RegionFaction AddEnemy(
        Region region, Faction faction, long population, int organization, float intel)
    {
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        RegionFaction regionFaction = new(planetFaction, region)
        {
            Population = population,
            Organization = organization,
            IsPublic = true
        };
        planetFaction.SetRegionIntel(region, intel);
        region.RegionFactionMap[faction.Id] = regionFaction;
        return regionFaction;
    }

    private static Region CreateRegion()
    {
        // SelectSpotter and the difficulty aggregation touch only the region's faction map and each
        // faction's own intel, so a lightweight region with no owning planet is sufficient here.
        return new Region(0, null, 0, "Test Region", new RegionCoordinate(0, 0), 0);
    }

    private static Faction CreateFaction(int id, string name)
    {
        // A non-player, non-default faction defaults to PopulationIsMilitary, so MilitaryStrength is
        // its Population (the horde case exercised by these tests).
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayerFaction: false,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.Conversion,
            new Dictionary<int, Species>(),
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
