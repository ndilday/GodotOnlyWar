using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Storage;
using System;
using System.IO;
using Xunit;

namespace OnlyWar.Tests.Data;

public sealed class DeploymentStorageTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"onlywar_storage_{Guid.NewGuid():N}");

    public DeploymentStorageTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void InstalledDatabaseFiles_AreLocatedWithoutWorkingDirectoryAssumptions()
    {
        Assert.True(File.Exists(GameStorage.RulesDatabasePath));
        Assert.True(File.Exists(GameStorage.SaveSchemaPath));
        Assert.Equal("OnlyWar.s3db", Path.GetFileName(GameStorage.RulesDatabasePath));
        Assert.Equal("SaveStructure.sql", Path.GetFileName(GameStorage.SaveSchemaPath));
    }

    [Fact]
    public void Discover_PrefersDefaultCompatibleSave_AndReportsInvalidSaves()
    {
        string defaultSave = CreateMetadataDatabase("default.s3db", SaveFormat.CurrentVersion);
        string newerNamedSave = CreateMetadataDatabase("slot-two.s3db", SaveFormat.CurrentVersion);
        string incompatibleSave = CreateMetadataDatabase("old.s3db", SaveFormat.CurrentVersion + 1);
        string corruptSave = Path.Combine(_tempDirectory, "corrupt.s3db");
        File.WriteAllText(corruptSave, "not a sqlite database");
        File.SetLastWriteTimeUtc(newerNamedSave, DateTime.UtcNow.AddMinutes(1));

        SaveGameCatalog catalog = new(_tempDirectory);
        var saves = catalog.Discover();

        Assert.Equal(4, saves.Count);
        Assert.Equal(defaultSave, catalog.FindPreferredCompatibleSave().FilePath);
        Assert.Contains(saves, save => save.FilePath == incompatibleSave
            && !save.IsCompatible
            && save.SaveVersion == SaveFormat.CurrentVersion + 1);
        Assert.Contains(saves, save => save.FilePath == corruptSave
            && !save.IsCompatible
            && !string.IsNullOrWhiteSpace(save.FailureReason));
    }

    [Fact]
    public void MissingSave_LoadDoesNotCreateEmptyDatabase()
    {
        string missingPath = Path.Combine(_tempDirectory, "missing.s3db");

        Assert.Throws<FileNotFoundException>(() => GameStateDataAccess.Instance.GetData(
            missingPath, null, null, null, null, null, null, null, null, null));
        Assert.False(File.Exists(missingPath));
    }

    [Fact]
    public void IncompatibleSave_IsRejectedBeforeSectorTablesAreRead()
    {
        string savePath = CreateMetadataDatabase("future.s3db", SaveFormat.CurrentVersion + 1);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            GameStateDataAccess.Instance.GetData(
                savePath, null, null, null, null, null, null, null, null, null));

        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void LegacyDefaultSave_IsCopiedOnceAndNeverOverwritesUserSave()
    {
        string legacyDirectory = Path.Combine(_tempDirectory, "legacy");
        string saveDirectory = Path.Combine(_tempDirectory, "user", "saves");
        Directory.CreateDirectory(legacyDirectory);
        string legacyPath = Path.Combine(legacyDirectory, GameStorage.DefaultSaveFileName);
        File.WriteAllText(legacyPath, "legacy-save");

        Assert.True(GameStorage.MigrateLegacyDefaultSave(saveDirectory, [legacyPath]));
        string migratedPath = Path.Combine(saveDirectory, GameStorage.DefaultSaveFileName);
        Assert.Equal("legacy-save", File.ReadAllText(migratedPath));

        File.WriteAllText(legacyPath, "changed-legacy-save");
        Assert.False(GameStorage.MigrateLegacyDefaultSave(saveDirectory, [legacyPath]));
        Assert.Equal("legacy-save", File.ReadAllText(migratedPath));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateMetadataDatabase(string fileName, int saveVersion)
    {
        string path = Path.Combine(_tempDirectory, fileName);
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        using SqliteConnection connection = new(connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE GlobalData (SaveVersion INTEGER NOT NULL);
            INSERT INTO GlobalData (SaveVersion) VALUES ($saveVersion);";
        command.Parameters.AddWithValue("$saveVersion", saveVersion);
        command.ExecuteNonQuery();
        return path;
    }
}
