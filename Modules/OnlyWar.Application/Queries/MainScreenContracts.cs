using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>Header facts the main screen prints. Already formatted; no campaign date object.</summary>
public sealed record CampaignHeaderView(string DateText, int Requisition);

/// <summary>
/// What the main screen opens on: the world to select first (the promised world at game start,
/// falling back to any charted world) and whether the founding directive is still unacknowledged.
/// </summary>
public sealed record MainScreenStartupView(int? InitialPlanetId, bool OpeningBriefPending);

/// <summary>
/// One resolved turn: the report to show, the scenario resolution line if the opening scenario
/// closed this turn, and whether the mandatory recruitment setup now blocks the player.
/// </summary>
public sealed record ResolveTurnView(
    bool Succeeded,
    string Message,
    TurnReportView Report = null,
    string ScenarioNotification = null,
    bool RequiresRecruitmentSetup = false);

/// <summary>A scout squad an aspirant may be posted to, with the label the menu shows.</summary>
public sealed record NeophytePlacementTarget(int SquadId, string Label);

public sealed record NeophytePlacementOptions(
    bool IsAvailable,
    string UnavailableReason,
    IReadOnlyList<NeophytePlacementTarget> Targets);

public sealed record NeophytePlacementResult(bool Succeeded, string Message);

/// <summary>
/// The main game screen's own campaign reads and commands. Navigation, dialogs and scene stacking
/// stay in the host; deciding what the campaign says, and every write to it, live here.
/// </summary>
public interface IMainScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    CampaignHeaderView QueryHeader();

    MainScreenStartupView QueryStartup();

    /// <summary>
    /// Records that the founding directive was shown. Only the session that produced the pending
    /// flag may acknowledge it, so a campaign replaced behind the brief is not marked read.
    /// </summary>
    void AcknowledgeOpeningBrief(Guid sessionToken);

    /// <summary>The saved report for the previous turn, or an empty view when a save has none.</summary>
    TurnReportView QueryLastTurnReport();

    /// <summary>
    /// Advances the campaign, builds the turn report and - only once both succeed - replaces the
    /// campaign's persisted last-turn report. A failed turn therefore leaves the previous report
    /// intact for the protected pre-turn save and any later manual save.
    /// </summary>
    ResolveTurnView ResolveTurn(Guid sessionToken);

    /// <summary>The scout squads an aspirant can be posted to, or why there are none.</summary>
    NeophytePlacementOptions QueryNeophytePlacementTargets();

    NeophytePlacementResult PlaceNeophyte(Guid sessionToken, int aspirantId, int squadId);
}
