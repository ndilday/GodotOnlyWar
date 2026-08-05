using OnlyWar.Models;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// Builds the player-facing identity for intelligence-discovered missions. The mission type
    /// alone is not unique: a region can contain several opportunities of the same type, possibly
    /// against the same faction. Context distinguishes most rows; the persisted mission id supplies
    /// a stable intelligence reference only when the contextual labels would still collide.
    /// </summary>
    public static class SpecialMissionPresentation
    {
        public static IReadOnlyDictionary<int, string> BuildLabels(Region region)
        {
            List<Mission> missions = region?.SpecialMissions?
                .Where(mission => mission != null)
                .ToList() ?? [];
            Dictionary<int, string> baseLabels = missions.ToDictionary(
                mission => mission.Id,
                mission => BuildBaseLabel(mission, region));
            HashSet<int> collidingIds = baseLabels
                .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(pair => pair.Key))
                .ToHashSet();

            return baseLabels.ToDictionary(
                pair => pair.Key,
                pair => collidingIds.Contains(pair.Key)
                    ? $"{pair.Value} · Intel M-{pair.Key}"
                    : pair.Value);
        }

        public static string Format(Mission mission, Region region)
        {
            if (mission == null) return "Special Mission";

            IReadOnlyDictionary<int, string> labels = BuildLabels(region);
            return labels.TryGetValue(mission.Id, out string label)
                ? label
                : BuildBaseLabel(mission, region);
        }

        private static string BuildBaseLabel(Mission mission, Region region)
        {
            if (mission.MissionType == MissionType.ShowOfForce)
            {
                Character governor = region?.Planet?.GetControllingFaction() is Faction controlling
                    && region.Planet.PlanetFactionMap.TryGetValue(
                        controlling.Id, out PlanetFaction planetFaction)
                        ? planetFaction.Leader
                        : null;
                return governor == null
                    ? "Show of Force — Governor's Request"
                    : $"Show of Force — Governor {governor.Name}'s Request";
            }

            string type = Humanize(mission.MissionType.ToString());
            RegionFaction target = mission.RegionFaction;
            string targetName = target?.IsPublic == true
                ? target.PlanetFaction?.Faction?.Name ?? "Unknown target"
                : "Unknown cell";
            string label = $"{type} — {targetName}";

            if (mission is SabotageMission sabotage)
            {
                label += $" · {DefenseTypeNames.Label(sabotage.DefenseType)}";
            }

            return label;
        }

        private static string Humanize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            StringBuilder result = new(value.Length + 4);
            result.Append(value[0]);
            for (int index = 1; index < value.Length; index++)
            {
                if (char.IsUpper(value[index]) && !char.IsWhiteSpace(value[index - 1]))
                {
                    result.Append(' ');
                }
                result.Append(value[index]);
            }
            return result.ToString();
        }
    }
}
