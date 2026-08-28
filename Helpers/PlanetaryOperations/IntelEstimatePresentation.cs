using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.PlanetaryOperations
{
    public sealed record IntelEstimatePresentation(
        string Value,
        IntelLevel Level,
        string Confidence,
        string EvidenceAge,
        string DecayNotice)
    {
        public string Summary => $"{Value} · {Confidence}";
        public string Detail => $"{EvidenceAge} · {DecayNotice}";
        public int? EvidenceAgeWeeks { get; init; }
        public string LastReport => EvidenceAgeWeeks switch
        {
            null => "No report",
            0 => "Current",
            1 => "Last report 1 week ago",
            _ => $"Last report {EvidenceAgeWeeks} weeks ago"
        };
    }

    /// <summary>Turns the campaign's evidence ledger into the deliberately imprecise UI ladder.</summary>
    public static class IntelEstimatePresentationBuilder
    {
        private static readonly string[] Magnitudes =
            ["Handful", "Dozens", "Hundreds", "Thousands", "Millions", "Billions"];

        public static IntelEstimatePresentation Build(
            FactionIntelBelief belief,
            int currentWeek)
        {
            if (belief == null)
            {
                return new IntelEstimatePresentation(
                    "No estimate", IntelLevel.None, "○○○○ None",
                    "No evidence", "No evidence to decay");
            }

            string value = belief.Level switch
            {
                IntelLevel.Rumor => "Strength undisclosed",
                IntelLevel.Suspected => BuildBand(belief.EstimatedMilitaryStrength),
                _ => RegionFactionExtensions.GetForceMagnitudeDescription(belief)
            };
            int age = Math.Max(0, currentWeek - belief.LastEvidenceWeek);
            return new IntelEstimatePresentation(
                value,
                belief.Level,
                $"{Marks(belief.Level)} {belief.Level}",
                age == 0 ? "Evidence refreshed this week" : $"Evidence age: {age} wk",
                DescribeDecay(belief))
            {
                EvidenceAgeWeeks = age
            };
        }

        public static IntelEstimatePresentation Build(
            RegionFaction presence,
            int currentWeek) =>
            presence?.PlanetFaction?.Faction?.IsPlayerFaction == true
                ? new IntelEstimatePresentation(
                    presence.GetDeployedStrength().ToString("N0"),
                    IntelLevel.Located,
                    "●●●● Exact",
                    "Current disposition",
                    "Friendly strength does not use hostile-intel decay")
                : Build(IntelligenceTargetService.GetBestPlayerVisibleBelief(
                    presence?.Region, presence?.PlanetFaction?.Faction), currentWeek);

        public static IntelEstimatePresentation BuildWorld(
            IEnumerable<RegionFaction> presences,
            int currentWeek)
        {
            List<FactionIntelBelief> beliefs = (presences ?? [])
                .Where(presence => presence != null)
                .Select(presence => IntelligenceTargetService.GetBestPlayerVisibleBelief(
                    presence.Region, presence.PlanetFaction.Faction))
                .Where(belief => belief != null)
                .ToList();
            if (beliefs.Count == 0) return Build((FactionIntelBelief)null, currentWeek);

            // The value comes from the largest disclosed concentration, while precision is capped
            // by the least certain region contributing to the world-level claim.
            IntelLevel weakest = beliefs.Min(belief => belief.Level);
            long? maximum = beliefs.Where(belief => belief.EstimatedMilitaryStrength.HasValue)
                .Select(belief => belief.EstimatedMilitaryStrength)
                .Max();
            FactionIntelBelief weakestBelief = beliefs
                .Where(belief => belief.Level == weakest)
                .OrderBy(belief => belief.Evidence)
                .First();
            FactionIntelBelief aggregate = new(
                weakestBelief.Region,
                weakestBelief.TargetFaction,
                weakestBelief.Evidence,
                null,
                maximum,
                beliefs.Min(belief => belief.LastEvidenceWeek));
            return Build(aggregate, currentWeek);
        }

        public static string Marks(IntelLevel level) => level switch
        {
            IntelLevel.Located => "●●●●",
            IntelLevel.Confirmed => "●●●○",
            IntelLevel.Suspected => "●●○○",
            IntelLevel.Rumor => "●○○○",
            _ => "○○○○"
        };

        private static string BuildBand(long? estimate)
        {
            if (!estimate.HasValue) return "Broad strength unknown";
            string center = Magnitude(estimate.Value);
            int index = Array.IndexOf(Magnitudes, center);
            if (index < 0) return "Broad strength unknown";
            int lower = Math.Max(0, index - 1);
            int upper = Math.Min(Magnitudes.Length - 1, index + 1);
            if (lower == upper) return Magnitudes[index];
            return $"{Magnitudes[lower]}–{Magnitudes[upper]}";
        }

        private static string DescribeDecay(FactionIntelBelief belief)
        {
            float evidence = belief.Evidence;
            IntelLevel current = belief.Level;
            for (int week = 1; week <= 52; week++)
            {
                evidence = FactionIntelligenceRules.DecayEvidence(evidence);
                IntelLevel next = FactionIntelligenceRules.GetLevel(evidence);
                if (next < current)
                {
                    return $"Drops to {next} in {week} wk without new evidence";
                }
            }
            return "Confidence stable for at least 52 wk";
        }

        private static string Magnitude(long strength)
        {
            if (strength <= 0) return "None";
            if (strength < 10) return "Handful";
            if (strength < 100) return "Dozens";
            if (strength < 1_000) return "Hundreds";
            if (strength < 1_000_000) return "Thousands";
            if (strength < 1_000_000_000) return "Millions";
            return "Billions";
        }
    }
}
