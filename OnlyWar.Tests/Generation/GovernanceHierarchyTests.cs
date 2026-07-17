using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

// Coverage for the governance hierarchy (Design/OpeningScenario.md §2.3 / step 1a):
// the derived Sector Lord / subsector-governor designation folded into
// SectorBuilder.GenerateWarpNetwork. The designation is recomputed (not persisted),
// so the round-trip test proves it re-derives identically from saved planet data.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class GovernanceHierarchyTests : IClassFixture<GovernanceHierarchyFixture>
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);
    private readonly GameStateRoundTripFixture _roundTrip;
    private readonly Sector _seedOneSector;

    public GovernanceHierarchyTests(GovernanceHierarchyFixture fixture)
    {
        _data = fixture.Data;
        _seedOneSector = fixture.SeedOneSector;
        _roundTrip = new GameStateRoundTripFixture(_data, _date);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void GenerateWarpNetwork_TagsExactlyOneSectorCapital()
    {
        List<Planet> sectorCapitals = _seedOneSector.Planets.Values
            .Where(p => p.GovernanceTier == GovernanceTier.SectorCapital)
            .ToList();

        Assert.Single(sectorCapitals);
        Assert.Same(sectorCapitals[0], _seedOneSector.GetSectorCapital());
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void EachSubsectorWithImperialWorld_HasExactlyOneSeat()
    {
        // At least one subsector must hold an Imperial world for this test to be meaningful.
        Assert.Contains(_seedOneSector.Subsectors, s => s.GovernanceSeat != null);

        foreach (Subsector subsector in _seedOneSector.Subsectors)
        {
            List<Planet> imperialWorlds = subsector.Planets
                .Where(p => p.GetControllingFaction()?.IsDefaultFaction == true)
                .ToList();

            // Worlds tagged as a capital of any tier are the subsector's seat.
            List<Planet> tagged = subsector.Planets
                .Where(p => p.GovernanceTier != GovernanceTier.Planetary)
                .ToList();

            if (imperialWorlds.Count == 0)
            {
                Assert.Null(subsector.GovernanceSeat);
                Assert.Empty(tagged);
                continue;
            }

            // Exactly one seat, and it is the highest-Importance Imperial world.
            Planet seat = Assert.Single(tagged);
            Assert.Same(seat, subsector.GovernanceSeat);
            Assert.True(seat.GetControllingFaction().IsDefaultFaction);
            Assert.Equal(imperialWorlds.Max(p => p.Importance), seat.Importance);
        }
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void GetSectorLord_ReturnsSectorCapitalGovernor()
    {
        Planet capital = _seedOneSector.GetSectorCapital();
        Assert.NotNull(capital);

        Character lord = _seedOneSector.GetSectorLord();
        // The Sector Lord is precisely the governor seated on the sector capital, which is
        // the leader of the capital's controlling (Imperial) PlanetFaction.
        Assert.Same(capital.Governor, lord);
        Assert.Same(
            capital.PlanetFactionMap[capital.GetControllingFaction().Id].Leader,
            lord);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Governance_IsDeterministicForSeed()
    {
        GameRulesData firstData = LoadFreshRulesData();
        GameRulesData secondData = LoadFreshRulesData();
        Sector first = SectorBuilder.GenerateSector(7, firstData, _date, "Deterministic Chapter");
        Sector second = SectorBuilder.GenerateSector(7, secondData, _date, "Deterministic Chapter");

        Planet firstCapital = first.GetSectorCapital();
        Planet secondCapital = second.GetSectorCapital();

        Assert.NotNull(firstCapital);
        Assert.Equal(firstCapital.Id, secondCapital.Id);
        Assert.Equal(first.GetSectorLord()?.Id, second.GetSectorLord()?.Id);

        // Subsector seats line up by id as well.
        Dictionary<ushort, int?> firstSeats = first.Subsectors
            .ToDictionary(s => s.Id, s => (int?)s.GovernanceSeat?.Id);
        foreach (Subsector subsector in second.Subsectors)
        {
            Assert.Equal(firstSeats[subsector.Id], subsector.GovernanceSeat?.Id);
        }
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void Governance_RederivesIdenticallyAfterSaveLoad()
    {
        Sector sector = _seedOneSector;
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        _roundTrip.RegisterPlayerArmy(sector);

        int originalCapitalId = sector.GetSectorCapital().Id;
        int originalLordId = sector.GetSectorLord().Id;

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_governance_roundtrip");
        try
        {
            _roundTrip.Save(sector, dbPath, _roundTrip.CurrentUnits);
            GameStateDataBlob loaded = _roundTrip.Load(dbPath);

            // Reconstruct the sector exactly as StartMenu.LoadGameData does, then rebuild
            // the derived warp network + governance designation from the persisted planets.
            Sector reloaded = new Sector(
                sector.PlayerForce, loaded.Characters, loaded.Planets, loaded.Fleets);
            SectorBuilder.GenerateWarpNetwork(reloaded, _data);

            Planet reloadedCapital = reloaded.GetSectorCapital();
            Assert.NotNull(reloadedCapital);
            Assert.Equal(originalCapitalId, reloadedCapital.Id);

            // Exactly one capital after re-derivation, and the Sector Lord round-trips
            // because the governor characters are persisted as planet leaders.
            Assert.Single(reloaded.Planets.Values, p => p.GovernanceTier == GovernanceTier.SectorCapital);
            Assert.Equal(originalLordId, reloaded.GetSectorLord().Id);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
        }
    }

    private static GameRulesData LoadFreshRulesData()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        GameRulesData data = new();
        GameDataSingleton.Instance.LoadGameDataFromBlob(data, new Date(39, 500, 1), null);
        return data;
    }
}

public sealed class GovernanceHierarchyFixture
{
    internal GameRulesData Data { get; }
    internal Sector SeedOneSector { get; }

    public GovernanceHierarchyFixture()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        Data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(Data, new Date(39, 500, 1), null);
        SeedOneSector = SectorBuilder.GenerateSector(1, Data, new Date(39, 500, 1), "Governance Fixture Chapter");
    }
}
