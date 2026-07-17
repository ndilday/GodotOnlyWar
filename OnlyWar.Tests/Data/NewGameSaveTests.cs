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
// savable through the same aggregate mapping used by CurrentCampaignSaveWriter, without test-only
// registration of the army root on the player faction. Generation itself must register the
// OrderOfBattle root on Faction.Units, otherwise the save writes no Soldier rows and the first
// PlayerSoldier insert violates PlayerSoldier.SoldierId -> Soldier.Id.
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class NewGameSaveTests : IClassFixture<NewGameSaveFixture>
{
    private readonly GameRulesData _data;
    private readonly Date _date;
    private readonly GameStateRoundTripFixture _roundTrip;
    private readonly Sector _sector;

    public NewGameSaveTests(NewGameSaveFixture fixture)
    {
        _data = fixture.Data;
        _date = fixture.Date;
        _roundTrip = fixture.RoundTrip;
        _sector = fixture.Sector;
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void NewGame_SavesThroughProductionPath_WithoutManualRegistration()
    {
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, _sector);

        // The production-path save below depends on generation registering the root unit. Keep the
        // direct assertion here rather than generating a second sector in a registration-only test.
        Assert.Contains(_sector.PlayerForce.Army.OrderOfBattle, _data.PlayerFaction.Units);

        // Match CurrentCampaignSaveWriter's unit selection: no manual Units.Add.
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();

        string dbPath = GameStateRoundTripFixture.CreateTempDbPath("onlywar_newgame");
        try
        {
            _roundTrip.Save(_sector, dbPath, units);

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

    [Trait("Category", "Slow")]
    [Fact]
    public void FailedSave_LeavesPreviousSaveIntact_AndNoTempFiles()
    {
        GameDataSingleton.Instance.LoadGameDataFromBlob(_data, _date, _sector);
        List<Unit> units = _data.Factions.SelectMany(f => f.Units).ToList();

        string dir = Path.Combine(Path.GetTempPath(), $"onlywar_atomic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "save.s3db");
        try
        {
            // First, a good save.
            _roundTrip.Save(_sector, dbPath, units);
            long originalSoldiers = GameStateRoundTripFixture.CountRows(dbPath, "Soldier");
            Assert.True(originalSoldiers > 0);

            // Now a save guaranteed to fail mid-write: duplicating the root unit forces a
            // primary-key violation on the Unit insert, after the schema is created.
            List<Unit> corruptUnits = units.Concat(units).ToList();
            Assert.ThrowsAny<Exception>(() => _roundTrip.Save(_sector, dbPath, corruptUnits));

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

public sealed class NewGameSaveFixture
{
    internal GameRulesData Data { get; }
    internal Date Date { get; } = new(39, 500, 1);
    internal GameStateRoundTripFixture RoundTrip { get; }
    internal Sector Sector { get; }

    public NewGameSaveFixture()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        Data = new GameRulesData();
        GameDataSingleton.Instance.LoadGameDataFromBlob(Data, Date, null);
        Sector = SectorBuilder.GenerateSector(1, Data, Date, "New Game Save Fixture Chapter");
        RoundTrip = new GameStateRoundTripFixture(Data, Date);
    }
}
