using Godot;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RegionScreenView : CommandWorkspaceView
{
    private Panel _legacyDataPanel;
    private Panel _legacySquadTreePanel;
    private Panel _legacyOrdersPanel;
    private Panel _boardPanel;
    private PanelContainer _shellContextPanel;
    private PanelContainer _shellCommandPanel;
    private PanelContainer _dossierPanel;

    private TacticalRegionController _centerRegionController;
    private TacticalRegionController _northRegionController;
    private TacticalRegionController _northeastRegionController;
    private TacticalRegionController _southeastRegionController;
    private TacticalRegionController _southRegionController;
    private TacticalRegionController _southwestRegionController;
    private TacticalRegionController _northwestRegionController;

    private Label _missionsHeaderLabel;
    private PanelContainer _missionsFlagPanel;
    private Label _missionsFlagLabel;
    private GridContainer _missionsListStack;
    private readonly List<Button> _missionButtons = [];
    private ButtonGroup _missionButtonGroup;

    private readonly Dictionary<Aggression, Button> _aggressionButtons = [];
    private ButtonGroup _aggressionButtonGroup;
    private OptionButton _targetFactionOption;
    private Button _assignButton;
    private Button _unassignButton;

    private Label _dossierTitleLabel;
    private VBoxContainer _dossierStack;
    private VBoxContainer _inboundOrdersStack;

    public event EventHandler<Region> AdjacentRegionClicked;
    public event EventHandler<Region> TargetRegionSelected;
    public event EventHandler<AvailableMission> MissionSelected;
    public event EventHandler<Aggression> AggressionChanged;
    public event EventHandler AssignPressed;
    public event EventHandler UnassignPressed;
    public event EventHandler<int> TargetFactionSelected;
    public event EventHandler<Order> InboundOrderActivated;

    public override void _Ready()
    {
        base._Ready();
        ClipContents = true;

        _legacyDataPanel = GetNodeOrNull<Panel>("DataPanel");
        _legacySquadTreePanel = GetNodeOrNull<Panel>("SquadTreePanel");
        _legacyOrdersPanel = GetNodeOrNull<Panel>("OrdersPanel");
        _boardPanel = GetNode<Panel>("RegionPanel");

        _centerRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionCenter");
        _northRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorth");
        _northeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNortheast");
        _southeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSoutheast");
        _southRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouth");
        _southwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouthwest");
        _northwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorthwest");

        ConnectHexSignals(_centerRegionController);
        ConnectHexSignals(_northRegionController);
        ConnectHexSignals(_northeastRegionController);
        ConnectHexSignals(_southeastRegionController);
        ConnectHexSignals(_southRegionController);
        ConnectHexSignals(_southwestRegionController);
        ConnectHexSignals(_northwestRegionController);

        HideLegacyPanels();
        BuildWorkspaceShell(0.755f, 0.785f);

        // The shared shell built a ContextPanel and CommandPanel we don't use for this screen's
        // mission-centric layout (a custom dossier and pinned commit bar replace them) - hide
        // rather than remove so the shell's contract with other screens (e.g. Planet Detail)
        // stays untouched.
        _shellContextPanel = GetNodeOrNull<PanelContainer>("ContextPanel");
        _shellCommandPanel = GetNodeOrNull<PanelContainer>("CommandPanel");
        if (_shellContextPanel != null) _shellContextPanel.Visible = false;
        if (_shellCommandPanel != null) _shellCommandPanel.Visible = false;

        ConfigureBoardPanel();
        CompactHexCluster();
        BuildMissionsSection();
        BuildCommitBar();
        BuildDossierPanel();
    }

    public void PopulateAdjacentRegions(Region centerRegion, Dictionary<string, Region> adjacentRegions, Region selectedTarget)
    {
        _centerRegionController.Populate(centerRegion, MapLayer.None, centerRegion == selectedTarget, showOverlays: false);

        void SetupAdjacentRegion(TacticalRegionController controller, string direction)
        {
            if (adjacentRegions.TryGetValue(direction, out Region region))
            {
                controller.Populate(region, MapLayer.None, region == selectedTarget, showOverlays: false);
                controller.Visible = true;
            }
            else
            {
                controller.Visible = false;
            }
        }

        SetupAdjacentRegion(_northRegionController, "N");
        SetupAdjacentRegion(_northeastRegionController, "NE");
        SetupAdjacentRegion(_southeastRegionController, "SE");
        SetupAdjacentRegion(_southRegionController, "S");
        SetupAdjacentRegion(_southwestRegionController, "SW");
        SetupAdjacentRegion(_northwestRegionController, "NW");
    }

    public void SetMissionsHeader(string targetName, string flagText)
    {
        _missionsHeaderLabel.Text = string.IsNullOrEmpty(targetName) ? "MISSIONS" : $"Missions vs {targetName}";
        _missionsFlagPanel.Visible = !string.IsNullOrEmpty(flagText);
        _missionsFlagLabel.Text = flagText ?? "";
    }

    public void SetMissions(IReadOnlyList<AvailableMission> missions, AvailableMission selected)
    {
        foreach (Button button in _missionButtons)
        {
            _missionsListStack.RemoveChild(button);
            button.QueueFree();
        }
        _missionButtons.Clear();
        _missionButtonGroup = new ButtonGroup();

        foreach (AvailableMission mission in missions)
        {
            Button button = new()
            {
                Text = mission.Kind == MissionAvailabilityKind.Special ? $"{mission.Label} (Special)" : mission.Label,
                TooltipText = GetMissionDescription(mission),
                ToggleMode = true,
                ButtonGroup = _missionButtonGroup,
                ButtonPressed = selected != null && selected.Kind == mission.Kind && selected.Label == mission.Label,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 46),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            IconAtlas.Apply(button, GetMissionIconKey(mission.Kind), 0);
            AvailableMission capturedMission = mission;
            button.Pressed += () => MissionSelected?.Invoke(this, capturedMission);
            Color accent = mission.Kind == MissionAvailabilityKind.Special ? OnlyWarStyle.Gold : OnlyWarStyle.PlayerAccent;
            OnlyWarStyle.ApplyAccentButtonRow(button, button.ButtonPressed, accent);
            // Re-apply the accent styling whenever the toggle state changes so a button the
            // ButtonGroup silently deselects (e.g. selecting a mission in the other column)
            // doesn't keep its highlighted "normal" stylebox.
            Button capturedButton = button;
            button.Toggled += pressed => OnlyWarStyle.ApplyAccentButtonRow(capturedButton, pressed, accent);
            _missionButtons.Add(button);
            _missionsListStack.AddChild(button);
        }
    }

    // Renders the "Inbound Orders" card in the target dossier: every player order aimed at the
    // selected region, regardless of which region its squads are tasked from. Rows are clickable
    // (single click) to reopen the order dialog - this is the surviving edit affordance now that
    // the central "active orders" strip is gone.
    public void SetInboundOrders(IReadOnlyList<InboundOrderInfo> rows)
    {
        ClearContainerChildren(_inboundOrdersStack);

        PanelContainer cardPanel = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        OnlyWarStyle.ApplyTintedListRow(cardPanel, false, OnlyWarStyle.PlayerAccent);

        VBoxContainer cardStack = new();
        cardStack.AddThemeConstantOverride("separation", 4);
        cardPanel.AddChild(cardStack);

        Label titleLabel = new() { Text = "INBOUND ORDERS" };
        titleLabel.AddThemeFontSizeOverride("font_size", 12);
        titleLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        cardStack.AddChild(titleLabel);

        if (rows.Count == 0)
        {
            Label empty = new() { Text = "None" };
            empty.AddThemeFontSizeOverride("font_size", 12);
            empty.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
            cardStack.AddChild(empty);
            _inboundOrdersStack.AddChild(cardPanel);
            return;
        }

        foreach (InboundOrderInfo row in rows)
        {
            string squads = row.SquadCount == 1 ? "1 squad" : $"{row.SquadCount} squads";
            Button rowButton = new()
            {
                Text = $"{row.MissionLabel} · {squads} · from {row.OriginLabel}",
                TooltipText = "Open orders",
                MouseDefaultCursorShape = CursorShape.PointingHand,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(0, 30)
            };
            rowButton.AddThemeFontSizeOverride("font_size", 12);
            OnlyWarStyle.ApplyAccentButtonRow(rowButton, false, OnlyWarStyle.PlayerAccent);
            Order capturedOrder = row.Order;
            rowButton.Pressed += () => InboundOrderActivated?.Invoke(this, capturedOrder);
            cardStack.AddChild(rowButton);
        }

        _inboundOrdersStack.AddChild(cardPanel);
    }

    public void SetAggression(Aggression aggression)
    {
        foreach (KeyValuePair<Aggression, Button> pair in _aggressionButtons)
        {
            bool isActive = pair.Key == aggression;
            pair.Value.ButtonPressed = isActive;
            OnlyWarStyle.ApplyAccentButtonRow(pair.Value, isActive, OnlyWarStyle.Gold);
        }
    }

    public void SetTargetFactionOptions(IReadOnlyList<(string Name, int Id)> options, bool visible)
    {
        _targetFactionOption.Visible = visible;
        _targetFactionOption.Clear();
        if (!visible) return;

        foreach ((string name, int id) in options)
        {
            _targetFactionOption.AddItem(name, id == -1 ? int.MinValue : id);
        }
        if (options.Count > 0)
        {
            _targetFactionOption.Select(0);
        }
    }

    public void SetAssignButton(string text, bool enabled)
    {
        _assignButton.Text = text;
        _assignButton.Disabled = !enabled;
    }

    public void SetDossier(string title, IReadOnlyList<DossierCardData> cards)
    {
        _dossierTitleLabel.Text = title ?? "Dossier";
        ClearContainerChildren(_dossierStack);

        foreach (DossierCardData card in cards)
        {
            _dossierStack.AddChild(DossierCard.Create(card));
        }
    }

    private void ConnectHexSignals(TacticalRegionController controller)
    {
        if (controller == null) return;
        controller.TacticalRegionPressed += (sender, region) => TargetRegionSelected?.Invoke(this, region);
        controller.TacticalRegionDoubleClicked += (sender, region) => AdjacentRegionClicked?.Invoke(this, region);
    }

    private void HideLegacyPanels()
    {
        if (_legacyDataPanel != null) _legacyDataPanel.Visible = false;
        if (_legacySquadTreePanel != null) _legacySquadTreePanel.Visible = false;
        if (_legacyOrdersPanel != null) _legacyOrdersPanel.Visible = false;
    }

    private void ConfigureBoardPanel()
    {
        _boardPanel.AnchorLeft = 0.245f;
        _boardPanel.AnchorTop = 0.08f;
        _boardPanel.AnchorRight = 0.755f;
        _boardPanel.AnchorBottom = 0.91f;
        _boardPanel.OffsetLeft = 0;
        _boardPanel.OffsetTop = 0;
        _boardPanel.OffsetRight = 0;
        _boardPanel.OffsetBottom = 0;
        _boardPanel.MouseFilter = MouseFilterEnum.Pass;

        StyleBoxFlat mapStyle = new()
        {
            BgColor = new Color(0.005f, 0.006f, 0.007f, 0.74f),
            BorderColor = new Color(0.45f, 0.35f, 0.18f, 0.86f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 1,
            CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1,
            CornerRadiusBottomRight = 1
        };
        _boardPanel.AddThemeStyleboxOverride("panel", mapStyle);
    }

    // The 7 hex controllers were sized (in region_screen.tscn) to fill the whole board as a big
    // tactical map. Option A wants a compact target-picker instead, so they're rescaled in code
    // into the top ~34% of the board and centered horizontally, rather than editing the scene.
    private void CompactHexCluster()
    {
        void Compact(TacticalRegionController controller, float left, float top, float right, float bottom)
        {
            Control control = controller;
            control.AnchorLeft = 0.25f + left * 0.5f;
            control.AnchorRight = 0.25f + right * 0.5f;
            control.AnchorTop = top * 0.34f;
            control.AnchorBottom = bottom * 0.34f;
            control.OffsetLeft = 0;
            control.OffsetRight = 0;
            control.OffsetTop = 0;
            control.OffsetBottom = 0;
        }

        Compact(_centerRegionController, 0.35f, 0.35f, 0.65f, 0.65f);
        Compact(_northRegionController, 0.35f, 0.01f, 0.65f, 0.31f);
        Compact(_northeastRegionController, 0.68f, 0.16f, 0.98f, 0.46f);
        Compact(_southeastRegionController, 0.68f, 0.54f, 0.98f, 0.84f);
        Compact(_southRegionController, 0.35f, 0.69f, 0.65f, 0.99f);
        Compact(_southwestRegionController, 0.02f, 0.54f, 0.32f, 0.84f);
        Compact(_northwestRegionController, 0.02f, 0.16f, 0.32f, 0.46f);
    }

    private void BuildMissionsSection()
    {
        VBoxContainer section = new()
        {
            Name = "MissionsSection",
            AnchorLeft = 0f,
            AnchorTop = 0.36f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 6,
            OffsetTop = 0,
            OffsetRight = -6,
            // Runs down to just above the pinned commit bar (top at -74). Previously stopped at
            // -168 to clear the active-orders strip that sat between it and the commit bar; that
            // strip moved to the dossier's Inbound Orders card, so the mission list reclaims the band.
            OffsetBottom = -78
        };
        section.AddThemeConstantOverride("separation", 4);
        _boardPanel.AddChild(section);

        HBoxContainer headerRow = new();
        headerRow.AddThemeConstantOverride("separation", 8);
        section.AddChild(headerRow);

        _missionsHeaderLabel = new Label { Text = "MISSIONS" };
        _missionsHeaderLabel.AddThemeFontSizeOverride("font_size", 14);
        _missionsHeaderLabel.AddThemeFontOverride("font", GetThemeFont("display"));
        headerRow.AddChild(_missionsHeaderLabel);

        _missionsFlagPanel = new PanelContainer { Visible = false };
        OnlyWarStyle.ApplyTintedListRow(_missionsFlagPanel, false, OnlyWarStyle.OpposingAccent);
        headerRow.AddChild(_missionsFlagPanel);

        _missionsFlagLabel = new Label();
        _missionsFlagLabel.AddThemeFontSizeOverride("font_size", 11);
        _missionsFlagLabel.AddThemeColorOverride("font_color", OnlyWarStyle.OpposingAccent);
        _missionsFlagPanel.AddChild(_missionsFlagLabel);

        ScrollContainer scroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        section.AddChild(scroll);

        // Two columns so the mission buttons don't stretch full-width (they're much wider than
        // their labels need) and the list needs far less vertical scrolling.
        _missionsListStack = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _missionsListStack.AddThemeConstantOverride("h_separation", 4);
        _missionsListStack.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(_missionsListStack);
    }

    // Pinned via a fixed pixel offset from the board's bottom edge rather than an anchor
    // fraction, so it stays fully visible regardless of board height - this is the fix for the
    // mockup's one flaw (the commit bar overflowing the panel).
    private void BuildCommitBar()
    {
        PanelContainer commitBar = new()
        {
            Name = "CommitBar",
            AnchorLeft = 0f,
            AnchorTop = 1f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 4,
            OffsetTop = -74,
            OffsetRight = -4,
            OffsetBottom = -4
        };
        OnlyWarStyle.ApplyContentPanel(commitBar);
        _boardPanel.AddChild(commitBar);

        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 4);
        commitBar.AddChild(stack);

        HBoxContainer aggressionRow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        aggressionRow.AddThemeConstantOverride("separation", 4);
        stack.AddChild(aggressionRow);

        _aggressionButtonGroup = new ButtonGroup();
        foreach (Aggression aggression in Enum.GetValues<Aggression>())
        {
            Button button = new()
            {
                Text = aggression.ToString(),
                ToggleMode = true,
                ButtonGroup = _aggressionButtonGroup,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 26)
            };
            button.AddThemeFontSizeOverride("font_size", 11);
            Aggression captured = aggression;
            button.Pressed += () => AggressionChanged?.Invoke(this, captured);
            _aggressionButtons[aggression] = button;
            aggressionRow.AddChild(button);
        }

        HBoxContainer assignRow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        assignRow.AddThemeConstantOverride("separation", 8);
        stack.AddChild(assignRow);

        _targetFactionOption = new OptionButton
        {
            Visible = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 32)
        };
        _targetFactionOption.ItemSelected += index =>
        {
            int id = _targetFactionOption.GetItemId((int)index);
            TargetFactionSelected?.Invoke(this, id == int.MinValue ? -1 : id);
        };
        assignRow.AddChild(_targetFactionOption);

        _unassignButton = new Button
        {
            Text = "Unassign",
            Disabled = true,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(96, 32)
        };
        IconAtlas.Apply(_unassignButton, "locked", 96);
        _unassignButton.Pressed += () => UnassignPressed?.Invoke(this, EventArgs.Empty);
        assignRow.AddChild(_unassignButton);

        _assignButton = new Button
        {
            Text = "Assign 0 Squads",
            Disabled = true,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 32)
        };
        _assignButton.Pressed += () => AssignPressed?.Invoke(this, EventArgs.Empty);
        assignRow.AddChild(_assignButton);
    }

    public void SetUnassignButton(bool enabled)
    {
        _unassignButton.Disabled = !enabled;
    }

    private void BuildDossierPanel()
    {
        _dossierPanel = CreatePanel("DossierPanel", 0.765f, 0.08f, 0.99f, 0.91f);

        VBoxContainer outer = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddThemeConstantOverride("separation", 8);
        _dossierPanel.AddChild(outer);

        Label caption = new() { Text = "SELECTED TARGET" };
        caption.AddThemeFontSizeOverride("font_size", 13);
        caption.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        outer.AddChild(caption);

        _dossierTitleLabel = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _dossierTitleLabel.AddThemeFontSizeOverride("font_size", 22);
        _dossierTitleLabel.AddThemeFontOverride("font", GetThemeFont("display"));
        outer.AddChild(_dossierTitleLabel);

        ScrollContainer scroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        outer.AddChild(scroll);

        // The scroll holds one content column so the dossier cards and the inbound-orders card
        // scroll together. Two separate stacks let SetDossier and SetInboundOrders repopulate
        // independently without clobbering each other's children.
        VBoxContainer content = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(content);

        _dossierStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _dossierStack.AddThemeConstantOverride("separation", 8);
        content.AddChild(_dossierStack);

        _inboundOrdersStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _inboundOrdersStack.AddThemeConstantOverride("separation", 8);
        content.AddChild(_inboundOrdersStack);
    }

    private static string GetMissionIconKey(MissionAvailabilityKind kind)
    {
        return kind switch
        {
            MissionAvailabilityKind.Recon => "scout",
            MissionAvailabilityKind.Defend => "objective",
            MissionAvailabilityKind.Patrol => "route",
            MissionAvailabilityKind.FortifyEntrenchment => "construction",
            MissionAvailabilityKind.BuildListeningPost => "construction",
            MissionAvailabilityKind.BuildAntiAir => "construction",
            MissionAvailabilityKind.Attack => "hostile",
            MissionAvailabilityKind.Diversion => "route",
            MissionAvailabilityKind.Move => "plot_course",
            MissionAvailabilityKind.Special => "award",
            _ => "objective"
        };
    }

    private static string GetMissionDescription(AvailableMission mission)
    {
        return mission.Kind switch
        {
            MissionAvailabilityKind.Recon => "Probe for hidden enemy forces and opportunities for special missions.",
            MissionAvailabilityKind.Defend => "Defend the region from attacks by enemy forces.",
            MissionAvailabilityKind.Patrol => "Move around the region, attempting to find hidden or infiltrating enemy forces.",
            MissionAvailabilityKind.FortifyEntrenchment => "Spend the turn building entrenchment defenses in this region.",
            MissionAvailabilityKind.BuildListeningPost => "Spend the turn building a listening post in this region.",
            MissionAvailabilityKind.BuildAntiAir => "Spend the turn building anti-air defenses in this region.",
            MissionAvailabilityKind.Attack => "Enter the target region, engaging any enemy forces there.",
            MissionAvailabilityKind.Diversion => "Feint from the origin region to pin the garrison in place.",
            MissionAvailabilityKind.Move => "Move into the target region.",
            MissionAvailabilityKind.Special => "Special mission opportunity identified in this region.",
            _ => ""
        };
    }

    private static void ClearContainerChildren(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
