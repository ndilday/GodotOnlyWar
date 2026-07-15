using Godot;
using OnlyWar.Helpers.Turns;
using System;
using System.IO;
using System.Text.Json;

namespace OnlyWar.Helpers.Settings
{
    public sealed class EndTurnWarningPreferences
    {
        public bool WarnIdleDeployableSquads { get; set; } = true;
        public bool WarnActionableTaskForces { get; set; } = true;
        public bool WarnSpecialMissionOpportunities { get; set; } = true;

        public bool IsEnabled(EndTurnWarningCategory category)
        {
            return category switch
            {
                EndTurnWarningCategory.IdleDeployableSquads => WarnIdleDeployableSquads,
                EndTurnWarningCategory.ActionableTaskForces => WarnActionableTaskForces,
                EndTurnWarningCategory.SpecialMissionOpportunities => WarnSpecialMissionOpportunities,
                _ => true
            };
        }

        public void SetEnabled(EndTurnWarningCategory category, bool enabled)
        {
            switch (category)
            {
                case EndTurnWarningCategory.IdleDeployableSquads:
                    WarnIdleDeployableSquads = enabled;
                    break;
                case EndTurnWarningCategory.ActionableTaskForces:
                    WarnActionableTaskForces = enabled;
                    break;
                case EndTurnWarningCategory.SpecialMissionOpportunities:
                    WarnSpecialMissionOpportunities = enabled;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, null);
            }
        }

        public EndTurnWarningPreferences Clone()
        {
            return new EndTurnWarningPreferences
            {
                WarnIdleDeployableSquads = WarnIdleDeployableSquads,
                WarnActionableTaskForces = WarnActionableTaskForces,
                WarnSpecialMissionOpportunities = WarnSpecialMissionOpportunities
            };
        }
    }

    public interface IEndTurnWarningPreferencesRepository
    {
        EndTurnWarningPreferences Load();
        void Save(EndTurnWarningPreferences preferences);
    }

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

        public static EndTurnWarningPreferencesRepository CreateDefault()
        {
            return new EndTurnWarningPreferencesRepository(ProjectSettings.GlobalizePath(DefaultUserPath));
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
                GD.PushWarning($"Could not read End Turn warning preferences; defaults will be used: {exception.Message}");
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
