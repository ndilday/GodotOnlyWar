using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

public enum BattleCasualtyDispositionView
{
    Dead,
    Incapacitated,
    ReplacementRequired,
    Recovering
}

public sealed record BattleCasualtyView(
    int SoldierId,
    string Name,
    string Rank,
    string Squad,
    string Company,
    BattleCasualtyDispositionView Disposition,
    int RecoveryWeeks);

/// <summary>
/// Detached casualty facts for a debrief. This deliberately does not expose the battle module's
/// report type so a Godot dialog cannot drift back into tactical/domain data.
/// </summary>
public sealed record BattleDebriefView(
    int PlayerDeaths,
    int OpposingDeaths,
    IReadOnlyList<BattleCasualtyView> PlayerCasualties,
    int PlayerIncapacitated = 0)
{
    public IReadOnlyList<BattleCasualtyView> Casualties { get; } =
        PlayerCasualties ?? Array.Empty<BattleCasualtyView>();
}

/// <summary>
/// One narrative line shown by the debrief dialog. A replay is addressed by an opaque application
/// id; the dialog never receives the replay implementation or a mission runtime record.
/// </summary>
public sealed record MissionDebriefLineView(
    string Text,
    Guid? BattleReplayId = null,
    BattleDebriefView BattleReport = null,
    ushort? Day = null,
    string SquadName = null)
{
    public bool HasBattle => BattleReplayId.HasValue || BattleReport != null;
}

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
    // NPC entries can open a redacted debrief without ever exposing the underlying mission.
    public string OutcomeStatus { get; }
    public IReadOnlyList<MissionDebriefLineView> DebriefLines { get; }

    public EndOfTurnReportEntry(
        string title,
        string subtitle,
        string summary,
        bool canOpenDebrief,
        string outcomeStatus = "",
        IReadOnlyList<MissionDebriefLineView> debriefLines = null,
        bool isEnemyActivity = false)
    {
        Title = title ?? "";
        Subtitle = subtitle ?? "";
        Summary = summary ?? "";
        CanOpenDebrief = canOpenDebrief;
        IsEnemyActivity = isEnemyActivity;
        OutcomeStatus = outcomeStatus ?? "";
        DebriefLines = debriefLines ?? Array.Empty<MissionDebriefLineView>();
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
