using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Reports;
using OnlyWar.Models.Supply;
using OnlyWar.Models.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Builds the persisted last-turn snapshot and the live presentation from one resolved result.
    /// The presentation retains BattleHistory only for the current session; the snapshot never does.
    /// </summary>
    internal static class LastTurnReportSnapshotBuilder
    {
        internal static LastTurnReportBuildResult Build(Date resolvedDate, TurnResolutionResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return Build(
                resolvedDate,
                result.MissionContexts,
                result.SpecialMissions,
                result.StrategicCombatResults,
                result.ConstructionReports,
                result.FortificationTransfers,
                result.GovernorRequestReports,
                result.RecruitmentReport,
                result.CampaignEvents,
                result.CampaignIdentity);
        }

        internal static LastTurnReportBuildResult Build(
            Date resolvedDate,
            IEnumerable<MissionContext> missionContexts,
            IEnumerable<Mission> specialMissions,
            IEnumerable<StrategicCombatResult> strategicCombatResults,
            IEnumerable<ConstructionProgressReport> constructionReports = null,
            IEnumerable<FortificationTransferReport> fortificationTransfers = null,
            IEnumerable<GovernorRequestReport> governorRequestReports = null,
            RecruitmentTurnReport recruitmentReport = null,
            IEnumerable<CampaignEvent> campaignEvents = null,
            CampaignIdentity campaignIdentity = null)
        {
            List<EndOfTurnReportEntry> presentationEntries = EndOfTurnDialogController.BuildReportEntries(
                (missionContexts ?? Enumerable.Empty<MissionContext>()).ToList(),
                specialMissions,
                strategicCombatResults,
                constructionReports,
                fortificationTransfers,
                governorRequestReports,
                recruitmentReport,
                campaignEvents,
                campaignIdentity);

            LastTurnReportSnapshot snapshot = BuildSnapshot(resolvedDate, presentationEntries);
            return new LastTurnReportBuildResult(snapshot, presentationEntries);
        }

        internal static LastTurnReportSnapshot BuildSnapshot(
            Date resolvedDate,
            IEnumerable<EndOfTurnReportEntry> presentationEntries)
        {
            return new LastTurnReportSnapshot(
                resolvedDate?.GetTotalWeeks() ?? 0,
                (presentationEntries ?? Enumerable.Empty<EndOfTurnReportEntry>())
                    .Select(ToSnapshot)
                    .ToList());
        }

        internal static IReadOnlyList<EndOfTurnReportEntry> BuildPresentationEntries(
            LastTurnReportSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return Array.Empty<EndOfTurnReportEntry>();
            }

            return snapshot.Entries.Select(ToPresentationEntry).ToList();
        }

        private static LastTurnReportEntrySnapshot ToSnapshot(EndOfTurnReportEntry entry)
        {
            LastTurnDebriefSnapshot debrief = entry.CanOpenDebrief
                ? new LastTurnDebriefSnapshot(
                    entry.Title,
                    entry.Subtitle,
                    entry.OutcomeStatus,
                    entry.Summary,
                    entry.DebriefLines.Select(ToSnapshot).ToList())
                : null;

            return new LastTurnReportEntrySnapshot(
                entry.Title,
                entry.Subtitle,
                entry.Summary,
                entry.OutcomeStatus,
                entry.IsEnemyActivity,
                debrief);
        }

        private static LastTurnDebriefLineSnapshot ToSnapshot(MissionDebriefLine line)
        {
            BattleDebriefReport report = line.BattleReport
                ?? (line.BattleHistory == null ? null : BattleDebriefReportBuilder.Build(line.BattleHistory));
            BattleSummarySnapshot battleSummary = report == null
                ? null
                : new BattleSummarySnapshot(
                    report.PlayerDeaths,
                    report.OpposingDeaths,
                    report.PlayerIncapacitated,
                    report.PlayerCasualties.Select(ToSnapshot).ToList());

            return new LastTurnDebriefLineSnapshot(
                line.Text,
                line.Day,
                line.SquadName,
                battleSummary);
        }

        private static BattleCasualtySnapshot ToSnapshot(BattleCasualtyEntry casualty) =>
            new(
                casualty.SoldierId,
                casualty.Name,
                casualty.Rank,
                casualty.Squad,
                casualty.Company,
                casualty.Disposition.ToString(),
                casualty.RecoveryWeeks);

        private static EndOfTurnReportEntry ToPresentationEntry(LastTurnReportEntrySnapshot entry)
        {
            if (entry.Debrief == null)
            {
                return new EndOfTurnReportEntry(
                    entry.Title,
                    entry.Subtitle,
                    entry.Summary,
                    false,
                    entry.OutcomeStatus,
                    isEnemyActivity: entry.IsEnemyActivity);
            }

            return new EndOfTurnReportEntry(
                entry.Title,
                entry.Subtitle,
                entry.Summary,
                true,
                entry.OutcomeStatus,
                entry.Debrief.Lines.Select(ToPresentationLine).ToList(),
                entry.IsEnemyActivity);
        }

        private static MissionDebriefLine ToPresentationLine(LastTurnDebriefLineSnapshot line)
        {
            BattleDebriefReport battleReport = line.BattleSummary == null
                ? null
                : new BattleDebriefReport(
                    line.BattleSummary.PlayerDeaths,
                    line.BattleSummary.OpposingDeaths,
                    line.BattleSummary.Casualties.Select(ToPresentationCasualty).ToList(),
                    line.BattleSummary.PlayerIncapacitated);

            return new MissionDebriefLine(
                line.Text,
                battleReport: battleReport,
                day: line.Day,
                squadName: line.SquadName);
        }

        private static BattleCasualtyEntry ToPresentationCasualty(BattleCasualtySnapshot casualty)
        {
            BattleCasualtyDisposition disposition = Enum.TryParse(
                casualty.Disposition,
                ignoreCase: true,
                out BattleCasualtyDisposition parsed)
                ? parsed
                : BattleCasualtyDisposition.Recovering;
            return new BattleCasualtyEntry(
                casualty.SoldierId,
                casualty.Name,
                casualty.Rank,
                casualty.Squad,
                casualty.Company,
                disposition,
                casualty.RecoveryWeeks);
        }
    }

    internal sealed class LastTurnReportBuildResult
    {
        internal LastTurnReportSnapshot Snapshot { get; }
        internal IReadOnlyList<EndOfTurnReportEntry> PresentationEntries { get; }

        internal LastTurnReportBuildResult(
            LastTurnReportSnapshot snapshot,
            IReadOnlyList<EndOfTurnReportEntry> presentationEntries)
        {
            Snapshot = snapshot;
            PresentationEntries = presentationEntries;
        }
    }
}
