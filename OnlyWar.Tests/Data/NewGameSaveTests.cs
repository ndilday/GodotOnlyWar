using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

// Regression guard for the new-game save FK failure: a freshly generated chapter must be
// savable through the exact path MainGameScene.OnSaveButtonPressed uses, WITHOUT any test-only
// registration of the army root on the player faction. Generation itself must register the
// OrderOfBattle root on Faction.Units, otherwise the save writes no Soldier rows and the first
// PlayerSoldier insert violates PlayerSoldier.SoldierId -> Soldier.Id.
public class NewGameSaveTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);

    public NewGameSaveTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
    }

    [Fact]
    public void GeneratedChapter_RegistersRootUnitOnPlayerFaction()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Registration Chapter");
        Assert.Contains(sector.PlayerForce.Army.OrderOfBattle, _data.PlayerFaction.Units);
    }

    [Fact]
    public void NewGame_SavesThroughProductionPath_WithoutManualRegistration()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "New Game Save Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);

        // Exactly what MainGameScene.OnSaveButtonPressed does — no manual Units.Add.
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();

        string dbPath = Path.Combine(Path.GetTempPath(), $"onlywar_newgame_{Guid.NewGuid():N}.s3db");
        string schemaPath = Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql");
        try
        {
            GameStateDataAccess.Instance.SaveData(
                dbPath, _date,
                sector.PlayerForce.Army.Requisition,
                sector.PlayerForce.GeneseedStockpile,
                sector.PlayerForce.GeneseedPurity,
                sector.Scenario,
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

            // Sanity: the player's soldiers actually made it into the file.
            Assert.True(File.Exists(dbPath));
            long soldierCount = CountRows(dbPath, "Soldier");
            Assert.True(soldierCount > 0, "expected Soldier rows to be written");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch (IOException) { }
        }
    }

    [Fact]
    public void FailedSave_LeavesPreviousSaveIntact_AndNoTempFiles()
    {
        Sector sector = SectorBuilder.GenerateSector(1, _data, _date, "Atomic Save Chapter");
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, sector);
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();

        string dir = Path.Combine(Path.GetTempPath(), $"onlywar_atomic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "save.s3db");
        string schemaPath = Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql");
        try
        {
            // First, a good save.
            Save(sector, dbPath, units, schemaPath);
            long originalSoldiers = CountRows(dbPath, "Soldier");
            Assert.True(originalSoldiers > 0);

            // Now a save guaranteed to fail mid-write: duplicating the root unit forces a
            // primary-key violation on the Unit insert, after the schema is created.
            List<Unit> corruptUnits = units.Concat(units).ToList();
            Assert.ThrowsAny<Exception>(() => Save(sector, dbPath, corruptUnits, schemaPath));

            // The prior good save must survive untouched.
            Assert.True(File.Exists(dbPath), "previous save file was destroyed by a failed save");
            Assert.Equal(originalSoldiers, CountRows(dbPath, "Soldier"));

            // No temp scratch files should be left behind.
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private void Save(Sector sector, string dbPath, IEnumerable<Unit> units, string schemaPath)
    {
        GameStateDataAccess.Instance.SaveData(
            dbPath, _date,
            sector.PlayerForce.Army.Requisition,
            sector.PlayerForce.GeneseedStockpile,
            sector.PlayerForce.GeneseedPurity,
            sector.Scenario,
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

    private static long CountRows(string dbPath, string table)
    {
        var b = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath };
        using var con = new Microsoft.Data.Sqlite.SqliteConnection(b.ToString());
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
