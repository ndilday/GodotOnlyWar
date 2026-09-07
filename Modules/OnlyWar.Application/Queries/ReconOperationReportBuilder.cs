using OnlyWar.Helpers.Missions;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public sealed record ReconOperationReport(string OutcomeStatus, string Summary);

    /// <summary>
    /// Builds the order-level commander summary for recon elements that resolved independently.
    /// Squad career records remain element-specific; this is only the grouped presentation.
    /// </summary>
    public static class ReconOperationReportBuilder
    {
        public static ReconOperationReport Build(
            IReadOnlyList<MissionContext> elementContexts,
            string location)
        {
            IReadOnlyList<MissionContext> contexts = elementContexts
                ?? Array.Empty<MissionContext>();
            List<MissionOutcomeClassification> outcomes = contexts
                .Select(MissionOutcomeClassifier.Classify)
                .ToList();
            int count = outcomes.Count;
            if (count == 0)
            {
                return new ReconOperationReport(
                    "MISSION INCONCLUSIVE",
                    $"No reconnaissance elements reported from {location}.");
            }

            int undetected = outcomes.Count(outcome =>
                !outcome.WasDetected
                && outcome.Disposition == MissionForceDisposition.Nominal);
            int brokeContact = outcomes.Count(outcome =>
                outcome.Disposition == MissionForceDisposition.BrokeContact);
            int lostContact = outcomes.Count(outcome =>
                outcome.Disposition == MissionForceDisposition.LostContact);
            int withdrew = outcomes.Count(outcome =>
                outcome.Disposition == MissionForceDisposition.WithdrewUnderFire);
            int aborted = outcomes.Count(outcome =>
                outcome.Disposition == MissionForceDisposition.AbortedBeforeObjective);
            int seriousFailures = lostContact + withdrew + aborted;
            float rawNetEvidence = outcomes.Sum(outcome => outcome.Impact);
            int battles = contexts.Sum(context =>
                context.DebriefLines.Count(line => line.HasBattle));
            int friendlyDeaths = contexts.Sum(context =>
                context.DebriefLines
                    .Where(line => line.HasBattle)
                    .Sum(line => line.BattleReport?.PlayerDeaths ?? 0));

            string status = seriousFailures == count
                ? "MISSION FAILED"
                : seriousFailures > 0
                    ? "MIXED RESULTS"
                    : rawNetEvidence > 0
                        ? "MISSION SUCCESSFUL"
                        : "MISSION INCONCLUSIVE";

            List<string> clauses = new();
            AddCountClause(clauses, undetected, count, "operated undetected");
            AddCountClause(clauses, brokeContact, count, "broke contact after detection");
            AddCountClause(clauses, withdrew, count, "withdrew under fire");
            AddCountClause(clauses, lostContact, count, "lost contact with base");
            AddCountClause(clauses, aborted, count, "aborted before completing its sweep");
            int otherwiseDetected = count - undetected - brokeContact - seriousFailures;
            AddCountClause(clauses, otherwiseDetected, count, "reported enemy detection");
            AddCountClause(clauses, battles, battles == 1
                ? "engagement was recorded"
                : "engagements were recorded");
            AddCountClause(clauses, friendlyDeaths, friendlyDeaths == 1
                ? "Battle Brother was killed"
                : "Battle Brothers were killed");

            string details = clauses.Count > 0
                ? " " + JoinClauses(clauses) + "."
                : "";
            return new ReconOperationReport(
                status,
                $"{count} reconnaissance {(count == 1 ? "squad" : "squads")} operated in {location}.{details}");
        }

        private static void AddCountClause(List<string> clauses, int count, string description)
        {
            if (count > 0) clauses.Add($"{count} {description}");
        }

        private static void AddCountClause(
            List<string> clauses,
            int outcomeCount,
            int totalCount,
            string description)
        {
            if (outcomeCount <= 0) return;
            clauses.Add(outcomeCount == totalCount
                ? $"All {description}"
                : $"{outcomeCount} {description}");
        }

        private static string JoinClauses(IReadOnlyList<string> clauses)
        {
            if (clauses.Count == 1) return clauses[0];
            if (clauses.Count == 2) return $"{clauses[0]} and {clauses[1]}";
            return string.Join(", ", clauses.Take(clauses.Count - 1))
                + $", and {clauses[^1]}";
        }
    }
}
