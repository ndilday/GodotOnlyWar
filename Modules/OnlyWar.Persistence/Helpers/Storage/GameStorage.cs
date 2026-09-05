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
    public static class GameStorage
    {
        public const string DefaultSaveFileName = "default.s3db";
        public const string ProtectedPreTurnSaveFileName = "autosave-pre-turn.s3db";
        public const string InitialAutosaveFileName = "autosave-initial.s3db";
        public const string PostTurnAutosaveFilePrefix = "autosave-turn-";

        private static readonly Lazy<string> InstallDirectoryValue =
            new(LocateInstallDirectory);

        public static string InstallDirectory => InstallDirectoryValue.Value;

        public static string RulesDatabasePath =>
            Path.Combine(InstallDirectory, "Database", "OnlyWar.s3db");

        public static string SaveSchemaPath =>
            Path.Combine(InstallDirectory, "Database", "SaveStructure.sql");

        private static string _saveDirectory;

        public static void ConfigureSaveDirectory(string saveDirectory) =>
            _saveDirectory = Path.GetFullPath(saveDirectory);

        public static string SaveDirectory => _saveDirectory
            ?? throw new InvalidOperationException("The host must configure the save directory before saving.");

        public static string DefaultSavePath =>
            Path.Combine(SaveDirectory, DefaultSaveFileName);

        /// <summary>
        /// Creates the writable save directory and adopts the old repository/install-root
        /// default.s3db once. The legacy file is copied, not moved, so an interrupted migration
        /// cannot destroy the player's previous save.
        /// </summary>
        public static void InitializeUserStorage()
        {
            Directory.CreateDirectory(SaveDirectory);
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
