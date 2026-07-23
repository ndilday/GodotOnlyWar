using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Battles;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BattleReviewView : DialogView
{
    private const float KeyboardPanSpeed = 520.0f;
    private const float WheelZoomFactor = 1.12f;
    private const float KeyboardZoomSpeed = 1.6f;
    private const float MinZoom = 0.05f;
    private const float MaxZoom = 3.0f;
    private Label _roundLabel;
    private VBoxContainer _forceTreeVBox;
    private SubViewportContainer _replayViewportContainer;
    private bool _isPanning;
    private Label _selectedNameLabel;
    private Label _selectedMetaLabel;
    private RichTextLabel _selectedStatsLabel;
    private VBoxContainer _activeWeaponSetsVBox;
    private VBoxContainer _effectsVBox;
    private ScrollContainer _eventScroll;
    private VBoxContainer _eventListVBox;
    private Button _previousRoundButton;
    private Button _stepBackButton;
    private Button _playPauseButton;
    private Button _stepForwardButton;
    private Button _nextRoundButton;
    private Button _speedButton;
    private IReadOnlyList<BattleForceHierarchyNode> _currentForceHierarchy = Array.Empty<BattleForceHierarchyNode>();
    private readonly HashSet<string> _collapsedForceNodes = [];

    public event EventHandler PreviousRoundPressed;
    public event EventHandler StepBackPressed;
    public event EventHandler PlayPausePressed;
    public event EventHandler StepForwardPressed;
    public event EventHandler NextRoundPressed;
    public event EventHandler SpeedPressed;
    public event EventHandler<int> FormationSelected;
    public event EventHandler<Vector2> ReplayPressed;

    public Node2D MapRoot { get; private set; }
    public Godot.Camera2D ReplayCamera { get; private set; }

    public override void _Ready()
    {
        base._Ready();
        _roundLabel = GetNode<Label>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/TitleRow/RoundLabel");
        _forceTreeVBox = GetNode<VBoxContainer>("Layout/LeftPanel/LeftMargin/LeftStack/ForceScroll/ForceTreeVBox");
        _selectedNameLabel = GetNode<Label>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/SelectedNameLabel");
        _selectedMetaLabel = GetNode<Label>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/SelectedMetaLabel");
        _selectedStatsLabel = GetNode<RichTextLabel>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/SelectedStatsLabel");
        _activeWeaponSetsVBox = GetNode<VBoxContainer>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/ActiveWeaponSetsVBox");
        _effectsVBox = GetNode<VBoxContainer>("Layout/RightPanel/SelectedPanel/SelectedMargin/SelectedStack/EffectsVBox");
        _eventScroll = GetNode<ScrollContainer>("Layout/RightPanel/EventPanel/EventMargin/EventStack/EventScroll");
        _eventListVBox = GetNode<VBoxContainer>("Layout/RightPanel/EventPanel/EventMargin/EventStack/EventScroll/EventListVBox");
        _previousRoundButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/PreviousRoundButton");
        _stepBackButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/StepBackButton");
        _playPauseButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/PlayPauseButton");
        _stepForwardButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/StepForwardButton");
        _nextRoundButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/NextRoundButton");
        _speedButton = GetNode<Button>("Layout/CenterPanel/HeaderPanel/HeaderMargin/HeaderStack/PlaybackRow/SpeedButton");
        MapRoot = GetNode<Node2D>("Layout/CenterPanel/ReplayPanel/SubViewportContainer/SubViewport/MapRoot");
        ReplayCamera = GetNode<Godot.Camera2D>("Layout/CenterPanel/ReplayPanel/SubViewportContainer/SubViewport/Camera2D");
        _replayViewportContainer = GetNode<SubViewportContainer>("Layout/CenterPanel/ReplayPanel/SubViewportContainer");
        _replayViewportContainer.TooltipText = "Right-drag or use WASD to pan. Mouse wheel or Q/E to zoom.";
        _replayViewportContainer.GuiInput += HandleReplayInput;

        _previousRoundButton.Pressed += () => PreviousRoundPressed?.Invoke(this, EventArgs.Empty);
        _stepBackButton.Pressed += () => StepBackPressed?.Invoke(this, EventArgs.Empty);
        _playPauseButton.Pressed += () => PlayPausePressed?.Invoke(this, EventArgs.Empty);
        _stepForwardButton.Pressed += () => StepForwardPressed?.Invoke(this, EventArgs.Empty);
        _nextRoundButton.Pressed += () => NextRoundPressed?.Invoke(this, EventArgs.Empty);
        _speedButton.Pressed += () => SpeedPressed?.Invoke(this, EventArgs.Empty);
    }

    private void HandleReplayInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right)
            {
                _isPanning = button.Pressed;
                _replayViewportContainer.AcceptEvent();
            }
            else if (button.Pressed && (button.ButtonIndex == MouseButton.WheelUp || button.ButtonIndex == MouseButton.WheelDown))
            {
                float factor = button.ButtonIndex == MouseButton.WheelUp ? WheelZoomFactor : 1.0f / WheelZoomFactor;
                ZoomAtPoint(button.Position, factor);
                _replayViewportContainer.AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Left)
            {
                ReplayPressed?.Invoke(this, ScreenToReplayPosition(button.Position));
                _replayViewportContainer.AcceptEvent();
            }
        }
        else if (inputEvent is InputEventMouseMotion motion && _isPanning)
        {
            ReplayCamera.Position -= motion.Relative / ReplayCamera.Zoom;
            _replayViewportContainer.AcceptEvent();
        }
    }

    private Vector2 ScreenToReplayPosition(Vector2 screenPoint)
    {
        Vector2 viewportSize = ReplayCamera.GetViewportRect().Size;
        Vector2 containerSize = _replayViewportContainer.Size;
        Vector2 viewportPoint = screenPoint;
        if (containerSize.X > 0.0f && containerSize.Y > 0.0f)
        {
            viewportPoint *= viewportSize / containerSize;
        }

        return ReplayCamera.Position + viewportPoint / ReplayCamera.Zoom;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (Input.IsKeyPressed(Key.E) ^ Input.IsKeyPressed(Key.Q))
        {
            float direction = Input.IsKeyPressed(Key.E) ? 1.0f : -1.0f;
            float factor = Mathf.Pow(KeyboardZoomSpeed, direction * (float)delta);
            ZoomAtPoint(_replayViewportContainer.Size / 2.0f, factor);
        }

        Vector2 panDirection = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) panDirection.Y -= 1.0f;
        if (Input.IsKeyPressed(Key.S)) panDirection.Y += 1.0f;
        if (Input.IsKeyPressed(Key.A)) panDirection.X -= 1.0f;
        if (Input.IsKeyPressed(Key.D)) panDirection.X += 1.0f;
        if (panDirection == Vector2.Zero) return;

        ReplayCamera.Position += panDirection.Normalized() * KeyboardPanSpeed * (float)delta / ReplayCamera.Zoom.X;
    }

    // Zooms by 'factor' while keeping the world point currently under 'screenPoint'
    // (in viewport-container pixels) fixed on screen. The camera uses FixedTopLeft
    // anchoring, so the world point under a screen pixel is Position + screenPoint / Zoom.
    private void ZoomAtPoint(Vector2 screenPoint, float factor)
    {
        float oldZoom = ReplayCamera.Zoom.X;
        float newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Mathf.IsEqualApprox(newZoom, oldZoom)) return;

        ReplayCamera.Position += screenPoint * (1.0f / oldZoom - 1.0f / newZoom);
        ReplayCamera.Zoom = new Vector2(newZoom, newZoom);
    }

    public void SetDisplay(BattleReplayDisplay display)
    {
        _roundLabel.Text = $"ROUND {display.CurrentTurnNumber} / {display.LastTurnNumber}";
        SetForceHierarchy(display.ForceHierarchy);
        SetSelectedFormation(display.SelectedFormation);
        SetEvents(display.CurrentTurnEvents);
    }

    public void SetPlaybackButtons(bool canGoBack, bool canGoForward, bool isPlaying, string speedLabel, bool canChangeSpeed)
    {
        _previousRoundButton.Disabled = !canGoBack;
        _stepBackButton.Disabled = !canGoBack;
        _stepForwardButton.Disabled = !canGoForward;
        _nextRoundButton.Disabled = !canGoForward;
        _playPauseButton.Disabled = !canGoForward && !isPlaying;
        _playPauseButton.Text = isPlaying ? "PAUSE" : "PLAY";
        _speedButton.Disabled = !canChangeSpeed;
        _speedButton.Text = speedLabel;
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
            ClipText = true,
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
        ClearContainer(_activeWeaponSetsVBox);
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
            $"[color=#f4d885]Losses:[/color] {summary.Losses} ({summary.LossPercent:P0})";

        foreach (BattleWeaponSetSummary weaponSet in summary.ActiveWeaponSets ?? Array.Empty<BattleWeaponSetSummary>())
        {
            Label label = new()
            {
                Text = $"{weaponSet.Name}  x{weaponSet.Count}",
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            _activeWeaponSetsVBox.AddChild(label);
        }

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
                Text = NormalizeEventText(entry.Text),
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            body.AddThemeFontSizeOverride("font_size", 12);
            stack.AddChild(body);
            _eventListVBox.AddChild(panel);
        }

        _eventScroll.ScrollVertical = 0;
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

    private static string NormalizeEventText(string text)
    {
        string normalizedText = (text ?? "").Replace("\r\n", "\n");
        return string.Join(
            "\n",
            normalizedText
                .Split('\n')
                .Select(line => string.Join(" ", line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)))
                .Where(line => !string.IsNullOrWhiteSpace(line)));
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
