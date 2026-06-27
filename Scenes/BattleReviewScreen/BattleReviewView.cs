using Godot;
using OnlyWar.Helpers.UI;
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
    private IReadOnlyList<BattleForceHierarchyNode> _currentForceHierarchy = Array.Empty<BattleForceHierarchyNode>();
    private readonly HashSet<string> _collapsedForceNodes = [];

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
        _currentForceHierarchy = forceHierarchy ?? Array.Empty<BattleForceHierarchyNode>();
        ClearContainer(_forceTreeVBox);
        foreach (BattleForceHierarchyNode node in _currentForceHierarchy)
        {
            AddForceNode(node, 0, "");
        }
    }

    private void AddForceNode(BattleForceHierarchyNode node, int depth, string parentKey)
    {
        string nodeKey = BuildForceNodeKey(node, parentKey);
        bool hasChildren = node.Children.Count > 0;
        bool isCollapsed = hasChildren && _collapsedForceNodes.Contains(nodeKey);
        Button row = new()
        {
            Text = BuildForceRowText(node, depth, isCollapsed),
            TooltipText = BuildForceRowTooltip(node, isCollapsed),
            Alignment = HorizontalAlignment.Left,
            Icon = IconAtlas.GetIcon(node.IconKey),
            IconAlignment = HorizontalAlignment.Left,
            ExpandIcon = false,
            CustomMinimumSize = new Vector2(0, node.FormationId.HasValue ? 34 : 30)
        };
        row.AddThemeConstantOverride("icon_max_width", node.FormationId.HasValue ? 22 : 20);
        row.AddThemeConstantOverride("h_separation", 6);
        Color accent = node.IsPlayerForce ? OnlyWarStyle.PlayerAccent : OnlyWarStyle.OpposingAccent;
        OnlyWarStyle.ApplyAccentButtonRow(row, node.IsSelected, accent);
        row.AddThemeColorOverride("font_color", accent);
        row.AddThemeColorOverride("font_hover_color", OnlyWarStyle.WithAlpha(accent.Lightened(0.28f), 1.0f));
        row.AddThemeColorOverride("font_pressed_color", OnlyWarStyle.Gold);
        if (hasChildren)
        {
            row.Pressed += () =>
            {
                if (!_collapsedForceNodes.Add(nodeKey))
                {
                    _collapsedForceNodes.Remove(nodeKey);
                }
                SetForceHierarchy(_currentForceHierarchy);
            };
        }
        else if (node.FormationId.HasValue)
        {
            int formationId = node.FormationId.Value;
            row.Pressed += () => FormationSelected?.Invoke(this, formationId);
        }
        _forceTreeVBox.AddChild(row);

        if (isCollapsed)
        {
            return;
        }

        foreach (BattleForceHierarchyNode child in node.Children)
        {
            AddForceNode(child, depth + 1, nodeKey);
        }
    }

    private static string BuildForceRowText(BattleForceHierarchyNode node, int depth, bool isCollapsed)
    {
        string indent = new(' ', depth * 3);
        string disclosure = node.Children.Count == 0 ? "   " : isCollapsed ? "[+] " : "[-] ";
        return $"{indent}{disclosure}{node.Title}  {node.CurrentStrength}/{node.StartingStrength}  Losses {Math.Max(0, node.Losses)}";
    }

    private static string BuildForceRowTooltip(BattleForceHierarchyNode node, bool isCollapsed)
    {
        string action = node.Children.Count == 0
            ? "Click to select formation"
            : isCollapsed ? "Click to expand" : "Click to collapse";
        return $"{node.Title}\n{node.Subtitle}\n{action}";
    }

    private static string BuildForceNodeKey(BattleForceHierarchyNode node, string parentKey)
    {
        string localKey = node.FormationId.HasValue
            ? $"formation:{node.FormationId.Value}"
            : $"{(node.IsPlayerForce ? "player" : "opposing")}:{node.Title}";
        return string.IsNullOrEmpty(parentKey) ? localKey : $"{parentKey}/{localKey}";
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
            label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            _effectsVBox.AddChild(label);
        }
    }

    private void SetEvents(IReadOnlyList<BattleEventEntry> events)
    {
        ClearContainer(_eventListVBox);
        foreach (BattleEventEntry entry in events)
        {
            PanelContainer panel = new();
            OnlyWarStyle.ApplyEventPanel(panel, GetEventTone(entry.Severity));
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
            actor.AddThemeColorOverride("font_color", OnlyWarStyle.PlayerAccent);
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
            OnlyWarStyle.ApplyAccentButtonRow(button, entry.IsSelected, OnlyWarStyle.PlayerAccent);
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
            label.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        }
        _casualtyGrid.AddChild(label);
    }

    private static Color GetSeverityColor(BattleEventSeverity severity)
    {
        return severity switch
        {
            BattleEventSeverity.Critical => OnlyWarStyle.Critical,
            BattleEventSeverity.Warning => OnlyWarStyle.MedicalWarning,
            _ => OnlyWarStyle.MutedText
        };
    }

    private static OnlyWarEventTone GetEventTone(BattleEventSeverity severity)
    {
        return severity switch
        {
            BattleEventSeverity.Critical => OnlyWarEventTone.Critical,
            BattleEventSeverity.Warning => OnlyWarEventTone.Warning,
            _ => OnlyWarEventTone.Normal
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
