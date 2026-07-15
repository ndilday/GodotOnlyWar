using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Storage;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Data;

public sealed class SaveGameManagerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"onlywar_save_manager_{Guid.NewGuid():N}");

    public SaveGameManagerTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void ManualSaves_AreNamedUniqueAndExposeChooserMetadata()
    {
        SaveGameManager manager = new(_tempDirectory);

        SaveGameEntry first = manager.CreateManualSave(
            "  My: Crusade?  ",
            "Sidecar Fallback",
            path => WriteMetadataSave(path, "Knights of Sol", 4, 221, 17));
        SaveGameEntry second = manager.CreateManualSave(
            "My: Crusade?",
            "Sidecar Fallback",
            path => WriteMetadataSave(path, "Knights of Sol", 4, 221, 18));

        Assert.NotEqual(first.FilePath, second.FilePath);
        Assert.Equal("My: Crusade?", first.DisplayName);
        Assert.Equal("Knights of Sol", first.CampaignName);
        Assert.Equal(new OnlyWar.Models.Date(4, 221, 17), first.CampaignDate);
        Assert.Equal(SaveGameKind.Manual, first.Kind);
        Assert.True(first.IsCompatible);
        Assert.StartsWith("manual-my-crusade-", Path.GetFileName(first.FilePath));
        Assert.DoesNotContain(':', Path.GetFileName(first.FilePath));
        Assert.DoesNotContain('?', Path.GetFileName(first.FilePath));
    }

    [Fact]
    public void FailedNewManualSave_LeavesNoPartialSaveOrSidecar()
    {
        SaveGameManager manager = new(_tempDirectory);

        Assert.Throws<InvalidOperationException>(() => manager.CreateManualSave(
            "Broken",
            "Chapter",
            path =>
            {
                File.WriteAllText(path, "partial");
                throw new InvalidOperationException("simulated write failure");
            }));

        Assert.Empty(Directory.EnumerateFiles(_tempDirectory));
    }

    [Fact]
    public void InvalidCompletedSave_IsRejectedAndPriorVersionIsRestored()
    {
        SaveGameManager manager = new(_tempDirectory);
        SaveGameEntry original = manager.CreateManualSave(
            "Checkpoint",
            "Chapter",
            path => WriteMetadataSave(path, "Chapter", 1, 1, 1));
        byte[] originalBytes = File.ReadAllBytes(original.FilePath);

        Assert.Throws<InvalidDataException>(() => manager.OverwriteManualSave(
            original.FilePath,
            "Checkpoint",
            "Chapter",
            path => File.WriteAllText(path, "not sqlite")));

        Assert.Equal(originalBytes, File.ReadAllBytes(original.FilePath));
        Assert.True(new SaveGameCatalog(_tempDirectory).Find(original.FilePath).IsCompatible);
    }

    [Fact]
    public void FailedOverwrite_RestoresPriorDatabaseAndChooserMetadata()
    {
        SaveGameManager manager = new(_tempDirectory);
        SaveGameEntry original = manager.CreateManualSave(
            "Before",
            "Original Chapter",
            path => WriteMetadataSave(path, "Original Chapter", 3, 100, 1));
        byte[] originalBytes = File.ReadAllBytes(original.FilePath);

        Assert.Throws<InvalidOperationException>(() => manager.OverwriteManualSave(
            original.FilePath,
            "After",
            "Replacement Chapter",
            path =>
            {
                File.WriteAllText(path, "broken replacement");
                throw new InvalidOperationException("simulated write failure");
            }));

        Assert.Equal(originalBytes, File.ReadAllBytes(original.FilePath));
        SaveGameEntry restored = new SaveGameCatalog(_tempDirectory).Find(original.FilePath);
        Assert.True(restored.IsCompatible);
        Assert.Equal("Before", restored.DisplayName);
        Assert.Equal("Original Chapter", restored.CampaignName);
    }

    [Fact]
    public void PostTurnAutosaves_RollAfterSuccessfulWrite_WithoutTouchingManualOrInitial()
    {
        SaveGameManager manager = new(_tempDirectory);
        SaveGameEntry manual = manager.CreateManualSave(
            "Checkpoint",
            "Chapter",
            path => WriteMetadataSave(path, "Chapter", 1, 1, 1));
        SaveGameEntry initial = manager.SaveInitialAutosave(
            "Chapter",
            path => WriteMetadataSave(path, "Chapter", 1, 1, 1));

        for (int week = 2; week <= 6; week++)
        {
            int capturedWeek = week;
            manager.SavePostTurnAutosave(
                "Chapter",
                path => WriteMetadataSave(path, "Chapter", 1, 1, capturedWeek));
        }

        var saves = new SaveGameCatalog(_tempDirectory).Discover();
        Assert.Equal(
            SaveGameManager.PostTurnAutosaveRetention,
            saves.Count(entry => Path.GetFileName(entry.FilePath).StartsWith(
                GameStorage.PostTurnAutosaveFilePrefix,
                StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(saves, entry => entry.FilePath == manual.FilePath
            && entry.Kind == SaveGameKind.Manual);
        Assert.Contains(saves, entry => entry.FilePath == initial.FilePath
            && entry.Kind == SaveGameKind.Autosave
            && entry.DisplayName == "Campaign Start");
    }

    [Fact]
    public void FailedPostTurnAutosave_DoesNotRotateExistingRecoveryPoints()
    {
        SaveGameManager manager = new(_tempDirectory);
        for (int week = 1; week <= 3; week++)
        {
            int capturedWeek = week;
            manager.SavePostTurnAutosave(
                "Chapter",
                path => WriteMetadataSave(path, "Chapter", 1, 1, capturedWeek));
        }
        string[] before = PostTurnPaths();

        Assert.Throws<IOException>(() => manager.SavePostTurnAutosave(
            "Chapter",
            path => throw new IOException("disk full")));

        Assert.Equal(before, PostTurnPaths());
    }

    [Fact]
    public void ProtectedPreTurnSave_IsDistinctAndFailurePreservesItsPriorVersion()
    {
        SaveGameManager manager = new(_tempDirectory);
        SaveGameEntry protectedSave = manager.SaveProtectedPreTurn(
            "Chapter",
            path => WriteMetadataSave(path, "Chapter", 2, 50, 10));
        byte[] originalBytes = File.ReadAllBytes(protectedSave.FilePath);

        Assert.Equal(SaveGameKind.ProtectedPreTurn, protectedSave.Kind);
        Assert.Equal(GameStorage.ProtectedPreTurnSaveFileName,
            Path.GetFileName(protectedSave.FilePath));
        Assert.Throws<IOException>(() => manager.SaveProtectedPreTurn(
            "Chapter",
            path =>
            {
                File.WriteAllText(path, "partial");
                throw new IOException("disk full");
            }));

        Assert.Equal(originalBytes, File.ReadAllBytes(protectedSave.FilePath));
        Assert.Throws<InvalidOperationException>(() =>
            manager.DeleteManualSave(protectedSave.FilePath));
    }

    [Fact]
    public void DeleteManualSave_RemovesDatabaseAndSidecar()
    {
        SaveGameManager manager = new(_tempDirectory);
        SaveGameEntry save = manager.CreateManualSave(
            "Delete Me",
            "Chapter",
            path => WriteMetadataSave(path, "Chapter", 1, 1, 1));

        manager.DeleteManualSave(save.FilePath);

        Assert.False(File.Exists(save.FilePath));
        Assert.False(File.Exists(SaveGameMetadataStore.GetPath(save.FilePath)));
    }

    [Fact]
    public void Catalog_LegacyDefaultHasUsefulFallbackLabelsAndLocalWriteTime()
    {
        string path = Path.Combine(_tempDirectory, GameStorage.DefaultSaveFileName);
        WriteMetadataSave(path, "Legacy Chapter", 5, 999, 52);

        SaveGameEntry entry = Assert.Single(new SaveGameCatalog(_tempDirectory).Discover());

        Assert.True(entry.IsLegacyDefault);
        Assert.Equal("Legacy Save", entry.DisplayName);
        Assert.Equal("Legacy Chapter", entry.CampaignName);
        Assert.Equal(entry.LastWriteTimeUtc.ToLocalTime(), entry.LastWriteTimeLocal);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string[] PostTurnPaths()
    {
        return Directory.EnumerateFiles(
                _tempDirectory,
                $"{GameStorage.PostTurnAutosaveFilePrefix}*.s3db")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void WriteMetadataSave(
        string path,
        string chapterName,
        int millenium,
        int year,
        int week,
        int version = SaveFormat.CurrentVersion)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        using SqliteConnection connection = new(connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE GlobalData (
                Millenium INTEGER NOT NULL,
                Year INTEGER NOT NULL,
                Week INTEGER NOT NULL,
                SaveVersion INTEGER NOT NULL);
            INSERT INTO GlobalData VALUES ($millenium, $year, $week, $version);

            CREATE TABLE Unit (
                Id INTEGER PRIMARY KEY,
                FactionId INTEGER NOT NULL,
                UnitTemplateId INTEGER NOT NULL,
                ParentUnitId INTEGER,
                Name TEXT NOT NULL);
            INSERT INTO Unit VALUES (10, 7, 100, NULL, $chapterName);
            INSERT INTO Unit VALUES (11, 7, 101, 10, 'First Company');

            CREATE TABLE Squad (
                Id INTEGER PRIMARY KEY,
                SquadTemplateId INTEGER NOT NULL,
                ParentUnitId INTEGER NOT NULL,
                Name TEXT NOT NULL);
            INSERT INTO Squad VALUES (20, 200, 11, 'First Squad');

            CREATE TABLE Soldier (
                Id INTEGER PRIMARY KEY,
                SquadId INTEGER);
            INSERT INTO Soldier VALUES (30, 20);

            CREATE TABLE PlayerSoldier (SoldierId INTEGER PRIMARY KEY);
            INSERT INTO PlayerSoldier VALUES (30);";
        command.Parameters.AddWithValue("$millenium", millenium);
        command.Parameters.AddWithValue("$year", year);
        command.Parameters.AddWithValue("$week", week);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$chapterName", chapterName);
        command.ExecuteNonQuery();
    }
}
