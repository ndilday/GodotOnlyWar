using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shows the turn report the application built. Since SB-11b this dialog decides nothing about
/// what the player is told: it receives finished cards and only handles selection, the debrief and
/// the battle replay it opens.
/// </summary>
public partial class EndOfTurnDialogController : DialogController
{
    private EndOfTurnDialogView _view;
    private MissionDebriefDialogController _missionDebriefDialog;
    private BattleReviewController _battleReviewDialog;
    private List<EndOfTurnReportEntry> _reportEntries = [];
    private IMainScreenApplication _application;

    // Raised with a planet id when the player asks to be taken to a card's world. The host owns
    // closing the report and every other open surface, then centring the map.
    public event EventHandler<int> PlanetGoToRequested;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<EndOfTurnDialogView>("DialogView");
        _view.EntrySelected += OnEntrySelected;
        _view.EntryGoToRequested += OnEntryGoToRequested;
    }

    public override void _ExitTree()
    {
        if (_view != null)
        {
            _view.EntrySelected -= OnEntrySelected;
            _view.EntryGoToRequested -= OnEntryGoToRequested;
        }
    }

    private void OnEntryGoToRequested(object sender, int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= _reportEntries.Count)
        {
            return;
        }

        int? planetId = _reportEntries[entryIndex].PlanetId;
        if (planetId.HasValue)
        {
            PlanetGoToRequested?.Invoke(this, planetId.Value);
        }
    }

    public void Configure(IMainScreenApplication application) =>
        _application = application ?? throw new ArgumentNullException(nameof(application));

    /// <summary>
    /// Renders a report projection. A restored snapshot's debriefs intentionally have no battle
    /// replay reference, so the view can show casualty details but cannot offer a replay that was
    /// not persisted.
    /// </summary>
    public void SetReport(TurnReportView report)
    {
        _reportEntries = report?.Entries?.ToList() ?? [];
        _view.SetReport(_reportEntries, report?.ResolvedDateLabel, report?.EmptyMessage);
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

    // Reads only entry-level data, so an enemy-activity entry can open its (already redacted)
    // debrief without the dialog ever seeing the mission it was built from.
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

    private void OnBattleReviewRequested(object sender, Guid replayId)
    {
        if (replayId == Guid.Empty || _application == null)
        {
            return;
        }

        if (_application.QueryBattleReplay(new BattleReplayQuery(replayId, 0)) == null)
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

        _battleReviewDialog.Configure(_application);
        _battleReviewDialog.LoadNewReplay(replayId);
        _missionDebriefDialog.Visible = false;
        _battleReviewDialog.Visible = true;
    }
}
