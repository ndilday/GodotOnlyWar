using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

// Coverage for the "Promised World" generation override (Design/OpeningScenario.md §3, step 2):
// ScenarioBuilder.StampPromisedWorld, invoked from SectorBuilder.GenerateSector in place of the
// old FoundTakebackPlanet prototype. The stamp invariants and full per-seed determinism are the
// load-bearing guarantees for the opening.
public class ScenarioBuilderTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);

    public ScenarioBuilderTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
    }

    private Faction Tyranids => _data.Factions.Single(f => f.Name == "Tyranids");

    private static List<Region> TyranidRegions(Planet promised, Faction tyranids)
    {
        return promised.Regions
            .Where(r => r.RegionFactionMap.ContainsKey(tyranids.Id))
            .ToList();
    }

    [Fact]
    public void GenerateSector_StampsPromisedWorldScenario()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Promise Chapter");

        Assert.NotNull(sector.Scenario);
        Assert.Equal(ScenarioType.PromisedWorld, sector.Scenario.Type);
        Assert.Equal(ObjectiveState.Pending, sector.Scenario.State);
        Assert.False(sector.Scenario.BriefingAcknowledged);
        Assert.False(string.IsNullOrWhiteSpace(sector.Scenario.BriefingText));
        // The recorded authority is the sitting Sector Lord (the common path; seed 1 has a
        // sector capital with a governor).
        Assert.Equal(sector.GetSectorLord().Id, sector.Scenario.OriginalAuthorityCharacterId);
    }

    [Fact]
    public void PromisedWorld_IsMajorityImperial()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Imperial Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);

        // The world is invaded but not conquered: the plurality of its regions remain under
        // the default (Imperial) faction.
        Assert.True(promised.GetControllingFaction().IsDefaultFaction);
    }

    [Fact]
    public void PromisedWorld_HasExactlyNTyranidRegions()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Swarm Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);

        int tyranidRegionCount = TyranidRegions(promised, Tyranids).Count;
        Assert.InRange(tyranidRegionCount,
            ScenarioRules.MinTyranidRegions, ScenarioRules.MaxTyranidRegions);
        // The rest of the 16-region world is untouched by the stamp.
        Assert.True(tyranidRegionCount < promised.Regions.Length);
    }

    [Fact]
    public void Fleet_StartsInOrbit_WithNoLandedSquads()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Orbit Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);

        // Every player task force is in orbit over the promised world.
        List<TaskForce> playerForces = sector.PlayerForce.Fleet.TaskForces;
        Assert.NotEmpty(playerForces);
        Assert.All(playerForces, tf =>
        {
            Assert.Same(promised, tf.Planet);
            Assert.Equal(promised.Position, tf.Position);
            Assert.Contains(tf, promised.OrbitingTaskForceList);
        });

        // No squad has landed: none carries a CurrentRegion and no region holds a landed squad.
        Assert.All(sector.PlayerForce.Army.SquadMap.Values, s => Assert.Null(s.CurrentRegion));
        Assert.All(sector.PlayerForce.Army.SquadMap.Values.Where(s => s.Members.Count > 0),
            s => Assert.NotNull(s.BoardedLocation));
        IEnumerable<Squad> landed = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .SelectMany(r => r.RegionFactionMap.Values)
            .SelectMany(rf => rf.LandedSquads);
        Assert.Empty(landed);
    }

    [Fact]
    public void GrowthThrottle_OnlyOnTyranidRegions()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Throttle Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction tyranids = Tyranids;

        // Every Tyranid region faction on the promised world is throttled below 1.0.
        List<RegionFaction> tyranidFactions = TyranidRegions(promised, tyranids)
            .Select(r => r.RegionFactionMap[tyranids.Id])
            .ToList();
        Assert.NotEmpty(tyranidFactions);
        Assert.All(tyranidFactions, rf =>
            Assert.Equal(ScenarioRules.TyranidGrowthMultiplier, rf.GrowthMultiplier));

        // Nothing else in the sector is throttled: every non-stamped-Tyranid region faction
        // keeps the default 1.0 multiplier.
        IEnumerable<RegionFaction> others = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .SelectMany(r => r.RegionFactionMap.Values)
            .Where(rf => rf.PlanetFaction.Faction.Id != tyranids.Id
                         || rf.Region.Planet.Id != promised.Id);
        Assert.All(others, rf => Assert.Equal(1.0f, rf.GrowthMultiplier));
    }

    [Fact]
    public void Stamp_IsDeterministicForSeed()
    {
        Sector first = SectorBuilder.GenerateSector(7, _data, _date, "Deterministic Chapter");
        Sector second = SectorBuilder.GenerateSector(7, _data, _date, "Deterministic Chapter");

        Assert.Equal(first.Scenario.PromisedPlanetId, second.Scenario.PromisedPlanetId);
        Assert.Equal(first.Scenario.OriginalAuthorityCharacterId,
                     second.Scenario.OriginalAuthorityCharacterId);
        Assert.Equal(first.Scenario.BriefingText, second.Scenario.BriefingText);

        Faction tyranids = Tyranids;
        List<int> firstStamped = TyranidRegions(
                first.GetPlanet(first.Scenario.PromisedPlanetId), tyranids)
            .Select(r => r.Id).OrderBy(id => id).ToList();
        List<int> secondStamped = TyranidRegions(
                second.GetPlanet(second.Scenario.PromisedPlanetId), tyranids)
            .Select(r => r.Id).OrderBy(id => id).ToList();
        Assert.Equal(firstStamped, secondStamped);
    }

    // The opening now plays out as a pre-/post-landing simulation during generation (§4.24), so the
    // promised world's final state is the product of several scoped turns of combat and growth. That
    // whole sequence must remain deterministic per seed: two generations of the same seed must land
    // on an identical region-faction population map, not merely the same stamped region set.
    [Fact]
    public void Stamp_SimulatedBoardIsDeterministicForSeed()
    {
        Sector first = SectorBuilder.GenerateSector(7, _data, _date, "Determinism Chapter");
        Sector second = SectorBuilder.GenerateSector(7, _data, _date, "Determinism Chapter");

        Assert.Equal(RegionFactionPopulations(first), RegionFactionPopulations(second));
    }

    // (regionId, factionId) -> population across every region faction on the promised world, sorted
    // for a stable order-independent comparison.
    private static List<((int, int), long)> RegionFactionPopulations(Sector sector)
    {
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        return promised.Regions
            .SelectMany(r => r.RegionFactionMap.Values
                .Select(rf => ((r.Id, rf.PlanetFaction.Faction.Id), rf.Population)))
            .OrderBy(t => t.Item1.Item1).ThenBy(t => t.Item1.Item2)
            .ToList();
    }

    // Regression: the opening now runs a scoped pre-/post-landing simulation during generation
    // (§4.24), which drives real NPC recon/combat on the promised world. A recon that is detected by
    // a region with no squads to scramble produced an empty OpFor, and the downstream battle steps
    // (MeetingEngagement/Ambushed) assumed a non-empty OpposingSquads and threw — so generation
    // itself crashed for some seeds (seed 3 was the first repro). This runs a spread of seeds end to
    // end; the assertion is simply that every one generates without an exception.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(11)]
    public void GenerateSector_RunsScopedSimsWithoutThrowing(int seed)
    {
        Sector sector = SectorBuilder.GenerateSector(seed, _data, _date, "Robustness Chapter");

        Assert.NotNull(sector.Scenario);
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Assert.True(promised.GetControllingFaction().IsDefaultFaction);
    }

    // The planet-scoped sim must touch only the target world: a second planet's populations are left
    // exactly as generated (§4.24 — the pre/post-landing sims are scoped to the promised planet only).
    [Fact]
    public void SimulatePlanetForward_LeavesOtherPlanetsUntouched()
    {
        Sector sector = SectorBuilder.GenerateSector(7, _data, _date, "Scoped Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);

        // A different world to guard against the sim leaking across planets.
        Planet other = sector.Planets.Values.First(p => p.Id != promised.Id);
        List<((int, int), long)> before = other.Regions
            .SelectMany(r => r.RegionFactionMap.Values
                .Select(rf => ((r.Id, rf.PlanetFaction.Faction.Id), rf.Population)))
            .OrderBy(t => t.Item1.Item1).ThenBy(t => t.Item1.Item2)
            .ToList();

        new TurnController().SimulatePlanetForward(sector, promised, turns: 5);

        List<((int, int), long)> after = other.Regions
            .SelectMany(r => r.RegionFactionMap.Values
                .Select(rf => ((r.Id, rf.PlanetFaction.Faction.Id), rf.Population)))
            .OrderBy(t => t.Item1.Item1).ThenBy(t => t.Item1.Item2)
            .ToList();

        Assert.Equal(before, after);
    }
}
