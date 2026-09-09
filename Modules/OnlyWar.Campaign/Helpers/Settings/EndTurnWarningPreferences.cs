using System;
using System.IO;
using System.Text.Json;

namespace OnlyWar.Campaign.Settings
{
    /// <summary>
    /// Persists global (not campaign-save) warning choices. Writes replace the small JSON file
    /// atomically so a terminated process cannot leave half a settings document behind.
    /// </summary>
    public sealed class EndTurnWarningPreferencesRepository : IEndTurnWarningPreferencesRepository
    {
        public const string DefaultUserPath = "user://settings/end_turn_warnings.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string PreferencesFilePath { get; }

        public EndTurnWarningPreferencesRepository(string preferencesFilePath)
        {
            if (string.IsNullOrWhiteSpace(preferencesFilePath))
            {
                throw new ArgumentException("A preferences file path is required.", nameof(preferencesFilePath));
            }

            PreferencesFilePath = Path.GetFullPath(preferencesFilePath);
        }

        public EndTurnWarningPreferences Load()
        {
            try
            {
                if (!File.Exists(PreferencesFilePath))
                {
                    return new EndTurnWarningPreferences();
                }

                string json = File.ReadAllText(PreferencesFilePath);
                return JsonSerializer.Deserialize<EndTurnWarningPreferences>(json, JsonOptions)
                    ?? new EndTurnWarningPreferences();
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException)
            {
                GameLog.Warn(() => $"Could not read End Turn warning preferences; defaults will be used: {exception.Message}");
                return new EndTurnWarningPreferences();
            }
        }

        public void Save(EndTurnWarningPreferences preferences)
        {
            if (preferences == null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            string directory = Path.GetDirectoryName(PreferencesFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = PreferencesFilePath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
                File.Move(temporaryPath, PreferencesFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
