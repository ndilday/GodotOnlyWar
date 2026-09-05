using System;
using System.IO;
using System.Text.Json;

namespace OnlyWar.Helpers.Storage
{
    internal sealed class SaveGameMetadata
    {
        public string DisplayName { get; set; }
        public string CampaignName { get; set; }
        public SaveGameKind Kind { get; set; }
    }

    /// <summary>
    /// Stores chooser-only labels alongside a save. Simulation data and compatibility continue to
    /// live exclusively in the version-1 SQLite file; losing a sidecar never makes a save unloadable.
    /// </summary>
    internal static class SaveGameMetadataStore
    {
        private const string SidecarSuffix = ".meta.json";

        internal static string GetPath(string savePath)
        {
            return Path.GetFullPath(savePath) + SidecarSuffix;
        }

        internal static SaveGameMetadata TryRead(string savePath)
        {
            string metadataPath = GetPath(savePath);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<SaveGameMetadata>(
                    File.ReadAllText(metadataPath));
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        internal static void Write(string savePath, SaveGameMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            string metadataPath = GetPath(savePath);
            string directory = Path.GetDirectoryName(metadataPath);
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(metadataPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(metadata));
                File.Move(tempPath, metadataPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
