using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

// Coverage for the governance hierarchy (Design/OpeningScenario.md §2.3 / step 1a):
// the derived Sector Lord / subsector-governor designation folded into
// SectorBuilder.GenerateWarpNetwork. The designation is recomputed (not persisted),
// so the round-trip test proves it re-derives identically from saved planet data.
public class GovernanceHierarchyTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);

    public GovernanceHierarchyTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
    }

    [Fact]
    public void GenerateWarpNetwork_TagsExactlyOneSectorCapital()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Capital Chapter");

        List<Planet> sectorCapitals = sector.Planets.Values
            .Where(p => p.GovernanceTier == GovernanceTier.SectorCapital)
            .ToList();

        Assert.Single(sectorCapitals);
        Assert.Same(sectorCapitals[0], sector.GetSectorCapital());
    }

    [Fact]
    public void EachSubsectorWithImperialWorld_HasExactlyOneSeat()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Seat Chapter");

        // At least one subsector must hold an Imperial world for this test to be meaningful.
        Assert.Contains(sector.Subsectors, s => s.GovernanceSeat != null);

        foreach (Subsector subsector in sector.Subsectors)
        {
            List<Planet> imperialWorlds = subsector.Planets
                .Where(p => p.GetControllingFaction().IsDefaultFaction)
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

    [Fact]
    public void GetSectorLord_ReturnsSectorCapitalGovernor()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Lord Chapter");

        Planet capital = sector.GetSectorCapital();
        Assert.NotNull(capital);

        Character lord = sector.GetSectorLord();
        // The Sector Lord is precisely the governor seated on the sector capital, which is
        // the leader of the capital's controlling (Imperial) PlanetFaction.
        Assert.Same(capital.Governor, lord);
        Assert.Same(
            capital.PlanetFactionMap[capital.GetControllingFaction().Id].Leader,
            lord);
    }

    [Fact]
    public void Governance_IsDeterministicForSeed()
    {
        Sector first = SectorBuilder.GenerateSector(7, _data, _date, "Deterministic Chapter");
        Sector second = SectorBuilder.GenerateSector(7, _data, _date, "Deterministic Chapter");

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

    [Fact]
    public void Governance_RederivesIdenticallyAfterSaveLoad()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Round Trip Governance Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        int originalCapitalId = sector.GetSectorCapital().Id;
        int originalLordId = sector.GetSectorLord().Id;

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_governance_roundtrip_{Guid.NewGuid():N}.s3db");
        try
        {
            Save(sector, dbPath, _data.Factions.SelectMany(f => f.Units).ToList());
            GameStateDataBlob loaded = Load(dbPath);

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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file; ignore if still locked.
            }
        }
    }

    private void Save(Sector sector, string dbPath, IEnumerable<Unit> units)
    {
        string schemaPath = Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql");
        GameStateDataAccess.Instance.SaveData(
            dbPath,
            _date,
            sector.PlayerForce.Army.Requisition,
            sector.PlayerForce.GeneseedStockpile,
            sector.PlayerForce.GeneseedPurity,
            sector.PlayerForce.Army.MedicalProcedures,
            sector.Characters,
            sector.PlayerForce.Requests,
            sector.Planets.Values,
            sector.Fleets.Values,
            units,
            sector.PlayerForce.Army.PlayerSoldierMap.Values,
            sector.PlayerForce.Army.FallenBrothers.Values,
            sector.PlayerForce.BattleHistory,
            schemaPath);
    }

    private GameStateDataBlob Load(string dbPath)
    {
        var shipTemplateMap = _data.Factions.Where(f => f.ShipTemplates != null)
            .SelectMany(f => f.ShipTemplates.Values).ToDictionary(s => s.Id);
        var unitTemplateMap = _data.Factions.Where(f => f.UnitTemplates != null)
            .SelectMany(f => f.UnitTemplates.Values).ToDictionary(u => u.Id);
        var squadTemplateMap = _data.Factions.Where(f => f.SquadTemplates != null)
            .SelectMany(f => f.SquadTemplates.Values).ToDictionary(s => s.Id);
        var hitLocations = _data.BodyHitLocationTemplateMap.Values.SelectMany(hl => hl)
            .Distinct().ToDictionary(hl => hl.Id);
        var soldierTypeMap = _data.Factions.Where(f => f.SoldierTemplates != null)
            .SelectMany(f => f.SoldierTemplates.Values).ToDictionary(st => st.Id);

        return GameStateDataAccess.Instance.GetData(
            dbPath,
            _data.Factions.ToDictionary(f => f.Id),
            _data.PlanetTemplateMap,
            shipTemplateMap,
            unitTemplateMap,
            squadTemplateMap,
            _data.WeaponSets,
            hitLocations,
            _data.BaseSkillMap,
            soldierTypeMap);
    }
}
