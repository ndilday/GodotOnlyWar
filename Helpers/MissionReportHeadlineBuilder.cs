using OnlyWar.Models.Missions;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // Pure string-building for the primary line of an end-of-turn mission report. Keeping this
    // separate from the Godot controller makes the player/enemy and recon/mission distinctions easy
    // to exercise without instantiating the UI scene.
    public static class MissionReportHeadlineBuilder
    {
        public static string Build(
            MissionType missionType,
            bool isPlayerMission,
            IReadOnlyList<string> squadNames,
            string actingFactionName,
            string enemyFactionName,
            string regionName,
            string planetName)
        {
            string missionName = missionType.ToString();
            string region = ValueOrFallback(regionName, "Unknown region");
            string planet = ValueOrFallback(planetName, "Unknown planet");
            string location = $"{region}, {planet}";

            if (!isPlayerMission)
            {
                return $"{ValueOrFallback(actingFactionName, "Unknown faction")} {missionName} in {location}";
            }

            int squadCount = squadNames?.Count ?? 0;
            string squadName = squadNames?
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            squadName = ValueOrFallback(squadName, "Mission force");

            if (missionType is MissionType.Recon or MissionType.Patrol)
            {
                return squadCount <= 1
                    ? $"{squadName} Recon {region} {planet}"
                    : $"{squadCount} squads Recon {location}";
            }

            string enemyFaction = ValueOrFallback(enemyFactionName, "Unknown enemy faction");
            return squadCount <= 1
                ? $"{squadName} {missionName} on {enemyFaction} in {location}"
                : $"{squadCount} squads {missionName} on {enemyFaction} in {location}";
        }

        private static string ValueOrFallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
