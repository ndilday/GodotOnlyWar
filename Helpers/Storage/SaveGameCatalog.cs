using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Storage
{
    internal enum SaveGameKind
    {
        Manual,
        Autosave,
        ProtectedPreTurn
    }

    internal enum SaveGameCompatibility
    {
        Compatible,
        Incompatible,
        Corrupt
    }

    internal sealed record SaveGameEntry(
        string FilePath,
        string DisplayName,
        string CampaignName,
        Date CampaignDate,
        DateTime LastWriteTimeUtc,
        SaveGameKind Kind,
        int? SaveVersion,
        SaveGameCompatibility Compatibility,
        string FailureReason)
    {
        internal bool IsCompatible => Compatibility == SaveGameCompatibility.Compatible;
        internal DateTime LastWriteTimeLocal => LastWriteTimeUtc.ToLocalTime();
        internal bool IsLegacyDefault => string.Equals(
            Path.GetFileName(FilePath),
            GameStorage.DefaultSaveFileName,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Discovers saves by reading only chooser metadata. It never constructs a Sector and always
    /// opens SQLite read-only, so discovery cannot create or modify a campaign file.
    /// </summary>
    internal sealed class SaveGameCatalog
    {
        private readonly string _saveDirectory;

        internal SaveGameCatalog(string saveDirectory)
        {
            _saveDirectory = Path.GetFullPath(
                saveDirectory ?? throw new ArgumentNullException(nameof(saveDirectory)));
        }

        internal IReadOnlyList<SaveGameEntry> Discover()
        {
            if (!Directory.Exists(_saveDirectory))
            {
                return [];
            }

            return Directory
                .EnumerateFiles(_saveDirectory, "*.s3db", SearchOption.TopDirectoryOnly)
                .Select(Inspect)
                .OrderByDescending(entry => entry.LastWriteTimeUtc)
                .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        internal SaveGameEntry FindPreferredCompatibleSave()
        {
            // Retained for callers transitioning to the visible chooser. default.s3db remains a
            // sensible preference for legacy installations, followed by the newest valid save.
            return Discover()
                .OrderByDescending(entry => entry.IsLegacyDefault)
                .ThenByDescending(entry => entry.LastWriteTimeUtc)
                .FirstOrDefault(entry => entry.IsCompatible);
        }

        internal SaveGameEntry Find(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            return File.Exists(fullPath) ? Inspect(fullPath) : null;
        }

        private static SaveGameEntry Inspect(string filePath)
        {
            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
            SaveGameMetadata metadata = SaveGameMetadataStore.TryRead(filePath);
            SaveGameKind kind = GetKind(filePath, metadata);
            string displayName = GetDisplayName(filePath, kind, metadata);
            string campaignName = metadata?.CampaignName;
            Date campaignDate = null;

            try
            {
                string connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = filePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString();

                using SqliteConnection connection = new(connectionString);
                connection.Open();

                int? version = ReadSaveVersion(connection);
                if (!version.HasValue)
                {
                    return Invalid(filePath, displayName, campaignName, campaignDate,
                        lastWriteTimeUtc, kind, null, SaveGameCompatibility.Corrupt,
                        "The save contains no campaign metadata.");
                }

                // These fields were already present in format version 1. Read them separately so
                // a minimal/incomplete metadata database can still receive the precise version
                // compatibility result instead of a misleading generic SQLite error.
                campaignDate = TryReadCampaignDate(connection);
                campaignName = TryReadCampaignName(connection) ?? campaignName;

                if (version.Value != SaveFormat.CurrentVersion)
                {
                    return Invalid(filePath, displayName, campaignName, campaignDate,
                        lastWriteTimeUtc, kind, version, SaveGameCompatibility.Incompatible,
                        $"Save version {version} is not supported by this build "
                        + $"(expected {SaveFormat.CurrentVersion}).");
                }

                if (campaignDate == null)
                {
                    return Invalid(filePath, displayName, campaignName, null,
                        lastWriteTimeUtc, kind, version, SaveGameCompatibility.Corrupt,
                        "The save contains no campaign date.");
                }

                return new SaveGameEntry(
                    filePath,
                    displayName,
                    campaignName,
                    campaignDate,
                    lastWriteTimeUtc,
                    kind,
                    version,
                    SaveGameCompatibility.Compatible,
                    null);
            }
            catch (Exception exception) when (
                exception is SqliteException or InvalidOperationException or FormatException
                or OverflowException or IOException or UnauthorizedAccessException)
            {
                return Invalid(filePath, displayName, campaignName, campaignDate,
                    lastWriteTimeUtc, kind, null, SaveGameCompatibility.Corrupt,
                    $"The save could not be read: {exception.Message}");
            }
        }

        private static int? ReadSaveVersion(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT SaveVersion FROM GlobalData LIMIT 1";
            object result = command.ExecuteScalar();
            return result == null || result is DBNull ? null : Convert.ToInt32(result);
        }

        private static Date TryReadCampaignDate(SqliteConnection connection)
        {
            try
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT Millenium, Year, Week FROM GlobalData LIMIT 1";
                using SqliteDataReader reader = command.ExecuteReader();
                return reader.Read()
                    ? new Date(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2))
                    : null;
            }
            catch (SqliteException)
            {
                return null;
            }
        }

        private static string TryReadCampaignName(SqliteConnection connection)
        {
            try
            {
                // PlayerSoldier is the durable marker for the Chapter's faction. From any active
                // battle-brother, find that faction's root unit (the Chapter name). A brand-new or
                // unusual save without an active brother simply falls back to its sidecar label.
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT root.Name
                    FROM Unit AS root
                    WHERE root.ParentUnitId IS NULL
                      AND root.FactionId = (
                          SELECT memberUnit.FactionId
                          FROM PlayerSoldier AS player
                          JOIN Soldier AS soldier ON soldier.Id = player.SoldierId
                          JOIN Squad AS squad ON squad.Id = soldier.SquadId
                          JOIN Unit AS memberUnit ON memberUnit.Id = squad.ParentUnitId
                          LIMIT 1)
                    LIMIT 1";
                object result = command.ExecuteScalar();
                return result == null || result is DBNull ? null : Convert.ToString(result);
            }
            catch (SqliteException)
            {
                return null;
            }
        }

        private static SaveGameKind GetKind(string filePath, SaveGameMetadata metadata)
        {
            string fileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, GameStorage.ProtectedPreTurnSaveFileName,
                              StringComparison.OrdinalIgnoreCase))
            {
                return SaveGameKind.ProtectedPreTurn;
            }

            if (string.Equals(fileName, GameStorage.InitialAutosaveFileName,
                              StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith(GameStorage.PostTurnAutosaveFilePrefix,
                                       StringComparison.OrdinalIgnoreCase))
            {
                return SaveGameKind.Autosave;
            }

            return metadata?.Kind ?? SaveGameKind.Manual;
        }

        private static string GetDisplayName(
            string filePath,
            SaveGameKind kind,
            SaveGameMetadata metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata?.DisplayName))
            {
                return metadata.DisplayName;
            }

            string fileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, GameStorage.DefaultSaveFileName,
                              StringComparison.OrdinalIgnoreCase))
            {
                return "Legacy Save";
            }

            if (kind == SaveGameKind.ProtectedPreTurn)
            {
                return "Before Last Turn";
            }

            if (string.Equals(fileName, GameStorage.InitialAutosaveFileName,
                              StringComparison.OrdinalIgnoreCase))
            {
                return "Campaign Start";
            }

            if (kind == SaveGameKind.Autosave)
            {
                return "End of Turn";
            }

            return Path.GetFileNameWithoutExtension(filePath)
                .Replace('-', ' ')
                .Replace('_', ' ');
        }

        private static SaveGameEntry Invalid(
            string filePath,
            string displayName,
            string campaignName,
            Date campaignDate,
            DateTime lastWriteTimeUtc,
            SaveGameKind kind,
            int? saveVersion,
            SaveGameCompatibility compatibility,
            string reason)
        {
            return new SaveGameEntry(filePath, displayName, campaignName, campaignDate,
                lastWriteTimeUtc, kind, saveVersion, compatibility, reason);
        }
    }
}
