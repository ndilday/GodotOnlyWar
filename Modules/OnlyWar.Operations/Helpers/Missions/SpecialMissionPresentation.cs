using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Fortifications;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OnlyWar.Operations.Missions.Ambush;
using OnlyWar.Operations.Planetary;

namespace OnlyWar.Operations.Missions
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

        public static string GetMissionTypeLabel(MissionType missionType) =>
            missionType == MissionType.Extermination
                ? "Ambush Hidden Cell"
                : Humanize(missionType.ToString());

        /// <summary>
        /// The "Recommended force" line. The squad count is the target measured in full-strength
        /// reference squads (see <see cref="AmbushMissionSizing.RecommendSquads"/>), a general
        /// statement that does not depend on which squads are in the region; how precise the line
        /// is allowed to be depends only on how well the target is known.
        /// </summary>
        public static string FormatRecommendedForce(
            Mission mission,
            int currentWeek,
            long referenceSquadBattleValue)
        {
            if (mission == null) return null;
            FactionIntelBelief belief = IntelligenceTargetService.GetBestPlayerVisibleBelief(
                mission.RegionFaction?.Region,
                mission.RegionFaction?.PlanetFaction?.Faction);
            IntelLevel level = belief?.Level ?? IntelLevel.Rumor;
            // Only ambushes store a target. For any other special mission the estimate is taken
            // against what the enemy has deployed; with no presence there is nothing to size.
            long target = mission.TargetBattleValue
                ?? AmbushMissionSizing.EstimateTargetBattleValue(
                    mission.RegionFaction?.GetDeployedStrength() ?? 0L);
            if (target <= 0) return null;
            long squads = AmbushMissionSizing.RecommendSquads(target, referenceSquadBattleValue);
            string unit = squads == 1 ? "squad" : "squads";
            bool imperial = FactionRelationshipService.IsImperial(
                mission.RegionFaction?.PlanetFaction?.Faction);

            if (imperial)
            {
                return $"Recommended force: {squads:N0} {unit}";
            }
            return level switch
            {
                IntelLevel.Located => $"Recommended force: {squads:N0} {unit}",
                IntelLevel.Confirmed => $"Recommended force: {squads:N0}–{squads + 1:N0} squads",
                IntelLevel.Suspected => $"Recommended force: roughly {Math.Max(1, squads / 2):N0}–{Math.Max(2, squads * 2):N0} squads",
                _ => "Recommended force: strength too uncertain; recon advised"
            };
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

            string type = GetMissionTypeLabel(mission.MissionType);
            RegionFaction target = mission.RegionFaction;
            string targetName = IsTargetIdentified(target)
                ? target.PlanetFaction?.Faction?.Name ?? "Unknown target"
                : "Unknown cell";
            string label = $"{type} — {targetName}";

            if (mission is SabotageMission sabotage)
            {
                label += $" · {DefenseTypeNames.Label(sabotage.DefenseType)}";
            }

            return label;
        }

        // A hidden cell is still attributable when the player already knows who is behind it:
        // the faction is openly active elsewhere on the planet (a cult cell that went to ground
        // after a revolt), or player intel on this region already names the faction.
        private static bool IsTargetIdentified(RegionFaction target)
        {
            if (target?.PlanetFaction?.Faction == null) return false;
            if (target.IsPublic || target.PlanetFaction.IsPublic) return true;
            FactionIntelBelief belief = IntelligenceTargetService.GetBestPlayerVisibleBelief(
                target.Region,
                target.PlanetFaction.Faction);
            return belief != null && belief.Level >= IntelLevel.Suspected;
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
