using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OnlyWar.Models.Reports
{
    /// <summary>
    /// The bounded, UI-safe representation of the most recently resolved turn report.
    /// It contains no references to campaign entities or tactical replay state.
    /// </summary>
    public sealed class LastTurnReportSnapshot
    {
        /// <summary>
        /// The campaign date as Date.GetTotalWeeks(). Zero means that the date was not supplied.
        /// </summary>
        public int ResolvedDate { get; }
        public IReadOnlyList<LastTurnReportEntrySnapshot> Entries { get; }

        public LastTurnReportSnapshot(
            int resolvedDate,
            IReadOnlyList<LastTurnReportEntrySnapshot> entries)
        {
            ResolvedDate = resolvedDate;
            Entries = new ReadOnlyCollection<LastTurnReportEntrySnapshot>(
                (entries ?? Array.Empty<LastTurnReportEntrySnapshot>()).ToList());
        }
    }

    public sealed class LastTurnReportEntrySnapshot
    {
        public string Title { get; }
        public string Subtitle { get; }
        public string Summary { get; }
        public string OutcomeStatus { get; }
        public bool IsEnemyActivity { get; }
        public LastTurnDebriefSnapshot Debrief { get; }

        public LastTurnReportEntrySnapshot(
            string title,
            string subtitle,
            string summary,
            string outcomeStatus,
            bool isEnemyActivity,
            LastTurnDebriefSnapshot debrief = null)
        {
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            Summary = summary ?? "";
            OutcomeStatus = outcomeStatus ?? "";
            IsEnemyActivity = isEnemyActivity;
            Debrief = debrief;
        }
    }

    public sealed class LastTurnDebriefSnapshot
    {
        public string Title { get; }
        public string Subtitle { get; }
        public string OutcomeStatus { get; }
        public string OutcomeSummary { get; }
        public IReadOnlyList<LastTurnDebriefLineSnapshot> Lines { get; }

        public LastTurnDebriefSnapshot(
            string title,
            string subtitle,
            string outcomeStatus,
            string outcomeSummary,
            IReadOnlyList<LastTurnDebriefLineSnapshot> lines)
        {
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            OutcomeStatus = outcomeStatus ?? "";
            OutcomeSummary = outcomeSummary ?? "";
            Lines = new ReadOnlyCollection<LastTurnDebriefLineSnapshot>(
                (lines ?? Array.Empty<LastTurnDebriefLineSnapshot>()).ToList());
        }
    }

    public sealed class LastTurnDebriefLineSnapshot
    {
        public string Text { get; }
        public ushort? Day { get; }
        public string SquadName { get; }
        public BattleSummarySnapshot BattleSummary { get; }

        public LastTurnDebriefLineSnapshot(
            string text,
            ushort? day = null,
            string squadName = null,
            BattleSummarySnapshot battleSummary = null)
        {
            Text = text ?? "";
            Day = day;
            SquadName = squadName;
            BattleSummary = battleSummary;
        }
    }

    public sealed class BattleSummarySnapshot
    {
        public int PlayerDeaths { get; }
        public int OpposingDeaths { get; }
        public int PlayerIncapacitated { get; }
        public IReadOnlyList<BattleCasualtySnapshot> Casualties { get; }

        public BattleSummarySnapshot(
            int playerDeaths,
            int opposingDeaths,
            int playerIncapacitated,
            IReadOnlyList<BattleCasualtySnapshot> casualties)
        {
            PlayerDeaths = playerDeaths;
            OpposingDeaths = opposingDeaths;
            PlayerIncapacitated = playerIncapacitated;
            Casualties = new ReadOnlyCollection<BattleCasualtySnapshot>(
                (casualties ?? Array.Empty<BattleCasualtySnapshot>()).ToList());
        }
    }

    public sealed class BattleCasualtySnapshot
    {
        public int SoldierId { get; }
        public string Name { get; }
        public string Rank { get; }
        public string Squad { get; }
        public string Company { get; }
        public string Disposition { get; }
        public int RecoveryWeeks { get; }

        public BattleCasualtySnapshot(
            int soldierId,
            string name,
            string rank,
            string squad,
            string company,
            string disposition,
            int recoveryWeeks)
        {
            SoldierId = soldierId;
            Name = name ?? "Unknown";
            Rank = rank ?? "Battle-Brother";
            Squad = squad ?? "Unassigned";
            Company = company ?? "No Company";
            Disposition = disposition ?? "Recovering";
            RecoveryWeeks = recoveryWeeks;
        }
    }
}
