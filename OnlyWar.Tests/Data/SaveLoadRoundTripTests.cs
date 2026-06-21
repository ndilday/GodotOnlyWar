using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

// End-to-end save/load coverage (TDD 9.2.1 #1). Generates a real new-game sector,
// writes it through GameStateDataAccess.SaveData, reads it back through GetData, and
// asserts the high-level state survives the round trip. This is the regression guard
// for schema drift: any future change to the save schema that is not propagated to
// both SaveData and GetData will fail here.
public class SaveLoadRoundTripTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);

    public SaveLoadRoundTripTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
    }

    [Fact]
    public void SaveThenLoad_GeneratedSector_PreservesHighLevelState()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Round Trip Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        // The new-game setup flow registers the generated army's root unit on the player
        // faction; the save path reads units from faction.Units, so mirror that here.
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }

        string dbPath = Path.Combine(
            Path.GetTempPath(), $"onlywar_roundtrip_{Guid.NewGuid():N}.s3db");
        try
        {
            List<Unit> originalUnits = _data.Factions.SelectMany(f => f.Units).ToList();
            Save(sector, dbPath, originalUnits);
            GameStateDataBlob loaded = Load(dbPath);

            Assert.Equal(_date, loaded.CurrentDate);
            Assert.Equal(sector.Planets.Count, loaded.Planets.Count);
            Assert.Equal(sector.Characters.Count(), loaded.Characters.Count);
            Assert.Equal(sector.PlayerForce.Requests.Count, loaded.Requests.Count);

            int originalShips = sector.Fleets.Values.SelectMany(tf => tf.Ships).Count();
            int loadedShips = loaded.Fleets.SelectMany(tf => tf.Ships).Count();
            Assert.Equal(originalShips, loadedShips);

            Assert.Equal(CountSoldiers(originalUnits), CountSoldiers(loaded.Units));
            Assert.Equal(CountSquads(originalUnits), CountSquads(loaded.Units));
            Assert.Equal(TotalPopulation(sector.Planets.Values), TotalPopulation(loaded.Planets));

            // Carrying capacity is generated, persisted, and restored per region; and no
            // region is generated above its carrying capacity (PRD Strategic Layer Phase 2).
            Assert.Equal(TotalCarryingCapacity(sector.Planets.Values), TotalCarryingCapacity(loaded.Planets));
            Assert.True(sector.Planets.Values.Sum(p => p.Regions.Length) > 0);
            Assert.All(
                sector.Planets.Values.SelectMany(p => p.Regions),
                r => Assert.True(r.Population <= r.CarryingCapacity,
                    $"Region {r.Id} population {r.Population} exceeds capacity {r.CarryingCapacity}"));

            // Open-ended evaluation ratings survive the round trip (the SoldierEvaluation
            // / SoldierEvaluationRating split). Every loaded evaluation carries its keyed
            // rating values.
            List<SoldierEvaluation> loadedEvaluations = loaded.Units
                .SelectMany(u => u.GetAllSquads())
                .SelectMany(s => s.Members)
                .OfType<PlayerSoldier>()
                .SelectMany(ps => ps.SoldierEvaluationHistory)
                .ToList();
            Assert.NotEmpty(loadedEvaluations);
            Assert.All(loadedEvaluations, e => Assert.Contains(RatingKeys.Melee, e.Ratings.Keys));
        }
        finally
        {
            // GameStateDataAccess closes but does not dispose its connections, so the
            // pooled handle keeps the file open; clear the pool before deleting.
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
            sector.Characters,
            sector.PlayerForce.Requests,
            sector.Planets.Values,
            sector.Fleets.Values,
            units,
            sector.PlayerForce.Army.PlayerSoldierMap.Values,
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

    private static int CountSoldiers(IEnumerable<Unit> rootUnits)
    {
        return rootUnits.Sum(u => u.GetAllSquads().Sum(s => s.Members.Count));
    }

    private static int CountSquads(IEnumerable<Unit> rootUnits)
    {
        return rootUnits.Sum(u => u.GetAllSquads().Count());
    }

    private static long TotalCarryingCapacity(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Regions.Sum(r => r.CarryingCapacity));
    }

    private static long TotalPopulation(IEnumerable<Models.Planets.Planet> planets)
    {
        return planets.Sum(p => p.Population);
    }
}
