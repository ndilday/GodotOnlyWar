using System;
using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using Xunit;

namespace OnlyWar.Tests.Missions;

public class ReconDetectionTests
{
    [Fact]
    public void CalculateStealthDifficulty_ZeroGarrisonHorde_ProducesAFinitePositivePresenceTerm()
    {
        // A PopulationIsMilitary horde carries no Garrison, so the old log10(Garrison) term was
        // log10(0) = -infinity and the region was trivially infiltrable. Deployed strength
        // (Population x Organization/100) now drives the ambient-presence term, which is finite and
        // positive - though capped, because a crowd that is not searching only helps so much.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Swarm"), population: 100_000, organization: 100, intel: 0f);

        StealthDifficultyTerms terms = MissionStealthDifficulty.Calculate(
            region, intruderHeadcount: 5, intruder: null);

        Assert.Equal(1, terms.EnemyCount);
        Assert.True(terms.AmbientMod > 0f);
        Assert.False(float.IsNegativeInfinity(terms.AmbientMod));
        Assert.True(float.IsFinite(terms.Total));
        Assert.True(terms.Total > 0f);
    }

    [Fact]
    public void SelectSpotter_IsProportionalToWatchScore()
    {
        // Two enemy factions of identical size, one with three times the region-intel of the other.
        // The spotter roll is weighted by WatchScore, the same per-faction number the difficulty was
        // built from, so the extra surveillance - not the intel value in isolation - is what shifts
        // the odds: 0.5*3 + 1.5 against 0.5*1 + 1.5, i.e. 3.0 against 2.0.
        Region region = CreateRegion();
        RegionFaction watcher = AddEnemy(region, CreateFaction(20, "Watchers"),
            population: 1_000, organization: 100, intel: 3f);
        RegionFaction sleeper = AddEnemy(region, CreateFaction(21, "Sleepers"),
            population: 1_000, organization: 100, intel: 1f);

        (int watcherHits, int sleeperHits) = TallySpotters(
            region, watcher, sleeper, 4000, new SeededRNG(1234));

        double watcherShare = watcherHits / (double)(watcherHits + sleeperHits);
        Assert.True(watcherHits > sleeperHits);
        // Expected 0.60; allow a generous band for RNG variance.
        Assert.InRange(watcherShare, 0.55, 0.65);
    }

    [Fact]
    public void SelectSpotter_WithNoSurveillance_IsWeightedByAmbientPresence()
    {
        // No faction has any awareness and none is out searching, so all that is left to weight by is
        // how thick on the ground each one is. Both holdings are kept under the ambient cap on
        // purpose: above it the two would be equally likely spotters, which is the intended
        // consequence of the cap (a dormant crowd of 10^7 is no better at noticing an intruder than a
        // dormant crowd of 10^4) and would make this test measure nothing.
        Region region = CreateRegion();
        RegionFaction strong = AddEnemy(region, CreateFaction(20, "Legion"),
            population: 900, organization: 100, intel: 0f);
        RegionFaction weak = AddEnemy(region, CreateFaction(21, "Cell"),
            population: 30, organization: 100, intel: 0f);

        float strongScore = MissionStealthDifficulty.CalculateWatchScore(strong);
        float weakScore = MissionStealthDifficulty.CalculateWatchScore(weak);
        Assert.True(strongScore < MissionStealthDifficulty.AmbientSearchCap);

        (int strongHits, int weakHits) = TallySpotters(
            region, strong, weak, 4000, new SeededRNG(4321));

        double strongShare = strongHits / (double)(strongHits + weakHits);
        double expectedShare = strongScore / (strongScore + weakScore);
        Assert.True(strongHits > weakHits);
        Assert.InRange(strongShare, expectedShare - 0.03, expectedShare + 0.03);
    }

    [Fact]
    public void SelectSpotter_ZeroIntelAndZeroDeployedStrength_ReturnsAnEnemyWithoutDividingByZero()
    {
        // Present factions (they have population) but neither eyes (intel 0) nor deployable troops
        // (organization 0 => deployed strength 0), so every WatchScore is 0 and the weighting total
        // is 0; SelectSpotter must still return a present enemy rather than throw or divide by zero.
        Region region = CreateRegion();
        AddEnemy(region, CreateFaction(20, "Alpha"), population: 1_000, organization: 0, intel: 0f);
        AddEnemy(region, CreateFaction(21, "Beta"), population: 1_000, organization: 0, intel: 0f);

        RegionFaction spotter = region.SelectSpotter(new SeededRNG(7));

        Assert.NotNull(spotter);
        Assert.Contains(spotter, region.RegionFactionMap.Values);
    }

    private static (int first, int second) TallySpotters(
        Region region,
        RegionFaction first,
        RegionFaction second,
        int iterations,
        IRNG random)
    {
        int firstHits = 0;
        int secondHits = 0;
        for (int i = 0; i < iterations; i++)
        {
            RegionFaction spotter = region.SelectSpotter(random);
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
