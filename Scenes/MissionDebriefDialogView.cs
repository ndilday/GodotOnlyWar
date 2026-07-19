using Godot;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;

public partial class MissionDebriefDialogView : DialogView
{
    private Label _titleLabel;
    private Label _subtitleLabel;
    private Label _outcomeLabel;
    private Label _outcomeSummary;
    private ScrollContainer _stepScroll;
    private VBoxContainer _lineList;

    public event EventHandler<BattleHistory> BattleReviewRequested;

    public override void _Ready()
    {
        base._Ready();
        _titleLabel = GetNode<Label>("DebriefPanel/DebriefMargin/Layout/HeaderPanel/HeaderMargin/HeaderStack/TitleLabel");
        _subtitleLabel = GetNode<Label>("DebriefPanel/DebriefMargin/Layout/HeaderPanel/HeaderMargin/HeaderStack/SubtitleLabel");
        _outcomeLabel = GetNode<Label>("DebriefPanel/DebriefMargin/Layout/OutcomeLabel");
        _outcomeSummary = GetNode<Label>("DebriefPanel/DebriefMargin/Layout/OutcomeSummary");
        _stepScroll = GetNode<ScrollContainer>("DebriefPanel/DebriefMargin/Layout/ScrollContainer");
        _lineList = GetNode<VBoxContainer>("DebriefPanel/DebriefMargin/Layout/ScrollContainer/LineList");
        OnlyWarStyle.ApplyContentPanel(GetNode<Panel>("DebriefPanel"));
        OnlyWarStyle.ApplyInsetPanel(GetNode<Panel>("DebriefPanel/DebriefMargin/Layout/HeaderPanel"));
    }

    public void SetMissionDebrief(string title, string subtitle, string outcomeStatus,
        string outcomeSummary, IReadOnlyList<MissionDebriefLine> lines)
    {
        _titleLabel.Text = (title ?? "Mission Debrief").ToUpperInvariant();
        _subtitleLabel.Text = subtitle ?? "";
        _outcomeLabel.Text = outcomeStatus ?? "MISSION OUTCOME";
        _outcomeSummary.Text = outcomeSummary ?? "";
        _stepScroll.Visible = true;
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

        if (line.HasBattle)
        {
            BattleDebriefReport report = line.BattleReport ?? BattleDebriefReportBuilder.Build(line.BattleHistory);
            Label summary = new()
            {
                Text = $"Friendly dead: {report.PlayerDeaths}    Opposing dead: {report.OpposingDeaths}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            summary.AddThemeColorOverride("font_color",
                report.PlayerDeaths > 0 ? OnlyWarStyle.MedicalWarning : OnlyWarStyle.MutedText);
            stack.AddChild(summary);

            HBoxContainer controls = new();
            controls.AddThemeConstantOverride("separation", 8);

            Button casualtyButton = new()
            {
                Text = "DEAD & INJURED",
                CustomMinimumSize = new Vector2(190, 34),
                TooltipText = "Show Chapter casualties and recovery requirements"
            };
            VBoxContainer casualtyList = null;
            casualtyButton.Pressed += () =>
            {
                if (casualtyList == null)
                {
                    casualtyList = BuildCasualtyList(report);
                    stack.AddChild(casualtyList);
                }
                else
                {
                    casualtyList.Visible = !casualtyList.Visible;
                }

                casualtyButton.Text = casualtyList.Visible ? "HIDE DEAD & INJURED" : "DEAD & INJURED";
            };
            controls.AddChild(casualtyButton);

            Button reviewButton = new()
            {
                Text = "VIEW BATTLE",
                CustomMinimumSize = new Vector2(170, 34),
                TooltipText = "Open the battle replay for this engagement"
            };
            BattleHistory battleHistory = line.BattleHistory;
            reviewButton.Pressed += () => BattleReviewRequested?.Invoke(this, battleHistory);
            controls.AddChild(reviewButton);
            stack.AddChild(controls);
            _lineList.AddChild(panel);
            return;
        }

        RichTextLabel text = new()
        {
            Text = line.Text ?? "",
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        stack.AddChild(text);
        _lineList.AddChild(panel);
    }

    private static VBoxContainer BuildCasualtyList(BattleDebriefReport report)
    {
        VBoxContainer list = new();
        list.AddThemeConstantOverride("separation", 5);
        if (report.PlayerCasualties.Count == 0)
        {
            Label empty = new() { Text = "No player soldiers were wounded or killed in this engagement." };
            empty.AddThemeColorOverride("font_color", OnlyWarStyle.MedicalStable);
            list.AddChild(empty);
            return list;
        }

        foreach (BattleCasualtyEntry casualty in report.PlayerCasualties)
        {
            PanelContainer row = new();
            OnlyWarStyle.ApplyEventPanel(row,
                casualty.Disposition == BattleCasualtyDisposition.Dead
                    ? OnlyWarEventTone.Critical
                    : OnlyWarEventTone.Warning);
            HBoxContainer content = new();
            content.AddThemeConstantOverride("separation", 12);
            row.AddChild(content);

            VBoxContainer identity = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Label name = new()
            {
                Text = $"{casualty.Rank} {casualty.Name}",
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            Label assignment = new()
            {
                Text = $"{casualty.Squad}  •  {casualty.Company}",
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            assignment.AddThemeFontSizeOverride("font_size", 12);
            assignment.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            identity.AddChild(name);
            identity.AddChild(assignment);
            content.AddChild(identity);

            Label status = new()
            {
                Text = BuildCasualtyStatus(casualty),
                CustomMinimumSize = new Vector2(190, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            status.AddThemeColorOverride("font_color",
                casualty.Disposition == BattleCasualtyDisposition.Dead
                    ? OnlyWarStyle.Critical
                    : OnlyWarStyle.MedicalWarning);
            content.AddChild(status);
            list.AddChild(row);
        }

        return list;
    }

    private static string BuildCasualtyStatus(BattleCasualtyEntry casualty)
    {
        return casualty.Disposition switch
        {
            BattleCasualtyDisposition.Dead => "DEAD",
            BattleCasualtyDisposition.ReplacementRequired => "LIMB REPLACEMENT REQUIRED",
            _ => $"{casualty.RecoveryWeeks} {(casualty.RecoveryWeeks == 1 ? "WEEK" : "WEEKS")} RECOVERY"
        };
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
