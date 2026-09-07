using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Application;

/// <summary>
/// The main game screen's boundary: header facts, the world it opens on, the founding directive,
/// turn resolution with its report, and neophyte placement. The scene keeps navigation and dialogs
/// and no longer reads or writes the campaign to answer any of these.
/// </summary>
public sealed partial class CampaignApplication : IMainScreenApplication
{
    private static readonly TurnReportView EmptyTurnReport = new(
        null,
        "No previous turn report is available for this save.",
        []);

    public CampaignHeaderView QueryHeader()
    {
        GameSession session = _activeSession;
        if (session == null) return new CampaignHeaderView("", 0);
        return new CampaignHeaderView(
            session.CurrentDate?.ToString() ?? "",
            session.Sector.PlayerForce?.Army?.Requisition ?? 0);
    }

    public MainScreenStartupView QueryStartup()
    {
        GameSession session = _activeSession;
        if (session == null) return new MainScreenStartupView(null, false);

        // Open on the world the chapter fleet is orbiting - the promised world at game start -
        // and fall back to any charted world so a campaign without an orbiting fleet still opens
        // somewhere sensible.
        Sector sector = session.Sector;
        Planet initial =
            sector.PlayerForce?.Fleet?.TaskForces?.FirstOrDefault()?.Planet
            ?? sector.Planets.Values.FirstOrDefault();
        CampaignScenario scenario = sector.Scenario;
        return new MainScreenStartupView(
            initial?.Id,
            scenario is { State: ObjectiveState.Pending, BriefingAcknowledged: false });
    }

    public void AcknowledgeOpeningBrief(Guid sessionToken)
    {
        if (_activeSession == null || sessionToken != SessionToken) return;
        CampaignScenario scenario = _activeSession.Sector.Scenario;
        if (scenario == null || scenario.BriefingAcknowledged) return;

        scenario.BriefingAcknowledged = true;
        MarkChanged();
    }

    public TurnReportView QueryLastTurnReport() =>
        BuildReportView(_activeSession?.Sector.PlayerForce?.LastTurnReportSnapshot);

    public ResolveTurnView ResolveTurn(Guid sessionToken)
    {
        GameSession session = _activeSession;
        if (session == null || sessionToken != SessionToken)
        {
            return new ResolveTurnView(
                false, "The campaign changed. Reopen the campaign and try again.");
        }

        TurnResolutionResult result = AdvanceTurn(session);
        LastTurnReportBuildResult build = LastTurnReportSnapshotBuilder.Build(
            session.CurrentDate, result);

        // Replace the persisted report only after resolution and report construction both
        // succeed. A failed turn therefore leaves the previous report available to the protected
        // pre-turn save and to any later manual save.
        PlayerForce force = session.Sector.PlayerForce;
        if (force != null)
        {
            force.LastTurnReportSnapshot = build.Snapshot;
        }

        return new ResolveTurnView(
            true,
            null,
            new TurnReportView(
                FormatResolvedDate(build.Snapshot), null, build.PresentationEntries),
            result.ScenarioNotification,
            RequiresRecruitmentSetup());
    }

    public NeophytePlacementOptions QueryNeophytePlacementTargets()
    {
        GameSession session = _activeSession;
        PlayerForce force = session?.Sector.PlayerForce;
        if (force?.RecruitmentProgram == null)
        {
            return new NeophytePlacementOptions(
                false, "The Chapter has no active recruitment program.", []);
        }

        SquadTemplate targetTemplate = session.Rules.ChapterDoctrine.ScoutSquad;
        List<NeophytePlacementTarget> targets = force.Army.OrderOfBattle.GetAllSquads()
            .Where(squad => squad.IsPresentOperationalForce)
            .Where(squad => squad.SquadTemplate == targetTemplate)
            .Where(squad =>
                (squad.CurrentRegion?.Planet
                    ?? squad.BoardedLocation?.Fleet?.Planet)?.Id
                == force.RecruitmentProgram.HomeWorldPlanetId)
            .OrderBy(squad => squad.ParentUnit?.Name)
            .ThenBy(squad => squad.Name)
            .Select(squad => new NeophytePlacementTarget(
                squad.Id,
                $"{squad.Name} - {squad.ParentUnit?.Name} ({SquadLocationFormatter.Format(squad)})"))
            .ToList();

        return targets.Count == 0
            ? new NeophytePlacementOptions(
                false,
                $"No {targetTemplate.Name} is available on or in orbit of the Home World.",
                [])
            : new NeophytePlacementOptions(true, null, targets);
    }

    public NeophytePlacementResult PlaceNeophyte(Guid sessionToken, int aspirantId, int squadId)
    {
        GameSession session = _activeSession;
        if (session == null || sessionToken != SessionToken)
        {
            return new NeophytePlacementResult(
                false, "The campaign changed. Reopen the recruitment screen and try again.");
        }

        RecruitmentPromotionResult result =
            new RecruitmentPromotionService(session).PromoteAspirantToNeophyte(aspirantId, squadId);
        if (result.Succeeded)
        {
            MarkChanged();
        }
        return new NeophytePlacementResult(result.Succeeded, result.Message);
    }

    private static TurnReportView BuildReportView(Models.Reports.LastTurnReportSnapshot snapshot) =>
        snapshot == null
            ? EmptyTurnReport
            : new TurnReportView(
                FormatResolvedDate(snapshot),
                null,
                LastTurnReportSnapshotBuilder.BuildPresentationEntries(snapshot));

    private static string FormatResolvedDate(Models.Reports.LastTurnReportSnapshot snapshot) =>
        snapshot?.ResolvedDate > 0
            ? Date.FromTotalWeeks(snapshot.ResolvedDate).ToString()
            : null;
}
