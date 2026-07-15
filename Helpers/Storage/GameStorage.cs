using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Storage
{
    /// <summary>
    /// Owns the boundary between immutable installed game data and writable player data.
    /// SQLite requires ordinary filesystem paths, so the shipped rules database and save
    /// schema remain loose files under Database/ beside the exported executable.
    /// </summary>
    internal static class GameStorage
    {
        internal const string DefaultSaveFileName = "default.s3db";
        internal const string ProtectedPreTurnSaveFileName = "autosave-pre-turn.s3db";
        internal const string InitialAutosaveFileName = "autosave-initial.s3db";
        internal const string PostTurnAutosaveFilePrefix = "autosave-turn-";

        private static readonly Lazy<string> InstallDirectoryValue =
            new(LocateInstallDirectory);

        internal static string InstallDirectory => InstallDirectoryValue.Value;

        internal static string RulesDatabasePath =>
            Path.Combine(InstallDirectory, "Database", "OnlyWar.s3db");

        internal static string SaveSchemaPath =>
            Path.Combine(InstallDirectory, "Database", "SaveStructure.sql");

        internal static string SaveDirectory =>
            ProjectSettings.GlobalizePath("user://saves");

        internal static string DefaultSavePath =>
            Path.Combine(SaveDirectory, DefaultSaveFileName);

        /// <summary>
        /// Creates the writable save directory and adopts the old repository/install-root
        /// default.s3db once. The legacy file is copied, not moved, so an interrupted migration
        /// cannot destroy the player's previous save.
        /// </summary>
        internal static void InitializeUserStorage()
        {
            Directory.CreateDirectory(SaveDirectory);

            string[] legacyCandidates =
            {
                Path.Combine(InstallDirectory, DefaultSaveFileName),
                Path.Combine(Directory.GetCurrentDirectory(), DefaultSaveFileName)
            };

            try
            {
                MigrateLegacyDefaultSave(SaveDirectory, legacyCandidates);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"Could not migrate the legacy save: {exception.Message}");
            }
        }

        internal static bool MigrateLegacyDefaultSave(
            string saveDirectory,
            IEnumerable<string> legacyCandidates)
        {
            Directory.CreateDirectory(saveDirectory);
            string targetPath = Path.GetFullPath(Path.Combine(saveDirectory, DefaultSaveFileName));
            if (File.Exists(targetPath))
            {
                return false;
            }

            string legacyPath = legacyCandidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path =>
                    !string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path));

            if (legacyPath == null)
            {
                return false;
            }

            string tempPath = targetPath + ".migration.tmp";
            try
            {
                File.Copy(legacyPath, tempPath, overwrite: true);
                File.Move(tempPath, targetPath, overwrite: false);
                return true;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static string LocateInstallDirectory()
        {
            List<string> startingDirectories = [];

            string processPath = System.Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                startingDirectories.Add(Path.GetDirectoryName(processPath));
            }

            startingDirectories.Add(AppContext.BaseDirectory);
            startingDirectories.Add(Directory.GetCurrentDirectory());

            foreach (string startingDirectory in startingDirectories
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                DirectoryInfo directory = new(Path.GetFullPath(startingDirectory));
                while (directory != null)
                {
                    string rulesPath = Path.Combine(directory.FullName, "Database", "OnlyWar.s3db");
                    string schemaPath = Path.Combine(directory.FullName, "Database", "SaveStructure.sql");
                    if (File.Exists(rulesPath) && File.Exists(schemaPath))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not locate the installed Database directory containing "
                + "OnlyWar.s3db and SaveStructure.sql. Reinstall the complete game folder.");
        }
    }
}
