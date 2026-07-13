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

// Coverage for the "Promised World" generation override (Design/OpeningScenario.md section 3,
// step 2): ScenarioBuilder.StampPromisedWorld, invoked from SectorBuilder.GenerateSector in place
// of the old FoundTakebackPlanet prototype. The stamp invariants and full per-seed determinism are
// the load-bearing guarantees for the opening.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
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
    public void GenerateSector_Seed1ProducesPlayablePromisedWorldInvariants()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Promise Chapter");
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        Faction tyranids = Tyranids;
        Faction cult = _data.SectorFactions.Infiltrator;
        Faction imperial = _data.DefaultFaction;

        Assert.NotNull(sector.Scenario);
        Assert.Equal(ScenarioType.PromisedWorld, sector.Scenario.Type);
        Assert.Equal(ObjectiveState.Pending, sector.Scenario.State);
        Assert.False(sector.Scenario.BriefingAcknowledged);
        Assert.False(string.IsNullOrWhiteSpace(sector.Scenario.BriefingText));
        // The recorded authority is the sitting Sector Lord (the common path; seed 1 has a
        // sector capital with a governor).
        Assert.Equal(sector.GetSectorLord().Id, sector.Scenario.OriginalAuthorityCharacterId);

        // The founding seed (PRD 4.23 / Supply & Requisition Phase 1) is a generous,
        // non-zero starting pool.
        Assert.True(sector.PlayerForce.Army.Requisition > 0);

        // The world is invaded but not conquered: the Imperial populace still overwhelmingly
        // outnumbers every invader on the planet. Region-plurality is not used here; once the cult
        // rises publicly it co-occupies the Imperial regions, so every region reads as contested.
        long imperialPop = promised.Regions
            .Where(r => r.RegionFactionMap.ContainsKey(imperial.Id))
            .Sum(r => r.RegionFactionMap[imperial.Id].Population);
        long largestInvaderPop = promised.Regions
            .SelectMany(r => r.RegionFactionMap.Values)
            .Where(rf => !rf.PlanetFaction.Faction.IsDefaultFaction && !rf.PlanetFaction.Faction.IsPlayerFaction)
            .GroupBy(rf => rf.PlanetFaction.Faction.Id)
            .Select(g => g.Sum(rf => rf.Population))
            .DefaultIfEmpty(0L)
            .Max();

        Assert.True(imperialPop > largestInvaderPop,
            $"expected Imperial population {imperialPop} to exceed the largest invader's {largestInvaderPop}");

        // The stamp seeds MinTyranidRegions..MaxTyranidRegions regions, but generation then runs the
        // post-landing sim, during which a now-fully-organized swarm can spread to fresh biomass.
        // The hand-off invariants: the swarm holds at least the stamp floor and not the whole world.
        List<RegionFaction> tyranidFactions = TyranidRegions(promised, tyranids)
            .Select(r => r.RegionFactionMap[tyranids.Id])
            .ToList();
        Assert.NotEmpty(tyranidFactions);
        Assert.True(tyranidFactions.Count >= ScenarioRules.MinTyranidRegions,
            $"expected at least {ScenarioRules.MinTyranidRegions} Tyranid regions, got {tyranidFactions.Count}");
        Assert.True(tyranidFactions.Count < promised.Regions.Length,
            "the swarm should not hold the entire world at hand-off");

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

        // Tyranids are a Consumption faction: no organic birthrate, so the scenario applies no
        // growth throttle. Winnability comes from the finite stranded biomass budget.
        Assert.Equal(GrowthType.Consumption, tyranids.GrowthType);
        IEnumerable<RegionFaction> allRegionFactions = sector.Planets.Values
            .SelectMany(p => p.Regions)
            .SelectMany(r => r.RegionFactionMap.Values);
        Assert.All(allRegionFactions, rf => Assert.Equal(1.0f, rf.GrowthMultiplier));

        PlanetFaction cultPlanetFaction = promised.PlanetFactionMap[cult.Id];
        List<Region> imperialRegions = promised.Regions
            .Where(region => region.RegionFactionMap.ContainsKey(imperial.Id))
            .ToList();

        Assert.NotEmpty(imperialRegions);
        // The cult knows its home ground: it starts with strong awareness of every region holding a
        // public Imperial force. Seeded at reveal, before the pre-landing sim decays it a little.
        Assert.Contains(imperialRegions, region =>
            cultPlanetFaction.GetRegionIntel(region) > 0f);
    }

    // The opening now plays out as a pre-/post-landing simulation during generation, so the promised
    // world's final state is the product of several scoped turns of combat and growth. This guards
    // both the stamped metadata and the final simulated board for deterministic replay.
    [Fact]
    public void GenerateSector_SameSeedProducesSameOpeningOutcome()
    {
        (GameRulesData firstData, Sector first) = GenerateFreshSector(7);
        (GameRulesData secondData, Sector second) = GenerateFreshSector(7);

        Assert.Equal(first.Scenario.Type, second.Scenario.Type);
        Assert.Equal(first.Scenario.State, second.Scenario.State);
        Assert.Equal(first.Scenario.PromisedPlanetId, second.Scenario.PromisedPlanetId);
        Assert.Equal(first.Scenario.OriginalAuthorityCharacterId,
                     second.Scenario.OriginalAuthorityCharacterId);
        Assert.Equal(first.Scenario.BriefingText, second.Scenario.BriefingText);

        Faction firstTyranids = firstData.SectorFactions.Invader;
        Faction secondTyranids = secondData.SectorFactions.Invader;
        List<int> firstStamped = TyranidRegions(
                first.GetPlanet(first.Scenario.PromisedPlanetId), firstTyranids)
            .Select(r => r.Id).OrderBy(id => id).ToList();
        List<int> secondStamped = TyranidRegions(
                second.GetPlanet(second.Scenario.PromisedPlanetId), secondTyranids)
            .Select(r => r.Id).OrderBy(id => id).ToList();
        Assert.Equal(firstStamped, secondStamped);
        Assert.Equal(FactionPopulationTotals(first), FactionPopulationTotals(second));
    }

    private (GameRulesData Data, Sector Sector) GenerateFreshSector(int seed)
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        GameRulesData data = new();
        GameDataSingleton.Instance.LoadGameDataFromBlob(data, _date, null);
        Sector sector = SectorBuilder.GenerateSector(seed, data, _date, "Deterministic Chapter");
        return (data, sector);
    }

    // Faction totals are part of the opening outcome, while individual region-faction
    // rows are implementation detail and would make this replay test unnecessarily brittle.
    private static List<(int FactionId, long Population)> FactionPopulationTotals(Sector sector)
    {
        Planet promised = sector.GetPlanet(sector.Scenario.PromisedPlanetId);
        return promised.Regions
            .SelectMany(r => r.RegionFactionMap.Values
                .Select(rf => rf))
            .GroupBy(rf => rf.PlanetFaction.Faction.Id)
            .Select(group => (FactionId: group.Key, Population: group.Sum(rf => rf.Population)))
            .OrderBy(t => t.FactionId)
            .ToList();
    }

    // Regression: the opening now runs a scoped pre-/post-landing simulation during generation,
    // which drives real NPC recon/combat on the promised world. A recon that is detected by a region
    // with no squads to scramble produced an empty OpFor, and the downstream battle steps assumed a
    // non-empty OpposingSquads and threw, so generation itself crashed for some seeds. Keep the
    // first repro seed plus a known-good baseline in the normal suite.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void GenerateSector_KeyScopedSimSeedsRunWithoutThrowing(int seed)
    {
        Sector sector = SectorBuilder.GenerateSector(seed, _data, _date, "Robustness Chapter");

        Assert.NotNull(sector.Scenario);
        Assert.Equal(ObjectiveState.Pending, sector.Scenario.State);
        // A valid, playable promised world exists. Control is seed-dependent.
        Assert.NotNull(sector.GetPlanet(sector.Scenario.PromisedPlanetId));
    }

    // Broader coverage for seed-sensitive scoped sim failures. This is intentionally kept out of
    // the default fast path; run with a Category=Slow filter when tuning generation/balance.
    [Trait("Category", "Slow")]
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(11)]
    public void GenerateSector_AdditionalScopedSimSeedsRunWithoutThrowing(int seed)
    {
        Sector sector = SectorBuilder.GenerateSector(seed, _data, _date, "Robustness Chapter");

        Assert.NotNull(sector.Scenario);
        Assert.Equal(ObjectiveState.Pending, sector.Scenario.State);
        Assert.NotNull(sector.GetPlanet(sector.Scenario.PromisedPlanetId));
    }

    // The planet-scoped sim must touch only the target world: a second planet's populations are left
    // exactly as generated.
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
