using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Extensions
{
    /// <summary>
    /// Intel-gated, player-facing descriptions of a region faction. These need the intelligence
    /// services, so they stay with their owner rather than moving down with the intrinsic
    /// awareness/strength queries in <see cref="RegionFactionExtensions"/>. Extension resolution is
    /// by namespace, so callers are unaffected by the split.
    /// </summary>
    public static class RegionFactionDescriptionExtensions
    {
        public static string GetPopulationDescription(this RegionFaction regionFaction)
        {
            if (regionFaction == null) return "None";
            if (regionFaction.PlanetFaction.Faction.IsPlayerFaction)
            {
                return regionFaction.Population.ToString();
            }

            FactionIntelBelief belief = IntelligenceTargetService.GetBestPlayerVisibleBelief(
                regionFaction.Region,
                regionFaction.PlanetFaction.Faction);
            return FormatBelievedPopulation(belief);
        }

        // Fuzzy, fog-of-war-friendly description of a defensive value (Entrenchment,
        // Detection, Anti-Air). Shared by the planet-tactical and region screens so enemy
        // defenses read consistently and never expose the raw value to the player.
        // Stats are fractional; rounding to the nearest whole level keeps the old int bands.
        public static string GetDefenseLevelDescription(double level)
        {
            switch ((int)Math.Round(level))
            {
                case <= 0:
                    return "None";
                case 1:
                case 2:
                    return "Minimal";
                case 3:
                case 4:
                    return "Mediocre";
                case 5:
                case 6:
                    return "Moderate";
                case 7:
                case 8:
                    return "Heavy";
                default:
                    return "Massive";
            }
        }

        // Player-facing description of regional intelligence. The boundaries preserve the
        // meaningful awareness thresholds used by recon, garrison planning, activity reporting,
        // and located target estimates without exposing the underlying score.
        public static string GetIntelligenceLevelDescription(float intelligence)
        {
            if (!float.IsFinite(intelligence) || intelligence <= 0f)
                return "None";
            if (intelligence < 1f)
                return "Basic";
            if (intelligence < 2f)
                return "Limited";
            if (intelligence < 3f)
                return "Partial";
            if (intelligence < 4f)
                return "Reliable";
            if (intelligence < 6f)
                return "Detailed";
            return "Comprehensive";
        }

        // Strength magnitude expressed as an order-of-magnitude word, intel-gated to match
        // fog-of-war disclosure. Lower intel yields coarser estimates (same as GetPopulationDescription).
        public static string GetForceMagnitudeDescription(this RegionFaction regionFaction)
        {
            if (regionFaction == null) return "None";
            if (regionFaction.PlanetFaction.Faction.IsPlayerFaction)
            {
                return GetMagnitudeWord(regionFaction.GetDeployedStrength());
            }

            FactionIntelBelief belief = IntelligenceTargetService.GetBestPlayerVisibleBelief(
                regionFaction.Region,
                regionFaction.PlanetFaction.Faction);
            if (belief == null) return "None";
            if (belief.Level == IntelLevel.Rumor) return "Rumor";
            return belief.EstimatedMilitaryStrength.HasValue
                ? GetMagnitudeWord(belief.EstimatedMilitaryStrength.Value)
                : "Unknown";
        }

        public static string GetForceMagnitudeDescription(FactionIntelBelief belief)
        {
            if (belief == null) return "None";
            if (belief.Level == IntelLevel.Rumor) return "Rumor";
            return belief.EstimatedMilitaryStrength.HasValue
                ? GetMagnitudeWord(belief.EstimatedMilitaryStrength.Value)
                : "Unknown";
        }

        private static string FormatBelievedPopulation(FactionIntelBelief belief)
        {
            if (belief == null) return "None";
            if (belief.Level == IntelLevel.Rumor) return "Rumor";
            if (!belief.EstimatedPopulation.HasValue) return "Unknown";
            return belief.EstimatedPopulation.Value > 0
                ? belief.EstimatedPopulation.Value.ToString()
                : "Low";
        }

        // Maps a deployed strength value to a rough order-of-magnitude word.
        private static string GetMagnitudeWord(long strength)
        {
            if (strength <= 0)
                return "None";
            if (strength < 10)
                return "Handful";
            if (strength < 100)
                return "Dozens";
            if (strength < 1000)
                return "Hundreds";
            if (strength < 1000000)
                return "Thousands";
            if (strength < 1000000000)
                return "Millions";
            return "Billions";
        }
    }
}
