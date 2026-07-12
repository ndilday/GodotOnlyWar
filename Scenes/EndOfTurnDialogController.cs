using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
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
        if (entry.MissionContext == null)
        {
            return;
        }

        ShowMissionDebrief(entry);
    }

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
            entry.MissionContext.DebriefLines.Count > 0
                ? entry.MissionContext.DebriefLines
                : entry.MissionContext.Log.Select(line => new MissionDebriefLine(line)).ToList());
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
            _battleReviewDialog.CloseButtonPressed += (s, e) => _battleReviewDialog.Visible = false;
            AddChild(_battleReviewDialog);
        }

        _battleReviewDialog.LoadNewHistory(battleHistory);
        _battleReviewDialog.Visible = true;
    }

    private static List<EndOfTurnReportEntry> BuildReportEntries(
        IReadOnlyList<MissionContext> missionContexts,
        IEnumerable<Mission> specialMissions,
        IEnumerable<StrategicCombatResult> strategicCombatResults)
    {
        List<EndOfTurnReportEntry> entries = [];

        foreach (MissionContext context in missionContexts)
        {
            entries.Add(BuildMissionEntry(context));
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

    private static EndOfTurnReportEntry BuildMissionEntry(MissionContext context)
    {
        Mission mission = context.Order?.Mission;
        Region region = mission?.RegionFaction?.Region;
        MissionType missionType = mission?.MissionType ?? MissionType.Patrol;
        string missionTypeName = mission?.MissionType.ToString() ?? "Mission";
        string location = region == null ? "Unknown location" : $"{region.Name}, {region.Planet?.Name}";
        string force = FormatMissionForce(context.MissionSquads);
        bool actingFactionIsPlayer = context.MissionSquads
            .Any(squad => squad?.Squad?.Faction?.IsPlayerFaction == true);
        string attacker = context.MissionSquads
            .Select(squad => squad?.Squad?.Faction?.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Unknown attacker";
        string defender = mission?.RegionFaction?.PlanetFaction?.Faction?.Name ?? "Unknown defender";

        // Mirror BuildStrategicCombatEntry's gating: the player's own missions are always shown in
        // full, but a mission run by an NPC faction only surfaces precise detail once the player has
        // some region intel - otherwise it degrades to an unconfirmed report, same as strategic combat.
        bool hasIntel = actingFactionIsPlayer || (region?.GetPlayerVisibleIntel() ?? 0f) > 0f;
        if (!hasIntel)
        {
            return new EndOfTurnReportEntry(
                missionTypeName,
                MissionReportSummaryBuilder.BuildUnconfirmedSubtitle(missionType, location),
                MissionReportSummaryBuilder.BuildUnconfirmedSummary(missionType, location),
                true,
                context);
        }

        string subtitle = $"{attacker} vs {defender}: {force} - {location}";
        MissionOutcomeClassification classification = MissionOutcomeClassifier.Classify(context);
        string summary = MissionReportSummaryBuilder.BuildSummary(
            classification,
            actingFactionIsPlayer,
            attacker,
            location);

        return new EndOfTurnReportEntry(missionTypeName, subtitle, summary, true, context);
    }

    private static EndOfTurnReportEntry BuildStrategicCombatEntry(StrategicCombatResult result)
    {
        RegionFaction target = result.Target;
        Region region = target?.Region;
        string location = region == null ? "Unknown region" : $"{region.Name}, {region.Planet?.Name}";
        string attacker = result.Attacker?.Name ?? "Unknown attacker";
        string defender = target?.PlanetFaction?.Faction?.Name ?? "Unknown defender";
        bool hasIntel = (region?.GetPlayerVisibleIntel() ?? 0f) > 0f;

        if (!hasIntel)
        {
            return new EndOfTurnReportEntry(
                "Unconfirmed Combat",
                $"{attacker} vs {defender} - {location}",
                $"You have received reports of combat between {attacker} and {defender} in {location}.",
                false,
                null);
        }

        string outcome = result.Outcome switch
        {
            StrategicCombatOutcome.DefenderHeld => $"{defender} held the region.",
            StrategicCombatOutcome.Raided => $"{attacker} raided the region and withdrew.",
            StrategicCombatOutcome.InvaderFoothold => $"{attacker} established a foothold.",
            StrategicCombatOutcome.AttackerDestroyed => $"{attacker} was destroyed.",
            _ => "Combat resolved."
        };
        string summary =
            $"{outcome} Attacker losses: {result.AttackerLosses}. Defender losses: {result.DefenderLosses}.";

        return new EndOfTurnReportEntry(
            "Strategic Combat",
            $"{attacker} vs {defender} - {location}",
            summary,
            false,
            null);
    }

    private static string FormatMissionForce(IReadOnlyList<BattleSquad> squads)
    {
        if (squads == null || squads.Count == 0)
        {
            return "Unassigned force";
        }

        List<string> names = squads
            .Select(squad => squad?.Squad?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .Take(3)
            .ToList();
        if (names.Count == 0)
        {
            return "Mission force";
        }

        string suffix = squads.Count > names.Count ? $" +{squads.Count - names.Count}" : "";
        return $"{string.Join(", ", names)}{suffix}";
    }
}

public sealed class EndOfTurnReportEntry
{
    public string Title { get; }
    public string Subtitle { get; }
    public string Summary { get; }
    public bool CanOpenDebrief { get; }
    public MissionContext MissionContext { get; }

    public EndOfTurnReportEntry(
        string title,
        string subtitle,
        string summary,
        bool canOpenDebrief,
        MissionContext missionContext)
    {
        Title = title ?? "";
        Subtitle = subtitle ?? "";
        Summary = summary ?? "";
        CanOpenDebrief = canOpenDebrief;
        MissionContext = missionContext;
    }
}
