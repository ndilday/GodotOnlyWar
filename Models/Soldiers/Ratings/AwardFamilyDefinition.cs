using System;
using System.Collections.Generic;

namespace OnlyWar.Models.Soldiers.Ratings
{
    /// <summary>
    /// Data-owned identity and presentation metadata for an award family. The
    /// icon key is logical content identity; it is resolved by the UI asset layer.
    /// </summary>
    public sealed class AwardFamilyDefinition
    {
        public string Key { get; }
        public string DisplayName { get; }
        public string IconAssetKey { get; }
        public int SortOrder { get; }
        public string SummaryGroup { get; }
        public string StackingGroup { get; }

        public AwardFamilyDefinition(string key, string displayName, string iconAssetKey,
                                     int sortOrder, string summaryGroup, string stackingGroup)
        {
            Key = key;
            DisplayName = displayName;
            IconAssetKey = iconAssetKey;
            SortOrder = sortOrder;
            SummaryGroup = summaryGroup;
            StackingGroup = string.IsNullOrWhiteSpace(stackingGroup) ? key : stackingGroup;
        }
    }

    /// <summary>
    /// Lookup of award presentation metadata with a safe generic fallback for
    /// awards whose optional art has not been installed.
    /// </summary>
    public sealed class AwardFamilyCatalog
    {
        private readonly IReadOnlyDictionary<string, AwardFamilyDefinition> _families;

        public AwardFamilyCatalog(IEnumerable<AwardFamilyDefinition> families)
        {
            Dictionary<string, AwardFamilyDefinition> map =
                new(StringComparer.Ordinal);
            foreach (AwardFamilyDefinition family in families ?? [])
            {
                if (string.IsNullOrWhiteSpace(family?.Key))
                {
                    throw new InvalidOperationException("An award family has no key.");
                }
                if (!map.TryAdd(family.Key, family))
                {
                    throw new InvalidOperationException(
                        $"Award family '{family.Key}' is defined more than once.");
                }
            }
            _families = map;
        }

        public IReadOnlyDictionary<string, AwardFamilyDefinition> Families => _families;

        public AwardFamilyDefinition Get(string key)
        {
            if (!string.IsNullOrWhiteSpace(key)
                && _families.TryGetValue(key, out AwardFamilyDefinition family))
            {
                return family;
            }
            return new AwardFamilyDefinition(
                key ?? "unknown_award",
                key ?? "Award",
                "award",
                int.MaxValue,
                null,
                key ?? "unknown_award");
        }

        public bool TryGet(string key, out AwardFamilyDefinition family) =>
            _families.TryGetValue(key, out family);

        public static AwardFamilyCatalog CreateDefault() => new(
            [
                new AwardFamilyDefinition(AwardTypes.Gun, "Gun", "core:honor_gun", 0, "combat", "gun"),
                new AwardFamilyDefinition(AwardTypes.Sword, "Sword", "core:honor_sword", 1, "combat", "sword"),
                new AwardFamilyDefinition(AwardTypes.Voice, "Voice", "core:honor_voice", 2, "command", "voice"),
                new AwardFamilyDefinition(AwardTypes.Banner, "Banner", "core:honor_banner", 3, "command", "banner")
            ]);
    }
}
