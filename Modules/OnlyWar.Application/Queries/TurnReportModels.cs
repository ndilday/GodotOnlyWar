using System;
using System.Collections.Generic;
using OnlyWar.Models.Missions;

namespace OnlyWar.Application;

/// <summary>
/// One card in the turn report. Everything on it is already redacted and rendered: the screen
/// prints these strings and never looks at the mission, region or faction they were built from.
/// The debrief lines carry a battle replay for the current session only; a snapshot restored from
/// a save carries the compact battle summary and no replay.
/// </summary>
public sealed class EndOfTurnReportEntry
{
    public string Title { get; }
    public string Subtitle { get; }
    public string Summary { get; }
    public bool CanOpenDebrief { get; }
    public bool IsEnemyActivity { get; }
    // Computed once at entry-build time so the debrief never needs to read a MissionContext -
    // NPC entries can open a (redacted) debrief without ever exposing the underlying mission.
    public string OutcomeStatus { get; }
    public IReadOnlyList<MissionDebriefLine> DebriefLines { get; }

    public EndOfTurnReportEntry(
        string title,
        string subtitle,
        string summary,
        bool canOpenDebrief,
        string outcomeStatus = "",
        IReadOnlyList<MissionDebriefLine> debriefLines = null,
        bool isEnemyActivity = false)
    {
        Title = title ?? "";
        Subtitle = subtitle ?? "";
        Summary = summary ?? "";
        CanOpenDebrief = canOpenDebrief;
        IsEnemyActivity = isEnemyActivity;
        OutcomeStatus = outcomeStatus ?? "";
        DebriefLines = debriefLines ?? Array.Empty<MissionDebriefLine>();
    }
}

/// <summary>
/// The whole turn report as the dialog shows it: the already-formatted resolved date, the cards,
/// and the message to print when there is nothing to show.
/// </summary>
public sealed record TurnReportView(
    string ResolvedDateLabel,
    string EmptyMessage,
    IReadOnlyList<EndOfTurnReportEntry> Entries);
