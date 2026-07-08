using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Data;

// Regression guard for the new-game save FK failure: a freshly generated chapter must be
// savable through the exact path MainGameScene.OnSaveButtonPressed uses, without any test-only
// registration of the army root on the player faction. Generation itself must register the
// OrderOfBattle root on Faction.Units, otherwise the save writes no Soldier rows and the first
// PlayerSoldier insert violates PlayerSoldier.SoldierId -> Soldier.Id.
public class NewGameSaveTests
{
    private readonly GameRulesData _data;
    private readonly Date _date = new(39, 500, 1);
    private readonly GameStateRoundTripFixture _roundTrip;

    public NewGameSaveTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        _data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, null);
        _roundTrip = new GameStateRoundTripFixture(_data, _date);
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

        // Exactly what MainGameScene.OnSaveButtonPressed does: no manual Units.Add.
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_newgame");
        try
        {
            _roundTrip.Save(sector, dbPath, units);

            // Sanity: the player's soldiers actually made it into the file.
            Assert.True(File.Exists(dbPath));
            long soldierCount = GameStateRoundTripFixture.CountRows(dbPath, "Soldier");
            Assert.True(soldierCount > 0, "expected Soldier rows to be written");
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(dbPath);
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
        try
        {
            // First, a good save.
            _roundTrip.Save(sector, dbPath, units);
            long originalSoldiers = GameStateRoundTripFixture.CountRows(dbPath, "Soldier");
            Assert.True(originalSoldiers > 0);

            // Now a save guaranteed to fail mid-write: duplicating the root unit forces a
            // primary-key violation on the Unit insert, after the schema is created.
            List<Unit> corruptUnits = units.Concat(units).ToList();
            Assert.ThrowsAny<Exception>(() => _roundTrip.Save(sector, dbPath, corruptUnits));

            // The prior good save must survive untouched.
            Assert.True(File.Exists(dbPath), "previous save file was destroyed by a failed save");
            Assert.Equal(originalSoldiers, GameStateRoundTripFixture.CountRows(dbPath, "Soldier"));

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
}
