using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Campaign.Recruitment;
using OnlyWar.Campaign.Turns;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Reports;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;

namespace OnlyWar.Application;

/// <summary>
/// Coordinates the main workspace's focused reads and commands.
/// </summary>
internal sealed class MainScreenContext
{
    private const string StaleSessionMessage =
        "The campaign changed. Reopen the campaign and try again.";

    private static readonly TurnReportView EmptyTurnReport = new(
        null,
        "No previous turn report is available for this save.",
        []);

    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly Date _currentDate;
    private readonly RecruitmentPromotionService _promotionService;
    private readonly Func<TurnResolutionResult> _advanceTurn;
    private readonly BattleReplayRegistry _battleReplays = new();
    private IBattleReplayProjector _battleReplayProjector;
    private TurnReportView _lastTurnReport;

    internal MainScreenContext(
        Sector sector,
        GameRulesData rules,
        Date currentDate,
        RecruitmentPromotionService promotionService,
        Func<TurnResolutionResult> advanceTurn)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _currentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
        _promotionService = promotionService
            ?? throw new ArgumentNullException(nameof(promotionService));
        _advanceTurn = advanceTurn ?? throw new ArgumentNullException(nameof(advanceTurn));
    }

    internal CampaignHeaderView QueryHeader() =>
        new(
            _currentDate?.ToString() ?? "",
            _sector.PlayerForce?.Army?.Requisition ?? 0);

    internal MainScreenStartupView QueryStartup()
    {
        // Open on the world the chapter fleet is orbiting - the promised world at game start -
        // and fall back to any charted world so a campaign without an orbiting fleet still opens
        // somewhere sensible.
        Planet initial =
            _sector.PlayerForce?.Fleet?.TaskForces?.FirstOrDefault()?.Planet
            ?? _sector.Planets.Values.FirstOrDefault();
        CampaignScenario scenario = _sector.Scenario;
        return new MainScreenStartupView(
            initial?.Id,
            scenario is { State: ObjectiveState.Pending, BriefingAcknowledged: false });
    }

    internal bool AcknowledgeOpeningBrief()
    {
        CampaignScenario scenario = _sector.Scenario;
        if (scenario == null || scenario.BriefingAcknowledged) return false;

        scenario.BriefingAcknowledged = true;
        return true;
    }

    internal TurnReportView QueryLastTurnReport() =>
        _lastTurnReport ?? BuildReportView(_sector.PlayerForce?.LastTurnReportSnapshot);

    internal void ConfigureBattleReplayProjector(IBattleReplayProjector projector) =>
        _battleReplayProjector = projector ?? throw new ArgumentNullException(nameof(projector));

    internal BattleReplayDisplay QueryBattleReplay(BattleReplayQuery query)
    {
        if (query == null || query.ReplayId == Guid.Empty
            || _battleReplayProjector == null
            || !_battleReplays.TryGet(query.ReplayId, out IBattleReplay replay))
        {
            return null;
        }

        return _battleReplayProjector.Build(
            replay,
            query.TurnIndex,
            query.SelectedFormationId);
    }

    internal ResolveTurnView ResolveTurn()
    {
        TurnResolutionResult result = _advanceTurn();
        LastTurnReportBuildResult build = LastTurnReportSnapshotBuilder.Build(
            _currentDate,
            result,
            replayIdFactory: replay => _battleReplays.Register(replay));

        // Replace the persisted report only after resolution and report construction both
        // succeed. A failed turn therefore leaves the previous report available to the protected
        // pre-turn save and to any later manual save.
        PlayerForce force = _sector.PlayerForce;
        if (force != null)
        {
            force.LastTurnReportSnapshot = build.Snapshot;
        }

        TurnReportView report = new(
            FormatResolvedDate(build.Snapshot), null, build.PresentationEntries);
        _lastTurnReport = report;

        return new ResolveTurnView(
            true,
            null,
            report,
            result.ScenarioNotification,
            RequiresRecruitmentSetup());
    }

    internal NeophytePlacementOptions QueryNeophytePlacementTargets()
    {
        PlayerForce force = _sector.PlayerForce;
        if (force?.RecruitmentProgram == null)
        {
            return new NeophytePlacementOptions(
                false, "The Chapter has no active recruitment program.", []);
        }

        SquadTemplate targetTemplate = _rules.ChapterDoctrine.ScoutSquad;
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

    internal NeophytePlacementResult PlaceNeophyte(int aspirantId, int squadId)
    {
        RecruitmentPromotionResult result =
            _promotionService.PromoteAspirantToNeophyte(aspirantId, squadId);
        return new NeophytePlacementResult(result.Succeeded, result.Message);
    }

    private bool RequiresRecruitmentSetup() =>
        _sector.PlayerForce?.RecruitmentProgram
            is RecruitmentProgram { IsSetupComplete: false };

    private static TurnReportView BuildReportView(LastTurnReportSnapshot snapshot) =>
        snapshot == null
            ? EmptyTurnReport
            : new TurnReportView(
                FormatResolvedDate(snapshot),
                null,
                LastTurnReportSnapshotBuilder.BuildPresentationEntries(snapshot));

    private static string FormatResolvedDate(LastTurnReportSnapshot snapshot) =>
        snapshot?.ResolvedDate > 0
            ? Date.FromTotalWeeks(snapshot.ResolvedDate).ToString()
            : null;
}
