using Godot;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;

public partial class MissionDebriefDialogController : DialogController
{
    private MissionDebriefDialogView _view;

    public event EventHandler<BattleHistory> BattleReviewRequested;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<MissionDebriefDialogView>("DialogView");
        _view.BattleReviewRequested += (s, history) => BattleReviewRequested?.Invoke(this, history);
    }

    public void SetMissionDebrief(string title, string subtitle, IReadOnlyList<MissionDebriefLine> lines)
    {
        _view.SetMissionDebrief(title, subtitle, lines);
    }
}
