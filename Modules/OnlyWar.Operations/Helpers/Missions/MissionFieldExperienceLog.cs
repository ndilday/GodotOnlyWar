using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    // Mission-specific façade over SoldierProgressLog: answers "what did a squad get for its week of
    // work?" by diffing each participating PlayerSoldier's skills/attributes across the whole mission.
    // Because it diffs the whole mission window it also captures any growth from combat embedded in
    // the mission (a recon that got detected and had to fight); for a clean, uncontested recon the
    // diff is exactly the field-XP award (PRD §4.12). All the snapshot/format work — and the Debug
    // gating that makes it zero-overhead when logging is off — lives in SoldierProgressLog.
    public static class MissionFieldExperienceLog
    {
        public static SoldierProgressLog.ProgressSnapshot Snapshot(
            IEnumerable<PlayerSoldier> participants) =>
            SoldierProgressLog.Capture(participants);

        public static void LogGains(MissionContext context, SoldierProgressLog.ProgressSnapshot before)
        {
            if (before == null || context == null)
            {
                return;
            }
            IReadOnlyList<PlayerSoldier> participants = context.StartingPlayerParticipants;
            SoldierProgressLog.LogDelta(
                BuildHeader(context, participants?.Count ?? 0), participants, before);
        }

        private static string BuildHeader(MissionContext context, int soldierCount)
        {
            string missionType = context.Order?.Mission?.MissionType.ToString() ?? "Mission";
            string region = context.Order?.Mission?.RegionFaction?.Region?.Name ?? "unknown region";
            string squads = string.Join("/", context.MissionSquads
                .Select(battleSquad => battleSquad.Squad?.Name
                    ?? battleSquad.CampaignCharacter?.Name)
                .Where(name => !string.IsNullOrEmpty(name)));
            if (string.IsNullOrEmpty(squads))
            {
                squads = $"{soldierCount} soldier(s)";
            }
            return $"Field XP [{missionType} @ {region}, {context.DaysElapsed}d] {squads} "
                + $"({soldierCount} soldier(s))";
        }
    }
}
