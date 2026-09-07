using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Missions;

namespace OnlyWar.Helpers
{
    // The tier of evidence the player has on an NPC-run mission, derived purely from the region's
    // player-visible intel (Region.GetPlayerVisibleIntel via RegionFactionExtensions - decays 0.75x/turn,
    // typical values 0-5). Higher tiers unlock more precise surveillance wording in BuildSurveillance;
    // the tiers do NOT gate the Contact/Aftermath channels, which fire on their own evidence (a sighting,
    // a visible effect) independent of ambient intel.
    public enum NpcReportTier
    {
        None,
        Movement,
        Identified,
        Assessment
    }

    // The end-of-turn entry content the controller renders for a single NPC report channel. Null (via
    // the builder's nullable return) means no entry should appear at all - not every NPC mission leaves
    // evidence the player could plausibly have collected.
    public sealed class NpcMissionReport
    {
        public string Title { get; }
        public string Subtitle { get; }
        public string Summary { get; }

        public NpcMissionReport(string title, string subtitle, string summary)
        {
            Title = title;
            Subtitle = subtitle;
            Summary = summary;
        }
    }

    // Builds end-of-turn report entries for missions run by non-player factions, from only the
    // evidence the player could plausibly have gathered (PRD "evidence-based enemy mission reporting").
    // Replaces the old binary gate in EndOfTurnDialogController (full detail above some intel threshold,
    // nothing at zero) with four independent channels, checked in priority order - ENGAGEMENT (the
    // player's own soldiers fought this mission's force directly - the strongest possible evidence,
    // and outranks everything else), CONTACT (a sighting during the mission), AFTERMATH (a visible
    // physical effect: a dead HQ officer, sabotage damage, casualties among player-side forces), and
    // SURVEILLANCE (ambient intel about activity in the region). The raw MissionType enum name - ground
    // truth the player has no way to know - must never appear in any string this builder returns; word
    // choice is deliberately vague below the top tier.
    //
    // Pure and Godot-free (no Godot types) so it can be exercised directly by xunit, same as
    // MissionReportSummaryBuilder - EndOfTurnDialogController is a Godot partial class and can't be
    // instantiated headlessly.
    public static class NpcMissionReportBuilder
    {
        // Intel thresholds, in the units GetPlayerVisibleIntel returns (decays 0.75x/turn, typical
        // range 0-5). Named here rather than inlined so the tier boundaries read as design constants.
        private const float IdentifiedIntelThreshold = 2f;
        private const float AssessmentIntelThreshold = 4f;

        public static NpcReportTier GetTier(float playerVisibleIntel)
        {
            if (playerVisibleIntel >= AssessmentIntelThreshold) return NpcReportTier.Assessment;
            if (playerVisibleIntel >= IdentifiedIntelThreshold) return NpcReportTier.Identified;
            if (playerVisibleIntel > 0f) return NpcReportTier.Movement;
            return NpcReportTier.None;
        }

        // spotterIsPlayerSide: does the sighting reach the player at all - true when whoever spotted the
        // mission (context.Spotter) belongs to the player's faction or the default (Imperial) faction,
        // since the default faction's sightings are assumed to reach Imperial command.
        // targetIsPlayerSide: is the mission's target (mission.RegionFaction) itself player or default -
        // an aftermath effect against Imperial assets is inherently visible to command regardless of
        // ambient regional intel.
        public static NpcMissionReport Build(
            MissionOutcomeClassification classification,
            bool spotterIsPlayerSide,
            bool targetIsPlayerSide,
            bool playerForcesEngaged,
            string actingFactionName,
            string defenderFactionName,
            string location,
            float playerVisibleIntel)
        {
            location = string.IsNullOrWhiteSpace(location) ? "an unknown location" : location;
            NpcReportTier tier = GetTier(playerVisibleIntel);

            return BuildEngagement(classification, playerForcesEngaged, actingFactionName, location)
                ?? BuildContact(classification, spotterIsPlayerSide, actingFactionName, location, tier)
                ?? BuildAftermath(classification, targetIsPlayerSide, defenderFactionName, location, tier)
                ?? BuildSurveillance(classification, actingFactionName, location, tier);
        }

        // The player's own soldiers fought this mission's force face-to-face - the strongest evidence
        // there is, so it outranks every other channel and fires regardless of ambient intel or
        // detection state. Identification is free here: you don't need spycraft to know who just shot
        // at you. Still never names the enemy's mission type - only that an attack happened.
        private static NpcMissionReport BuildEngagement(
            MissionOutcomeClassification classification,
            bool playerForcesEngaged,
            string actingFactionName,
            string location)
        {
            if (!playerForcesEngaged)
            {
                return null;
            }

            string faction = ValueOrFallback(actingFactionName, "An unidentified force");
            string summary = $"Your forces in {location} were attacked by {faction}.";
            if (classification.EnemiesKilled > 0)
            {
                summary += " Casualties were sustained.";
            }

            return new NpcMissionReport("Enemy Attack", $"Enemy attack - {location}", summary);
        }

        // A sighting during the mission itself. Never names the mission's purpose - only that a force
        // was seen, roughly who (once identified), and what became of it.
        private static NpcMissionReport BuildContact(
            MissionOutcomeClassification classification,
            bool spotterIsPlayerSide,
            string actingFactionName,
            string location,
            NpcReportTier tier)
        {
            if (!classification.WasDetected || !spotterIsPlayerSide)
            {
                return null;
            }

            string forceLabel = tier >= NpcReportTier.Identified
                ? ValueOrFallback(actingFactionName, "An unidentified force")
                : "An unidentified force";
            string summary = $"{forceLabel} was detected in {location}; its purpose is unknown.";

            string tail = classification.Disposition switch
            {
                MissionForceDisposition.BrokeContact => " It slipped away before it could be engaged.",
                MissionForceDisposition.LostContact => " It was destroyed or scattered.",
                MissionForceDisposition.WithdrewUnderFire => " It was driven off with heavy losses.",
                _ => ""
            };

            return new NpcMissionReport("Enemy Contact", $"Enemy activity - {location}", summary + tail);
        }

        // A physical effect left behind that the player would notice regardless of whether the
        // responsible force was ever spotted: a dead officer, sabotage damage, or casualties among
        // player/default-side forces. Fires only for successful missions with such an effect.
        private static NpcMissionReport BuildAftermath(
            MissionOutcomeClassification classification,
            bool targetIsPlayerSide,
            string defenderFactionName,
            string location,
            NpcReportTier tier)
        {
            bool aftermathVisible = targetIsPlayerSide || tier > NpcReportTier.None;
            if (!aftermathVisible)
            {
                return null;
            }

            if (classification.MissionType == MissionType.Assassination && classification.TargetEliminated)
            {
                string defender = ValueOrFallback(defenderFactionName, "Local");
                return new NpcMissionReport(
                    "Assassination",
                    $"Assassination reported - {location}",
                    $"{defender} leadership was found dead in {location}.");
            }

            if ((classification.MissionType == MissionType.Sabotage
                    || classification.MissionType == MissionType.Diversion)
                && classification.Impact > 0)
            {
                return new NpcMissionReport(
                    "Sabotage Reported",
                    $"Sabotage - {location}",
                    $"Explosions and sabotage damaged operations in {location}.");
            }

            if (IsCombatMissionType(classification.MissionType)
                && classification.EnemiesKilled > 0
                && targetIsPlayerSide)
            {
                return new NpcMissionReport(
                    "Enemy Attack",
                    $"Casualties reported - {location}",
                    $"Imperial forces in {location} came under attack and suffered casualties.");
            }

            return null;
        }

        // Ambient intelligence about activity in the region, with no specific triggering event. This is
        // the only channel gated purely on intel tier, and Assessment (the top tier) is still the
        // vaguest possible inference - never the ground-truth MissionType.
        private static NpcMissionReport BuildSurveillance(
            MissionOutcomeClassification classification,
            string actingFactionName,
            string location,
            NpcReportTier tier)
        {
            switch (tier)
            {
                case NpcReportTier.Movement:
                    return new NpcMissionReport(
                        "Enemy Movement",
                        $"Enemy activity - {location}",
                        $"Listening posts report enemy movement in {location}.");

                case NpcReportTier.Identified:
                {
                    string faction = ValueOrFallback(actingFactionName, "An unidentified force");
                    return new NpcMissionReport(
                        "Enemy Activity",
                        $"Enemy activity - {location}",
                        $"{faction} forces are active in {location}.");
                }

                case NpcReportTier.Assessment:
                {
                    string faction = ValueOrFallback(actingFactionName, "An unidentified force");
                    string category = ActivityCategory(classification.MissionType);
                    return new NpcMissionReport(
                        "Enemy Activity",
                        $"Enemy activity - {location}",
                        $"{faction} forces are active in {location}. " +
                        $"Analysis: pattern consistent with {category} (confidence: moderate).");
                }

                default:
                    return null;
            }
        }

        private static bool IsCombatMissionType(MissionType missionType) => missionType switch
        {
            MissionType.LightningRaid or MissionType.HitAndRun or MissionType.Advance
                or MissionType.DeepStrike or MissionType.EstablishAirhead or MissionType.ObjectiveRaid
                or MissionType.Ambush or MissionType.Extermination or MissionType.CloseAirSupport
                or MissionType.Infiltrate => true,
            _ => false
        };

        // Deliberately vague category phrases for the Assessment tier - the closest the player ever
        // gets to ground truth, and still never the literal MissionType noun.
        private static string ActivityCategory(MissionType missionType) => missionType switch
        {
            MissionType.LightningRaid or MissionType.HitAndRun or MissionType.ObjectiveRaid
                or MissionType.Ambush => "a raid in preparation",
            MissionType.Infiltrate or MissionType.Recon or MissionType.Patrol => "reconnaissance activity",
            MissionType.Sabotage or MissionType.Diversion => "a sabotage operation",
            MissionType.Advance or MissionType.DeepStrike or MissionType.EstablishAirhead
                or MissionType.Extermination or MissionType.CloseAirSupport => "an offensive buildup",
            MissionType.Fortify or MissionType.DefenseInDepth or MissionType.LastStand => "defensive preparations",
            MissionType.Assassination => "an infiltration effort",
            MissionType.Training => "training exercises",
            MissionType.Construction => "construction activity",
            _ => "unidentified activity"
        };

        private static string ValueOrFallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
