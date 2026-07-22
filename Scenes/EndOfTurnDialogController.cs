using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
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
        IEnumerable<StrategicCombatResult> strategicCombatResults)
    {
        _missionContexts = (missionContexts ?? Enumerable.Empty<MissionContext>()).ToList();
        _reportEntries = BuildReportEntries(_missionContexts, specialMissions, strategicCombatResults);
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
        IEnumerable<StrategicCombatResult> strategicCombatResults)
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
            string summary = MissionReportSummaryBuilder.BuildSummary(classification, location);
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
        bool spotterIsPlayerSide = IsPlayerOrDefaultFaction(context.Spotter?.PlanetFaction?.Faction);
        bool targetIsPlayerSide = IsPlayerOrDefaultFaction(mission?.RegionFaction?.PlanetFaction?.Faction);
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

    private static bool IsPlayerOrDefaultFaction(Faction faction) =>
        faction != null && (faction.IsPlayerFaction || faction.IsDefaultFaction);

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
