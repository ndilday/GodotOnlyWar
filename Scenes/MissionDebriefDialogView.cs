using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;

public partial class MissionDebriefDialogView : DialogView
{
    private Label _titleLabel;
    private Label _subtitleLabel;
    private VBoxContainer _lineList;

    public event EventHandler<BattleHistory> BattleReviewRequested;

    public override void _Ready()
    {
        base._Ready();
        _titleLabel = GetNode<Label>("DebriefPanel/DebriefMargin/Layout/HeaderPanel/HeaderMargin/HeaderStack/TitleLabel");
        _subtitleLabel = GetNode<Label>("DebriefPanel/DebriefMargin/Layout/HeaderPanel/HeaderMargin/HeaderStack/SubtitleLabel");
        _lineList = GetNode<VBoxContainer>("DebriefPanel/DebriefMargin/Layout/ScrollContainer/LineList");
        OnlyWarStyle.ApplyContentPanel(GetNode<Panel>("DebriefPanel"));
        OnlyWarStyle.ApplyInsetPanel(GetNode<Panel>("DebriefPanel/DebriefMargin/Layout/HeaderPanel"));
    }

    public void SetMissionDebrief(string title, string subtitle, IReadOnlyList<MissionDebriefLine> lines)
    {
        _titleLabel.Text = (title ?? "Mission Debrief").ToUpperInvariant();
        _subtitleLabel.Text = subtitle ?? "";
        ClearLines();

        if (lines == null || lines.Count == 0)
        {
            AddTextLine(new MissionDebriefLine("No debrief lines were recorded for this mission."));
            return;
        }

        foreach (MissionDebriefLine line in lines)
        {
            AddTextLine(line);
        }
    }

    private void AddTextLine(MissionDebriefLine line)
    {
        PanelContainer panel = new();
        OnlyWarStyle.ApplyEventPanel(panel, line.HasBattle ? OnlyWarEventTone.Warning : OnlyWarEventTone.Normal);

        MarginContainer margin = new();
        panel.AddChild(margin);

        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 6);
        margin.AddChild(stack);

        RichTextLabel text = new()
        {
            Text = line.Text ?? "",
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        stack.AddChild(text);

        if (line.HasBattle)
        {
            Button reviewButton = new()
            {
                Text = "REVIEW BATTLE",
                CustomMinimumSize = new Vector2(170, 34),
                TooltipText = "Open the battle replay for this engagement"
            };
            BattleHistory battleHistory = line.BattleHistory;
            reviewButton.Pressed += () => BattleReviewRequested?.Invoke(this, battleHistory);
            stack.AddChild(reviewButton);
        }

        _lineList.AddChild(panel);
    }

    private void ClearLines()
    {
        foreach (Node child in _lineList.GetChildren())
        {
            _lineList.RemoveChild(child);
            child.QueueFree();
        }
    }
}
