using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Squads
{
    /// <summary>
    /// A player-selectable scout training choice. The key is the stable rules/save identity;
    /// DisplayName is presentation text and may be changed by a mod.
    /// </summary>
    public sealed class ScoutTrainingOption
    {
        public string Key { get; }
        public string DisplayName { get; }
        public TrainingProfile Profile { get; }
        public int SortOrder { get; }

        public ScoutTrainingOption(
            string key,
            string displayName,
            TrainingProfile profile,
            int sortOrder = 0)
        {
            Key = key;
            DisplayName = displayName;
            Profile = profile;
            SortOrder = sortOrder;
        }
    }

    public static class ScoutTrainingOptionKeys
    {
        public const string Balanced = "scout.balanced";
        public const string Physical = "scout.physical";
        public const string Vehicles = "scout.vehicles";
        public const string Melee = "scout.melee";
        public const string Ranged = "scout.ranged";
    }

    /// <summary>
    /// The validated catalog of scout-training choices exposed by the active rules database.
    /// </summary>
    public sealed class ScoutTrainingOptionCatalog
    {
        private readonly IReadOnlyDictionary<string, ScoutTrainingOption> _byKey;

        public IReadOnlyList<ScoutTrainingOption> Options { get; }

        public ScoutTrainingOptionCatalog(IEnumerable<ScoutTrainingOption> options)
        {
            Options = (options ?? throw new ArgumentNullException(nameof(options)))
                .OrderBy(option => option.SortOrder)
                .ThenBy(option => option.Key, StringComparer.Ordinal)
                .ToList();
            _byKey = Options.ToDictionary(option => option.Key, StringComparer.Ordinal);
        }

        public bool TryGet(string key, out ScoutTrainingOption option)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                option = null;
                return false;
            }
            return _byKey.TryGetValue(key, out option);
        }

        public ScoutTrainingOption GetRequired(string key)
        {
            if (!TryGet(key, out ScoutTrainingOption option))
            {
                throw new InvalidOperationException(
                    $"Scout training option '{key}' is not available in the active rules database.");
            }
            return option;
        }

        public ScoutTrainingOption Default => GetRequired(ScoutTrainingOptionKeys.Balanced);
    }
}
