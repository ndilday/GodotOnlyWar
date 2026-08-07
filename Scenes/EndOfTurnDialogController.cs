using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class EndOfTurnDialogController : DialogController
{
    private EndOfTurnDialogView _view;
    private MissionDebriefDialogController _missionDebriefDialog;
    private BattleReviewController _battleReviewDialog;
    private List<MissionContext> _missionContexts = [];
    private List<EndOfTurnReportEntry> _reportEntries = [];

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<EndOfTurnDialogView>("DialogView");
        _view.EntrySelected += OnEntrySelected;
    }

    public override void _ExitTree()
    {
        if (_view != null)
        {
            _view.EntrySelected -= OnEntrySelected;
        }
    }

    public void AddData(
        IEnumerable<MissionContext> missionContexts,
        IEnumerable<Mission> specialMissions,
        IEnumerable<StrategicCombatResult> strategicCombatResults,
        IEnumerable<ConstructionProgressReport> constructionReports = null,
        IEnumerable<FortificationTransferReport> fortificationTransfers = null,
        IEnumerable<GovernorRequestReport> governorRequestReports = null,
        RecruitmentTurnReport recruitmentReport = null)
    {
        _missionContexts = (missionContexts ?? Enumerable.Empty<MissionContext>()).ToList();
        _reportEntries = BuildReportEntries(
            _missionContexts, specialMissions, strategicCombatResults, constructionReports,
            fortificationTransfers, governorRequestReports, recruitmentReport);
        _view.SetReport(_reportEntries);
    }

    private void OnEntrySelected(object sender, int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= _reportEntries.Count)
        {
            return;
        }

        EndOfTurnReportEntry entry = _reportEntries[entryIndex];
        if (!entry.CanOpenDebrief)
        {
            return;
        }

        ShowMissionDebrief(entry);
    }

    // Reads only entry-level data (no MissionContext) so NPC entries can open a debrief - built from
    // redacted, entry-owned lines - without ever exposing the underlying MissionContext to the view.
    private void ShowMissionDebrief(EndOfTurnReportEntry entry)
    {
        if (_missionDebriefDialog == null)
        {
            PackedScene scene = GD.Load<PackedScene>("res://Scenes/MissionDebriefDialog.tscn");
            _missionDebriefDialog = (MissionDebriefDialogController)scene.Instantiate();
            _missionDebriefDialog.CloseButtonPressed += (s, e) =>
            {
                _missionDebriefDialog.Visible = false;
                _view.Visible = true;
            };
            _missionDebriefDialog.BattleReviewRequested += OnBattleReviewRequested;
            AddChild(_missionDebriefDialog);
        }

        _missionDebriefDialog.SetMissionDebrief(
            entry.Title,
            entry.Subtitle,
            entry.OutcomeStatus,
            entry.Summary,
            entry.DebriefLines);
        _view.Visible = false;
        _missionDebriefDialog.Visible = true;
    }

    private void OnBattleReviewRequested(object sender, BattleHistory battleHistory)
    {
        if (battleHistory == null)
        {
            return;
        }

        if (_battleReviewDialog == null)
        {
            PackedScene scene = GD.Load<PackedScene>("res://Scenes/BattleReviewScreen/battle_review_screen.tscn");
            _battleReviewDialog = (BattleReviewController)scene.Instantiate();
            _battleReviewDialog.CloseButtonPressed += (s, e) =>
            {
                _battleReviewDialog.Visible = false;
                _missionDebriefDialog.Visible = true;
            };
            AddChild(_battleReviewDialog);
        }

        _battleReviewDialog.LoadNewHistory(battleHistory);
        _missionDebriefDialog.Visible = false;
        _battleReviewDialog.Visible = true;
    }

    private static List<EndOfTurnReportEntry> BuildReportEntries(
        IReadOnlyList<MissionContext> missionContexts,
        IEnumerable<Mission> specialMissions,
        IEnumerable<StrategicCombatResult> strategicCombatResults,
        IEnumerable<ConstructionProgressReport> constructionReports,
        IEnumerable<FortificationTransferReport> fortificationTransfers,
        IEnumerable<GovernorRequestReport> governorRequestReports = null,
        RecruitmentTurnReport recruitmentReport = null)
    {
        List<EndOfTurnReportEntry> entries = [];
        HashSet<MissionContext> reportedContexts = [];

        foreach (MissionContext context in missionContexts)
        {
            if (!reportedContexts.Add(context)) continue;

            bool isPlayerRecon = context.Order?.Mission?.MissionType == MissionType.Recon
                && context.MissionSquads.Any(squad => squad?.Squad?.Faction?.IsPlayerFaction == true);
            if (isPlayerRecon)
            {
                List<MissionContext> orderElements = missionContexts
                    .Where(candidate => ReferenceEquals(candidate.Order, context.Order))
                    .ToList();
                foreach (MissionContext element in orderElements)
                {
                    reportedContexts.Add(element);
                }
                entries.Add(BuildPlayerReconEntry(orderElements));
                continue;
            }

            EndOfTurnReportEntry entry = BuildMissionEntry(context);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        // Construction resolves without a MissionContext, so its entries are built from the
        // progress records the turn carried out instead. Only the player's own works are reported;
        // NPC faction development is squad-less and never produces one of these.
        foreach (ConstructionProgressReport report in
            constructionReports ?? Enumerable.Empty<ConstructionProgressReport>())
        {
            if (!report.IsPlayerConstruction) continue;
            entries.Add(BuildConstructionEntry(report));
        }

        // Works the Chapter built and then marched away from. Reported so the player learns the
        // effort was not wasted - the garrison it was handed to still holds the position.
        foreach (FortificationTransferReport transfer in
            fortificationTransfers ?? Enumerable.Empty<FortificationTransferReport>())
        {
            if (!transfer.IsPlayerHandover) continue;
            entries.Add(BuildFortificationTransferEntry(transfer));
        }

        foreach (StrategicCombatResult result in strategicCombatResults ?? Enumerable.Empty<StrategicCombatResult>())
        {
            entries.Add(BuildStrategicCombatEntry(result));
        }

        foreach (Mission mission in specialMissions ?? Enumerable.Empty<Mission>())
        {
            Region region = mission.RegionFaction?.Region;
            string location = region == null ? "Unknown location" : $"{region.Name}, {region.Planet?.Name}";
            entries.Add(new EndOfTurnReportEntry(
                "New Opportunity",
                $"{mission.MissionType} in {location}",
                $"Intelligence has identified a {mission.MissionType} opportunity.",
                false,
                null));
        }

        // Governor requests previously arrived, were met, and lapsed in complete silence - the
        // only trace was an unexplained standing opinion penalty. They resolve inside the
        // planetary sim without a MissionContext, so they are carried out of the turn separately.
        foreach (GovernorRequestReport report in
            governorRequestReports ?? Enumerable.Empty<GovernorRequestReport>())
        {
            entries.Add(BuildGovernorRequestEntry(report));
        }
        if (recruitmentReport != null)
        {
            entries.Add(BuildRecruitmentEntry(recruitmentReport));
        }

        if (entries.Count == 0)
        {
            entries.Add(new EndOfTurnReportEntry(
                "No Reports",
                "No mission activity this turn",
                "The sector is quiet, or no actionable reports reached command.",
                false,
                null));
        }

        return entries;
    }

    private static EndOfTurnReportEntry BuildRecruitmentEntry(
        RecruitmentTurnReport report)
    {
        if (!report.Processed)
        {
            return new EndOfTurnReportEntry(
                "Recruitment Paused",
                "10th Company recruitment program",
                report.PausedReason ?? "The program made no progress this week.",
                false,
                "PAUSED");
        }

        string summary =
            $"{report.RequisitionSpent:N0} Requisition spent; "
            + $"{report.ScreenedCandidates:N0} screened, "
            + $"{report.QualifiedCandidates:N0} qualified, and "
            + $"{report.AspirantsAdmitted:N0} admitted. "
            + $"{report.ImplantationsCompleted:N0} implantation phase"
            + $"{(report.ImplantationsCompleted == 1 ? "" : "s")} completed.";
        if (report.AspirantDeaths > 0 || report.CandidatesAgedOut > 0)
        {
            summary += $" Losses: {report.AspirantDeaths:N0} dead and "
                + $"{report.CandidatesAgedOut:N0} candidate"
                + $"{(report.CandidatesAgedOut == 1 ? "" : "s")} aged out.";
        }
        return new EndOfTurnReportEntry(
            "Recruitment",
            "10th Company recruitment program",
            summary,
            false,
            "PROCESSED");
    }

    private static EndOfTurnReportEntry BuildGovernorRequestEntry(GovernorRequestReport report)
    {
        IRequest request = report.Request;
        string governor = request?.Requester?.Name ?? "An unnamed governor";
        string planet = request?.TargetPlanet?.Name ?? "an unknown world";
        string subtitle = $"{governor}, Governor of {planet}";

        switch (report.Kind)
        {
            case GovernorRequestReportKind.Fulfilled:
                return new EndOfTurnReportEntry(
                    "Request Fulfilled",
                    subtitle,
                    $"The commitment is discharged. {governor} has pledged "
                    + $"{request.OfferedRequisition:N0} Requisition"
                    + (request.OfferedScheduleKind == PledgeScheduleKind.Standing
                        ? $" every {request.OfferedCadenceWeeks} weeks."
                        : ".")
                    + " Their regard for the Chapter has risen.",
                    false,
                    null);

            case GovernorRequestReportKind.Failed:
                return new EndOfTurnReportEntry(
                    "Request Failed",
                    subtitle,
                    $"{report.FailureReason} The pledged "
                    + $"{request.OfferedRequisition:N0} Requisition is forfeit, and "
                    + $"{governor}'s regard for the Chapter has fallen.",
                    false,
                    null);

            default:
                return new EndOfTurnReportEntry(
                    "Governor's Request",
                    subtitle,
                    BuildRequestArrivalSummary(request),
                    false,
                    null);
        }
    }

    private static string BuildRequestArrivalSummary(IRequest request)
    {
        string commitment =
            $"{request.Commitment.PackageCount} "
            + $"{request.Commitment.DisplayUnitName}"
            + (request.Commitment.PackageCount == 1 ? "" : "s")
            + $" for {request.Commitment.ServiceWeeks} weeks";
        string concern = request.ThreatFaction != null
            ? $"{request.ThreatFaction.Name} are in open revolt"
            : "an unverified threat is reported";
        string ask = request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed
            ? "Suppress the threat before the deadline."
            : "Assign squads to the Show of Force order posted in the capital region.";
        return $"A petition has reached the Chapter: {concern}. The governor asks for "
            + $"{commitment}, by {FormatDate(request.Deadline)}, and offers "
            + $"{request.OfferedRequisition:N0} Requisition. {ask} "
            + "Full terms are on the Diplomacy screen.";
    }

    private static string FormatDate(Date date) =>
        date == null ? "an unstated date" : $"{date.Year:000}.M{date.Millenium} (week {date.Week})";

    private static EndOfTurnReportEntry BuildPlayerReconEntry(
        IReadOnlyList<MissionContext> elementContexts)
    {
        MissionContext first = elementContexts[0];
        Mission mission = first.Order.Mission;
        Region region = mission.RegionFaction?.Region;
        string location = region == null ? "Unknown location" : $"{region.Name}, {region.Planet?.Name}";
        List<string> squadNames = elementContexts
            .SelectMany(context => context.MissionSquads)
            .Select(squad => squad?.Squad?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();
        string subtitle = MissionReportHeadlineBuilder.Build(
            MissionType.Recon,
            squadNames,
            mission.RegionFaction?.PlanetFaction?.Faction?.Name,
            region?.Name,
            region?.Planet?.Name);
        ReconOperationReport report = ReconOperationReportBuilder.Build(elementContexts, location);
        IReadOnlyList<MissionDebriefLine> lines = elementContexts
            .SelectMany(context => context.DebriefLines.Count > 0
                ? context.DebriefLines
                : context.Log.Select(line => new MissionDebriefLine(line)))
            .OrderBy(line => line.Day ?? ushort.MaxValue)
            .ThenBy(line => line.SquadName)
            .ToList();

        return new EndOfTurnReportEntry(
            "Recon",
            subtitle,
            report.Summary,
            true,
            report.OutcomeStatus,
            lines);
    }

    private static EndOfTurnReportEntry BuildMissionEntry(MissionContext context)
    {
        Mission mission = context.Order?.Mission;
        Region region = mission?.RegionFaction?.Region;
        string location = region == null ? "Unknown location" : $"{region.Name}, {region.Planet?.Name}";
        bool actingFactionIsPlayer = context.MissionSquads
            .Any(squad => squad?.Squad?.Faction?.IsPlayerFaction == true);
        string attacker = context.MissionSquads
            .Select(squad => squad?.Squad?.Faction?.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Unknown attacker";
        string defender = mission?.RegionFaction?.PlanetFaction?.Faction?.Name ?? "Unknown defender";

        if (actingFactionIsPlayer)
        {
            string missionTypeName = mission?.MissionType.ToString() ?? "Mission";
            string subtitle = MissionReportHeadlineBuilder.Build(
                mission?.MissionType ?? MissionType.Patrol,
                context.MissionSquads
                    .Select(squad => squad?.Squad?.Name)
                    .ToList(),
                defender,
                region?.Name,
                region?.Planet?.Name);
            MissionOutcomeClassification classification = MissionOutcomeClassifier.Classify(context);
            string summary = MissionReportSummaryBuilder.BuildSummary(classification, location)
                + MissionReportSummaryBuilder.BuildFriendlyCasualtyLine(classification)
                + MissionReportSummaryBuilder.BuildFieldCareLine(classification);
            string outcomeStatus = MissionReportSummaryBuilder.BuildOutcomeStatus(classification);
            IReadOnlyList<MissionDebriefLine> lines = context.DebriefLines.Count > 0
                ? context.DebriefLines
                : context.Log.Select(line => new MissionDebriefLine(line)).ToList();

            return new EndOfTurnReportEntry(
                missionTypeName, subtitle, summary, true, outcomeStatus, lines);
        }

        // NPC-run mission: never surface the ground-truth mission type or the full debrief log - only
        // what the player could plausibly have gathered from a sighting, a visible aftermath effect,
        // ambient regional surveillance, or direct engagement (NpcMissionReportBuilder). CanOpenDebrief
        // is false except when the player's own squads fought this mission's force directly (below);
        // that closes the old full-mission-log leak while still letting real battles be reviewed.
        MissionOutcomeClassification npcClassification = MissionOutcomeClassifier.Classify(context);
        bool spotterIsPlayerSide = FactionDispositionService.IsImperial(context.Spotter?.PlanetFaction?.Faction);
        bool targetIsPlayerSide = FactionDispositionService.IsImperial(mission?.RegionFaction?.PlanetFaction?.Faction);
        bool playerForcesEngaged = context.OpposingSquads?.Any(squad => squad?.IsPlayerSquad == true) == true;
        float playerVisibleIntel = region?.GetPlayerVisibleIntel() ?? 0f;

        NpcMissionReport report = NpcMissionReportBuilder.Build(
            npcClassification,
            spotterIsPlayerSide,
            targetIsPlayerSide,
            playerForcesEngaged,
            attacker,
            defender,
            location,
            playerVisibleIntel);

        if (report == null)
        {
            return null;
        }

        // The player's soldiers fought real battles here - the same BattleHistory a player mission
        // would show - so those specific battles may be opened for review. Only the battle-bearing
        // lines pass through: they contain casualty totals and the player's dead/injured roster.
        // The non-battle lines narrate the enemy's actual intent and are filtered out here.
        List<MissionDebriefLine> battleLines = context.DebriefLines.Where(line => line.HasBattle).ToList();
        bool canOpenDebrief = playerForcesEngaged && battleLines.Count > 0;
        string engagementStatus = canOpenDebrief ? "ENGAGEMENT REPORT" : "";
        IReadOnlyList<MissionDebriefLine> debriefLines = canOpenDebrief
            ? battleLines
            : Array.Empty<MissionDebriefLine>();

        return new EndOfTurnReportEntry(
            report.Title, report.Subtitle, report.Summary, canOpenDebrief, engagementStatus, debriefLines,
            isEnemyActivity: true);
    }

    // No debrief to open: construction has no narrative log, and everything the player needs (the
    // levels, the week's output, and the projection to the next visible rating) is in the summary.
    private static EndOfTurnReportEntry BuildConstructionEntry(ConstructionProgressReport report)
    {
        Region region = report.RegionFaction?.Region;
        string location = region == null ? "Unknown location" : $"{region.Name}, {region.Planet?.Name}";
        // Read live, after the whole turn has resolved, so the figure quoted here is the same one
        // the region dossier will show when the player goes to look at it. Construction resolves in
        // the mission phase; allied building and the planetary sim both move it afterwards.
        double sharedLevelNow = report.RegionFaction == null
            ? report.LevelAfter
            : RegionDefenses.GetShared(report.RegionFaction, report.ConstructionType);

        return new EndOfTurnReportEntry(
            ConstructionReportBuilder.BuildTitle(),
            ConstructionReportBuilder.BuildSubtitle(report, location),
            ConstructionReportBuilder.BuildSummary(report, location, sharedLevelNow),
            false,
            ConstructionReportBuilder.BuildOutcomeStatus(report, sharedLevelNow));
    }

    private static EndOfTurnReportEntry BuildFortificationTransferEntry(FortificationTransferReport transfer)
    {
        Region region = transfer.Region;
        string location = region == null ? "Unknown location" : $"{region.Name}, {region.Planet?.Name}";
        string inheritor = transfer.To?.Name ?? "local forces";
        string rating = RegionFactionExtensions.GetDefenseLevelDescription(transfer.SharedEntrenchment);

        return new EndOfTurnReportEntry(
            "Fortifications",
            $"Works in {location} handed to {inheritor}",
            $"With no Chapter forces left to man them, your works in {location} passed to {inheritor}. "
                + $"The position still stands at {rating}.",
            false,
            "HANDED OVER");
    }

    private static EndOfTurnReportEntry BuildStrategicCombatEntry(StrategicCombatResult result)
    {
        RegionFaction target = result.Target;
        Region region = target?.Region;
        string location = region == null ? "Unknown region" : $"{region.Name}, {region.Planet?.Name}";
        string attacker = result.Attacker?.Name ?? "Unknown attacker";
        string defender = target?.PlanetFaction?.Faction?.Name ?? "Unknown defender";
        float playerVisibleIntel = region?.GetPlayerVisibleIntel() ?? 0f;

        if (playerVisibleIntel <= 0f)
        {
            // No evidence at all: don't name either faction or imply anything about scale/outcome,
            // just that something happened nearby (mirrors NpcMissionReportBuilder's Movement tier).
            return new EndOfTurnReportEntry(
                "Distant Fighting",
                $"Enemy activity - {location}",
                $"Reports of fighting in {location} have reached command.",
                false,
                isEnemyActivity: true);
        }

        string outcome = result.Outcome switch
        {
            StrategicCombatOutcome.DefenderHeld => $"{defender} held the region.",
            StrategicCombatOutcome.Raided => $"{attacker} raided the region and withdrew.",
            StrategicCombatOutcome.InvaderFoothold => $"{attacker} established a foothold.",
            StrategicCombatOutcome.AttackerDestroyed => $"{attacker} was destroyed.",
            _ => "Combat resolved."
        };
        string summary = outcome;
        // Precise loss figures require confirmed identification of the forces involved, not just
        // ambient awareness that a fight happened - same tier NpcMissionReportBuilder uses to unlock
        // naming the acting faction in its Contact channel.
        if (NpcMissionReportBuilder.GetTier(playerVisibleIntel) >= NpcReportTier.Identified)
        {
            summary += $" Attacker losses: {result.AttackerLosses}. Defender losses: {result.DefenderLosses}.";
        }

        return new EndOfTurnReportEntry(
            "Strategic Combat",
            $"{attacker} vs {defender} - {location}",
            summary,
            false,
            isEnemyActivity: true);
    }
}

public sealed class EndOfTurnReportEntry
{
    public string Title { get; }
    public string Subtitle { get; }
    public string Summary { get; }
    public bool CanOpenDebrief { get; }
    public bool IsEnemyActivity { get; }
    // Computed once at entry-build time so ShowMissionDebrief never needs to read a MissionContext -
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
