using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers
{
    // Pure string-building for the end-of-turn mission report (PRD 4.13/4.19). Kept free of Godot
    // types so it can be exercised directly by xunit tests - EndOfTurnDialogController itself is a
    // Godot partial class and can't be instantiated headlessly. The underlying outcome classification
    // is shared with the career-log recorder via MissionOutcomeClassifier (a MissionOutcomeClassification
    // built from MissionContext's structured signals); this builder only renders it in the commander's
    // second-person "Your forces ..." voice, so the two consumers can never silently disagree.
    public static class MissionReportSummaryBuilder
    {
        public static string BuildOutcomeStatus(MissionOutcomeClassification classification)
        {
            if (classification.Disposition is MissionForceDisposition.LostContact
                or MissionForceDisposition.WithdrewUnderFire
                or MissionForceDisposition.AbortedBeforeObjective)
                return "MISSION FAILED";

            return classification.MissionType switch
            {
                MissionType.Assassination => classification.TargetEliminated ? "MISSION SUCCESSFUL" : "MISSION FAILED",
                MissionType.Sabotage or MissionType.Diversion => classification.Impact > 0 ? "MISSION SUCCESSFUL" : "MISSION FAILED",
                MissionType.Recon or MissionType.Patrol => classification.Impact > 0 || !classification.WasDetected
                    ? "MISSION SUCCESSFUL" : "MISSION INCONCLUSIVE",
                MissionType.LightningRaid or MissionType.HitAndRun or MissionType.Advance
                    or MissionType.DeepStrike or MissionType.EstablishAirhead or MissionType.ObjectiveRaid
                    or MissionType.Ambush or MissionType.Extermination or MissionType.CloseAirSupport
                    => classification.EnemiesKilled > 0 ? "MISSION SUCCESSFUL"
                        : classification.NoViableTarget ? "MISSION INCONCLUSIVE" : "MISSION FAILED",
                _ => "MISSION COMPLETE"
            };
        }

        // The player's own missions are the only ones this builder renders (NPC missions go through
        // NpcMissionReportBuilder instead), so the subject is always the second-person "Your forces"
        // voice per 4.19.
        public static string BuildSubject() => "Your forces";

        public static string BuildSummary(
            MissionOutcomeClassification classification,
            string location)
        {
            string subject = BuildSubject();
            location = string.IsNullOrWhiteSpace(location) ? "an unknown location" : location;

            switch (classification.MissionType)
            {
                case MissionType.Recon:
                case MissionType.Patrol:
                    return BuildReconSummary(subject, location, classification);

                case MissionType.Assassination:
                    return BuildAssassinationSummary(subject, location, classification);

                case MissionType.Sabotage:
                    return BuildSabotageSummary(subject, location, classification);

                case MissionType.Diversion:
                    return classification.Impact > 0
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
                    return BuildCombatSummary(subject, location, classification);

                case MissionType.Fortify:
                case MissionType.DefenseInDepth:
                case MissionType.LastStand:
                case MissionType.Training:
                case MissionType.Construction:
                    return $"{subject} carried out {classification.MissionType} operations in {location}.";

                default:
                    return $"{subject} conducted a {classification.MissionType} in {location}.";
            }
        }

        private static string BuildReconSummary(
            string subject, string location, MissionOutcomeClassification classification)
        {
            if (!classification.WasDetected)
            {
                return $"{subject} reconnoitered {location} undetected.";
            }

            if (classification.Disposition == MissionForceDisposition.LostContact)
            {
                return $"{subject} were detected in {location} and lost contact with base.";
            }
            if (classification.Disposition == MissionForceDisposition.BrokeContact)
            {
                return $"{subject} were detected in {location} but broke contact successfully.";
            }
            return $"{subject} were detected in {location}.";
        }

        private static string BuildAssassinationSummary(
            string subject, string location, MissionOutcomeClassification classification)
        {
            if (classification.TargetEliminated)
            {
                return $"{subject} eliminated the target in {location}.";
            }
            if (classification.TargetLocated)
            {
                return $"{subject} located the target in {location}, but the mission did not conclude cleanly.";
            }
            return $"{subject} conducted an assassination attempt in {location}.";
        }

        // Names the works the charges went on and how badly they were hurt, so the debrief says more
        // than that "operations" were sabotaged. The damage comes from aftermath rather than from
        // Impact - the raid's effect is capped by the mission's size and then by whatever of the
        // position was still standing - so a success that found nothing left to wreck reads as one.
        //
        // Reported against the same fuzzy bands the region screens show enemy defenses in, not as a
        // raw level: a strike that leaves the position in the band it started in "weakened" it,
        // while one that drops it a band did "substantial" damage and is worth naming the new band
        // for. The player never sees an exact enemy figure here that the region screen would deny
        // them, and the two readouts move together.
        private static string BuildSabotageSummary(
            string subject,
            string location,
            MissionOutcomeClassification classification)
        {
            string target = classification.SabotageTarget is DefenseType defenseType
                ? $"enemy {DefenseTypeNames.Prose(defenseType)}"
                : "enemy operations";

            if (classification.Impact <= 0)
            {
                return $"{subject} attempted to sabotage {target} in {location} without notable effect.";
            }
            if (classification.SabotageDamage <= 0)
            {
                return $"{subject} sabotaged {target} in {location}, finding little left to destroy.";
            }

            double levelAfter = classification.SabotageLevelBefore - classification.SabotageDamage;
            string bandBefore = RegionFactionExtensions.GetDefenseLevelDescription(
                classification.SabotageLevelBefore);
            string bandAfter = RegionFactionExtensions.GetDefenseLevelDescription(levelAfter);

            if (bandAfter == bandBefore)
            {
                return $"{subject} sabotaged {target} in {location}, weakening them.";
            }
            if (bandAfter == NoDefensesBand)
            {
                return $"{subject} sabotaged {target} in {location}, destroying them outright.";
            }
            return $"{subject} sabotaged {target} in {location}, inflicting substantial damage - "
                + $"the position now reads {bandAfter}.";
        }

        // What GetDefenseLevelDescription calls a position that has been flattened. Reported as
        // destruction rather than as a band, since "now reads None" is not how a debrief speaks.
        private const string NoDefensesBand = "None";

        private static string BuildCombatSummary(
            string subject,
            string location,
            MissionOutcomeClassification classification)
        {
            if (classification.EnemiesKilled > 0)
            {
                if (classification.Disposition == MissionForceDisposition.WithdrewUnderFire)
                {
                    return $"{subject} killed {classification.EnemiesKilled} enemy troops in {location} before being forced to withdraw with heavy losses.";
                }
                if (classification.RemainedInTargetRegion)
                {
                    return $"{subject} killed {classification.EnemiesKilled} enemy troops in {location} and remained deployed there.";
                }
                bool returned = classification.ReturnedToBase
                    || classification.Disposition == MissionForceDisposition.BrokeContact;
                return returned
                    ? $"{subject} killed {classification.EnemiesKilled} enemy troops in {location} and returned to base."
                    : $"{subject} killed {classification.EnemiesKilled} enemy troops in {location}.";
            }

            if (classification.NoViableTarget)
            {
                if (classification.RemainedInTargetRegion)
                {
                    return $"{subject} found no viable target in {location} and remained deployed there.";
                }
                return classification.ReturnedToBase
                    ? $"{subject} found no viable target in {location} and returned to base."
                    : $"{subject} found no viable target in {location}.";
            }
            return $"{subject} conducted a {classification.MissionType} in {location} without confirmed enemy casualties.";
        }
    }
}
