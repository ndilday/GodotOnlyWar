using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Storage
{
    internal sealed record SaveGameEntry(
        string FilePath,
        DateTime LastWriteTimeUtc,
        int? SaveVersion,
        bool IsCompatible,
        string FailureReason);

    /// <summary>
    /// Discovers save files without constructing an entire sector. This keeps the start menu
    /// responsive and lets it distinguish missing, corrupt, and incompatible saves.
    /// </summary>
    internal sealed class SaveGameCatalog
    {
        private readonly string _saveDirectory;

        internal SaveGameCatalog(string saveDirectory)
        {
            _saveDirectory = saveDirectory ?? throw new ArgumentNullException(nameof(saveDirectory));
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
                .OrderByDescending(entry =>
                    string.Equals(Path.GetFileName(entry.FilePath), GameStorage.DefaultSaveFileName,
                                  StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(entry => entry.LastWriteTimeUtc)
                .ToList();
        }

        internal SaveGameEntry FindPreferredCompatibleSave()
        {
            return Discover().FirstOrDefault(entry => entry.IsCompatible);
        }

        private static SaveGameEntry Inspect(string filePath)
        {
            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
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
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT SaveVersion FROM GlobalData LIMIT 1";
                object result = command.ExecuteScalar();
                if (result == null || result is DBNull)
                {
                    return Invalid(filePath, lastWriteTimeUtc, null,
                        "The save contains no campaign metadata.");
                }

                int version = Convert.ToInt32(result);
                if (version != SaveFormat.CurrentVersion)
                {
                    return Invalid(filePath, lastWriteTimeUtc, version,
                        $"Save version {version} is not supported by this build "
                        + $"(expected {SaveFormat.CurrentVersion}).");
                }

                return new SaveGameEntry(filePath, lastWriteTimeUtc, version, true, null);
            }
            catch (Exception exception) when (
                exception is SqliteException or InvalidOperationException or FormatException
                or OverflowException or IOException or UnauthorizedAccessException)
            {
                return Invalid(filePath, lastWriteTimeUtc, null,
                    $"The save could not be read: {exception.Message}");
            }
        }

        private static SaveGameEntry Invalid(
            string filePath,
            DateTime lastWriteTimeUtc,
            int? saveVersion,
            string reason)
        {
            return new SaveGameEntry(filePath, lastWriteTimeUtc, saveVersion, false, reason);
        }
    }
}
