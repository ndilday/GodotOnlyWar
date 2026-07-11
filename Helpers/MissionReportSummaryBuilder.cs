using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    // Pure string-building for the end-of-turn mission report (PRD 4.13/4.19). Kept free of Godot
    // types so it can be exercised directly by xunit tests - EndOfTurnDialogController itself is a
    // Godot partial class and can't be instantiated headlessly. Classification is deliberately built
    // only from signals MissionContext already exposes (EnemiesKilled, DaysElapsed, Impact, Spotter,
    // Log) rather than any richer per-mission-type outcome data, since that may or may not land from a
    // sibling work stream.
    public static class MissionReportSummaryBuilder
    {
        // The subject phrase used to open the outcome sentence: "Your forces" for the player's own
        // mission (per 4.19 second-person voice), the faction's name otherwise.
        public static string BuildSubject(bool isPlayerFaction, string actingFactionName)
        {
            if (isPlayerFaction)
            {
                return "Your forces";
            }
            return string.IsNullOrWhiteSpace(actingFactionName) ? "An unknown force" : actingFactionName;
        }

        // The unconfirmed variant shown for NPC-vs-NPC (or NPC-vs-third-party) missions the player has
        // no region intel on - mirrors BuildStrategicCombatEntry's hasIntel gating so no precise outcome
        // leaks through.
        public static string BuildUnconfirmedSummary(MissionType missionType, string location)
        {
            return $"Unconfirmed reports place a {missionType} action near {location}.";
        }

        public static string BuildUnconfirmedSubtitle(MissionType missionType, string location)
        {
            return $"Unconfirmed {missionType} - {location}";
        }

        public static string BuildSummary(
            MissionType missionType,
            bool isPlayerFaction,
            string actingFactionName,
            string location,
            int enemiesKilled,
            ushort daysElapsed,
            float impact,
            bool wasDetected,
            IReadOnlyList<string> log)
        {
            log ??= Array.Empty<string>();
            string subject = BuildSubject(isPlayerFaction, actingFactionName);
            location = string.IsNullOrWhiteSpace(location) ? "an unknown location" : location;

            switch (missionType)
            {
                case MissionType.Recon:
                case MissionType.Patrol:
                    return BuildReconSummary(subject, location, wasDetected, log);

                case MissionType.Assassination:
                    return BuildAssassinationSummary(subject, location, impact, log);

                case MissionType.Sabotage:
                    return impact > 0
                        ? $"{subject} sabotaged enemy operations in {location}."
                        : $"{subject} attempted sabotage in {location} without notable effect.";

                case MissionType.Diversion:
                    return impact > 0
                        ? $"{subject} staged a diversion in {location}, drawing enemy attention."
                        : $"{subject} attempted a diversion in {location} with limited effect.";

                case MissionType.LightningRaid:
                case MissionType.HitAndRun:
                case MissionType.Advance:
                case MissionType.DeepStrike:
                case MissionType.EstablishAirhead:
                case MissionType.ObjectiveRaid:
                case MissionType.Ambush:
                case MissionType.Extermination:
                case MissionType.Infiltrate:
                case MissionType.CloseAirSupport:
                    return BuildCombatSummary(subject, missionType, location, enemiesKilled, log);

                case MissionType.Fortify:
                case MissionType.DefenseInDepth:
                case MissionType.LastStand:
                case MissionType.Training:
                case MissionType.Construction:
                    return $"{subject} carried out {missionType} operations in {location}.";

                default:
                    return $"{subject} conducted a {missionType} in {location}.";
            }
        }

        private static string BuildReconSummary(
            string subject, string location, bool wasDetected, IReadOnlyList<string> log)
        {
            if (!wasDetected)
            {
                return $"{subject} reconnoitered {location} undetected.";
            }

            if (ContainsAny(log, "assumed dead", "gone to ground"))
            {
                return $"{subject} were detected in {location} and lost contact with base.";
            }
            if (ContainsAny(log, "successfully escaped", "returned to base"))
            {
                return $"{subject} were detected in {location} but broke contact successfully.";
            }
            return $"{subject} were detected in {location}.";
        }

        private static string BuildAssassinationSummary(
            string subject, string location, float impact, IReadOnlyList<string> log)
        {
            bool locatedTarget = ContainsAny(log, "located the assassination target");
            if (locatedTarget && impact > 0)
            {
                return $"{subject} eliminated the target in {location}.";
            }
            if (locatedTarget)
            {
                return $"{subject} located the target in {location}, but the mission did not conclude cleanly.";
            }
            return $"{subject} conducted an assassination attempt in {location}.";
        }

        private static string BuildCombatSummary(
            string subject, MissionType missionType, string location, int enemiesKilled, IReadOnlyList<string> log)
        {
            if (enemiesKilled > 0)
            {
                if (ContainsAny(log, "combat-ineffective"))
                {
                    return $"{subject} killed {enemiesKilled} enemy troops in {location} before being forced to withdraw with heavy losses.";
                }
                bool withdrew = ContainsAny(log, "returned to base");
                return withdrew
                    ? $"{subject} killed {enemiesKilled} enemy troops in {location} and withdrew."
                    : $"{subject} killed {enemiesKilled} enemy troops in {location}.";
            }

            if (ContainsAny(log, "no isolated force", "no military target", "no combat-capable forces"))
            {
                return $"{subject} found no viable target in {location}.";
            }
            return $"{subject} conducted a {missionType} in {location} without confirmed enemy casualties.";
        }

        private static bool ContainsAny(IReadOnlyList<string> log, params string[] needles)
        {
            for (int i = 0; i < log.Count; i++)
            {
                string line = log[i];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                for (int j = 0; j < needles.Length; j++)
                {
                    if (line.IndexOf(needles[j], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
