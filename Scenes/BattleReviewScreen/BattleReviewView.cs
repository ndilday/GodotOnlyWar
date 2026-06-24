using Godot;
using OnlyWar.Models.Battles;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BattleReviewView : DialogView
{
    private Label _battleTitleLabel;
    private Label _roundLabel;
    private Label _phaseLabel;
    private Label _resultLabel;
    private VBoxContainer _forceTreeVBox;
    private HBoxContainer _timelineBox;
    private GridContainer _casualtyGrid;
    private Label _selectedNameLabel;
    private Label _selectedMetaLabel;
    private RichTextLabel _selectedStatsLabel;
    private VBoxContainer _effectsVBox;
    private VBoxContainer _eventListVBox;
    private Button _previousRoundButton;
    private Button _stepBackButton;
    private Button _playPauseButton;
    private Button _stepForwardButton;
    private Button _nextRoundButton;

    public event EventHandler PreviousRoundPressed;
    public event EventHandler StepBackPressed;
    public event EventHandler PlayPausePressed;
    public event EventHandler StepForwardPressed;
    public event EventHandler NextRoundPressed;
    public event EventHandler<int> FormationSelected;
    public event EventHandler<int> TimelineTurnSelected;

    public Node2D MapRoot { get; private set; }
    public Godot.Camera2D ReplayCamera { get; private set; }

    public override void _Ready()
    {
        base._Ready();
        _battleTitleLabel = GetNode<Label>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/TitleRow/BattleTitleLabel");
        _roundLabel = GetNode<Label>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/TitleRow/RoundLabel");
        _phaseLabel = GetNode<Label>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/TitleRow/PhaseLabel");
        _resultLabel = GetNode<Label>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/TitleRow/ResultLabel");
        _forceTreeVBox = GetNode<VBoxContainer>("Layout/LeftPanel/LeftMargin/LeftStack/ForceScroll/ForceTreeVBox");
        _timelineBox = GetNode<HBoxContainer>("Layout/CenterPanel/BottomPanel/BottomMargin/BottomStack/TimelineScroll/TimelineBox");
        _casualtyGrid = GetNode<GridContainer>("Layout/CenterPanel/BottomPanel/BottomMargin/BottomStack/CasualtyGrid");
        _selectedNameLabel = GetNode<Label>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/SelectedNameLabel");
        _selectedMetaLabel = GetNode<Label>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/SelectedMetaLabel");
        _selectedStatsLabel = GetNode<RichTextLabel>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/SelectedStatsLabel");
        _effectsVBox = GetNode<VBoxContainer>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/EffectsVBox");
        _eventListVBox = GetNode<VBoxContainer>("Layout/RightPanel/EventPanel/EventMargin/EventStack/EventScroll/EventListVBox");
        _previousRoundButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/PreviousRoundButton");
        _stepBackButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/StepBackButton");
        _playPauseButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/PlayPauseButton");
        _stepForwardButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/StepForwardButton");
        _nextRoundButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/NextRoundButton");
        MapRoot = GetNode<Node2D>("Layout/CenterPanel/ReplayPanel/SubViewportContainer/SubViewport/MapRoot");
        ReplayCamera = GetNode<Godot.Camera2D>("Layout/CenterPanel/ReplayPanel/SubViewportContainer/SubViewport/Camera2D");

        _previousRoundButton.Pressed += () => PreviousRoundPressed?.Invoke(this, EventArgs.Empty);
        _stepBackButton.Pressed += () => StepBackPressed?.Invoke(this, EventArgs.Empty);
        _playPauseButton.Pressed += () => PlayPausePressed?.Invoke(this, EventArgs.Empty);
        _stepForwardButton.Pressed += () => StepForwardPressed?.Invoke(this, EventArgs.Empty);
        _nextRoundButton.Pressed += () => NextRoundPressed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDisplay(BattleReplayDisplay display)
    {
        _battleTitleLabel.Text = display.BattleTitle;
        _roundLabel.Text = $"ROUND {display.CurrentTurnNumber} / {display.LastTurnNumber}";
        _phaseLabel.Text = display.PhaseLabel.ToUpperInvariant();
        _resultLabel.Text = display.ResultLabel.ToUpperInvariant();
        SetPlaybackButtons(display.CurrentTurnIndex > 0, display.CurrentTurnIndex < display.Timeline.Count - 1);
        SetForceHierarchy(display.ForceHierarchy);
        SetSelectedFormation(display.SelectedFormation);
        SetEvents(display.CurrentTurnEvents);
        SetTimeline(display.Timeline);
        SetCasualties(display.CasualtiesByRound);
    }

    public void SetPlaybackButtons(bool canGoBack, bool canGoForward)
    {
        _previousRoundButton.Disabled = !canGoBack;
        _stepBackButton.Disabled = !canGoBack;
        _stepForwardButton.Disabled = !canGoForward;
        _nextRoundButton.Disabled = !canGoForward;
        _playPauseButton.Disabled = !canGoForward;
    }

    private void SetForceHierarchy(IReadOnlyList<BattleForceHierarchyNode> forceHierarchy)
    {
        ClearContainer(_forceTreeVBox);
        foreach (BattleForceHierarchyNode node in forceHierarchy)
        {
            AddForceNode(node, 0);
        }
    }

    private void AddForceNode(BattleForceHierarchyNode node, int depth)
    {
        Button row = new()
        {
            Text = BuildForceRowText(node, depth),
            TooltipText = $"{node.Title}\n{node.Subtitle}",
            Disabled = !node.FormationId.HasValue,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, node.FormationId.HasValue ? 34 : 30)
        };
        row.AddThemeStyleboxOverride("normal", CreateRowStyle(node.IsSelected, node.IsPlayerForce, false));
        row.AddThemeStyleboxOverride("hover", CreateRowStyle(true, node.IsPlayerForce, true));
        row.AddThemeStyleboxOverride("pressed", CreateRowStyle(true, node.IsPlayerForce, true));
        row.AddThemeStyleboxOverride("disabled", CreateRowStyle(node.IsSelected, node.IsPlayerForce, false));
        row.AddThemeColorOverride("font_disabled_color", node.IsPlayerForce ? Color.Color8(106, 205, 222) : Color.Color8(212, 94, 82));
        if (node.FormationId.HasValue)
        {
            int formationId = node.FormationId.Value;
            row.Pressed += () => FormationSelected?.Invoke(this, formationId);
        }
        _forceTreeVBox.AddChild(row);

        foreach (BattleForceHierarchyNode child in node.Children)
        {
            AddForceNode(child, depth + 1);
        }
    }

    private static string BuildForceRowText(BattleForceHierarchyNode node, int depth)
    {
        string indent = new(' ', depth * 3);
        string marker = node.Children.Count > 0 ? "▸" : "•";
        return $"{indent}{marker} {node.Title}  {node.CurrentStrength}/{node.StartingStrength}  Losses {Math.Max(0, node.Losses)}";
    }

    private void SetSelectedFormation(BattleFormationSummary summary)
    {
        ClearContainer(_effectsVBox);
        if (summary == null)
        {
            _selectedNameLabel.Text = "No formation selected";
            _selectedMetaLabel.Text = "";
            _selectedStatsLabel.Text = "";
            return;
        }

        _selectedNameLabel.Text = summary.Name.ToUpperInvariant();
        _selectedMetaLabel.Text = $"{summary.FormationType}\n{summary.ForceName}\nCommander: {summary.CommanderName}";
        _selectedStatsLabel.Text =
            $"[color=#f4d885]Starting Strength:[/color] {summary.StartingStrength}\n" +
            $"[color=#f4d885]Current Strength:[/color] {summary.CurrentStrength}\n" +
            $"[color=#f4d885]Losses:[/color] {summary.Losses} ({summary.LossPercent:P0})\n" +
            $"[color=#f4d885]Fatigue:[/color] {summary.FatigueLabel}\n" +
            $"[color=#f4d885]Morale:[/color] {summary.MoraleLabel}\n" +
            $"[color=#f4d885]Ammunition:[/color] {summary.AmmunitionLabel}";

        foreach (string effect in summary.NotableEffects)
        {
            Label label = new()
            {
                Text = $"• {effect}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", Color.Color8(212, 188, 130));
            _effectsVBox.AddChild(label);
        }
    }

    private void SetEvents(IReadOnlyList<BattleEventEntry> events)
    {
        ClearContainer(_eventListVBox);
        foreach (BattleEventEntry entry in events)
        {
            PanelContainer panel = new();
            panel.AddThemeStyleboxOverride("panel", CreateEventStyle(entry.Severity));
            VBoxContainer stack = new();
            stack.AddThemeConstantOverride("separation", 2);
            panel.AddChild(stack);

            Label header = new()
            {
                Text = $"{entry.Timestamp}  {entry.EventType.ToUpperInvariant()}",
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            header.AddThemeFontSizeOverride("font_size", 12);
            header.AddThemeColorOverride("font_color", GetSeverityColor(entry.Severity));
            stack.AddChild(header);

            Label actor = new()
            {
                Text = $"{entry.ActorName} - {entry.FormationName}",
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            actor.AddThemeFontSizeOverride("font_size", 12);
            actor.AddThemeColorOverride("font_color", Color.Color8(150, 212, 222));
            stack.AddChild(actor);

            Label body = new()
            {
                Text = CollapseWhitespace(entry.Text),
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            body.AddThemeFontSizeOverride("font_size", 12);
            stack.AddChild(body);
            _eventListVBox.AddChild(panel);
        }
    }

    private void SetTimeline(IReadOnlyList<BattleTimelineEntry> timeline)
    {
        ClearContainer(_timelineBox);
        foreach (BattleTimelineEntry entry in timeline)
        {
            Button button = new()
            {
                Text = entry.Label,
                TooltipText = entry.Summary,
                CustomMinimumSize = new Vector2(54, 32)
            };
            button.AddThemeStyleboxOverride("normal", CreateRowStyle(entry.IsSelected, true, false));
            button.AddThemeStyleboxOverride("hover", CreateRowStyle(true, true, true));
            int turnIndex = entry.TurnIndex;
            button.Pressed += () => TimelineTurnSelected?.Invoke(this, turnIndex);
            _timelineBox.AddChild(button);
        }
    }

    private void SetCasualties(IReadOnlyList<BattleCasualtyRoundSummary> casualties)
    {
        ClearContainer(_casualtyGrid);
        AddCasualtyCell("Round", true);
        AddCasualtyCell("Player", true);
        AddCasualtyCell("Enemy", true);
        AddCasualtyCell("P Cum.", true);
        AddCasualtyCell("E Cum.", true);

        foreach (BattleCasualtyRoundSummary summary in casualties.TakeLast(12))
        {
            AddCasualtyCell(summary.TurnNumber.ToString(), false);
            AddCasualtyCell(summary.PlayerLossesThisRound.ToString(), false);
            AddCasualtyCell(summary.OpposingLossesThisRound.ToString(), false);
            AddCasualtyCell(summary.PlayerCumulativeLosses.ToString(), false);
            AddCasualtyCell(summary.OpposingCumulativeLosses.ToString(), false);
        }
    }

    private void AddCasualtyCell(string text, bool isHeader)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(70, 18)
        };
        label.AddThemeFontSizeOverride("font_size", isHeader ? 12 : 11);
        if (isHeader)
        {
            label.AddThemeColorOverride("font_color", Color.Color8(244, 216, 133));
        }
        _casualtyGrid.AddChild(label);
    }

    private static StyleBoxFlat CreateRowStyle(bool selected, bool isPlayerForce, bool hover)
    {
        Color accent = isPlayerForce ? Color.Color8(68, 183, 205) : Color.Color8(202, 72, 58);
        return new StyleBoxFlat
        {
            BgColor = selected
                ? new Color(accent.R, accent.G, accent.B, hover ? 0.24f : 0.18f)
                : new Color(0.01f, 0.012f, 0.014f, hover ? 0.96f : 0.72f),
            BorderColor = selected ? accent : Color.Color8(84, 72, 46, 170),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 8,
            ContentMarginTop = 5,
            ContentMarginRight = 8,
            ContentMarginBottom = 5
        };
    }

    private static StyleBoxFlat CreateEventStyle(BattleEventSeverity severity)
    {
        Color border = GetSeverityColor(severity);
        return new StyleBoxFlat
        {
            BgColor = new Color(0.008f, 0.01f, 0.012f, 0.82f),
            BorderColor = new Color(border.R, border.G, border.B, severity == BattleEventSeverity.Normal ? 0.35f : 0.72f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 8,
            ContentMarginTop = 6,
            ContentMarginRight = 8,
            ContentMarginBottom = 6
        };
    }

    private static Color GetSeverityColor(BattleEventSeverity severity)
    {
        return severity switch
        {
            BattleEventSeverity.Critical => Color.Color8(213, 78, 66),
            BattleEventSeverity.Warning => Color.Color8(226, 171, 74),
            _ => Color.Color8(143, 128, 91)
        };
    }

    private static string CollapseWhitespace(string text)
    {
        return string.Join(" ", (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
