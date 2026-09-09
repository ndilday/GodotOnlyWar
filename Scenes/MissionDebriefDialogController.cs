using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;

public partial class MissionDebriefDialogController : DialogController
{
    private MissionDebriefDialogView _view;

    public event EventHandler<Guid> BattleReviewRequested;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<MissionDebriefDialogView>("DialogView");
        _view.BattleReviewRequested += (s, replayId) =>
            BattleReviewRequested?.Invoke(this, replayId);
    }

    public void SetMissionDebrief(string title, string subtitle, string outcomeStatus,
        string outcomeSummary, IReadOnlyList<MissionDebriefLineView> lines)
    {
        _view.SetMissionDebrief(title, subtitle, outcomeStatus, outcomeSummary, lines);
    }
}
