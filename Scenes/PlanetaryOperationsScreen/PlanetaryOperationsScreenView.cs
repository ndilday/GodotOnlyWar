using Godot;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlanetaryOperationsScreenView : Control
{
    private Button _worldName;
    private Label _aggregateStrip;
    private HBoxContainer _verbBar;
    private VBoxContainer _leftContent;
    private VBoxContainer _rightContent;
    private ScrollContainer _rightScroll;
    private PanelContainer _bottomPanel;
    private VBoxContainer _bottomContent;
    private PlanetRegionMapView _map;
    private Control _dossierOverlay;
    private VBoxContainer _dossierContent;
    private PlanetaryOperationsVerb _verb;
    private HierarchyTreeView _forceTree;
    private IReadOnlyDictionary<string, bool> _forceTreeCollapsedStates =
        new Dictionary<string, bool>();
    private int _forceTreeScrollVertical;

    public event EventHandler<Region> RegionSelected;
    public event EventHandler<Region> RegionActivated;
    public event EventHandler WorldDossierRequested;
    public event EventHandler<PlanetaryOperationsVerb> VerbSelected;
    public event EventHandler<string> ForceNodePressed;
    public event EventHandler<string> ForceNodeActivated;
    public event EventHandler<string> ForceFilterChanged;
    public event EventHandler<ForceTreeGrouping> GroupingChanged;
    public event EventHandler<string> MissionSelected;
    public event EventHandler<int> OrderSelected;
    public event EventHandler<int> RemoveSquadRequested;
    public event EventHandler<int> CancelOrderRequested;
    public event EventHandler<Aggression> AggressionSelected;
    public event EventHandler<int> SpecialistToggleRequested;
    public event EventHandler UndoRequested;
    public event EventHandler<int> ShipSelected;
    public event EventHandler ConfirmMovementRequested;
    public event EventHandler<int> CasualtyToggled;
    public event EventHandler<int> RecoveryRequested;
    public event EventHandler OpenShipManagementRequested;

    public ulong MapInstanceId => _map?.PersistentInstanceId ?? 0;

    public override void _Ready() => BuildShell();

    public void SetHeader(PlanetaryOperationsHeaderViewModel model)
    {
        _worldName.Text = $"PLANETARY OPERATIONS  /  {model.PlanetName?.ToUpperInvariant()}";
        _worldName.TooltipText = "Open the world dossier without leaving regional operations.";
        _aggregateStrip.Text = $"REGIONS {model.ImperialRegions}/{model.TotalRegions}  ·  "
            + $"LANDED {model.Landed}  ·  ORBIT {model.InOrbit}  ·  {model.RequestClock.ToUpperInvariant()}";
    }

    public void SetVerb(PlanetaryOperationsVerb verb)
    {
        _verb = verb;
        foreach (Button button in _verbBar.GetChildren().OfType<Button>())
        {
            bool selected = button.GetMeta("verb").AsInt32() == (int)verb;
            button.ButtonPressed = selected;
            OnlyWarStyle.ApplyAccentButtonRow(button, selected, OnlyWarStyle.Gold);
        }
    }

    public void DisplayMap(PlanetRegionMapViewModel model, Region selectedRegion) =>
        _map.Display(model, selectedRegion);

    public void DisplayOrders(
        RegionalOperationsViewModel model,
        IReadOnlyList<HierarchyTreeItem> forceTree,
        string filter,
        string selectedMissionKey,
        Order selectedOrder,
        IReadOnlyList<SpecialistOption> specialists,
        string undoDescription)
    {
        CaptureForceTreeState();
        Clear(_leftContent);
        Clear(_rightContent);
        AddForceTree(_leftContent, "REGIONAL FORCE", forceTree, filter, false,
            ForceTreeGrouping.Company);

        AddCaption(_rightContent, "SELECTED REGION");
        AddCards(_rightContent, model.SelectedRegionCards);
        AddCaption(_rightContent, "ACTIVE ORDERS");
        if (model.ActiveOrders.Count == 0) AddHint(_rightContent, "No Chapter orders target this region.");
        else AddHint(_rightContent, "Select an active order to edit or reinforce it.");
        IReadOnlyList<AvailableMission> missionOptions = model.OrdinaryMissions
            .Concat(model.SpecialMissions)
            .ToList();
        foreach (Order order in model.ActiveOrders)
            AddOrderChoice(order, selectedOrder?.Id == order.Id, missionOptions);

        AddCaption(_rightContent, "AVAILABLE ORDERS");
        GridContainer ordinary = new()
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        ordinary.AddThemeConstantOverride("h_separation", 5);
        ordinary.AddThemeConstantOverride("v_separation", 5);
        List<AvailableMission> availableOrdinary = model.OrdinaryMissions
            .Where(mission => !IsActiveMission(mission, model.ActiveOrders)).ToList();
        if (availableOrdinary.Count == 0 && model.OrdinaryMissions.Count > 0)
            AddHint(_rightContent, "All ordinary order types are already active here.");
        foreach (AvailableMission mission in availableOrdinary)
        {
            Button button = MissionButton(mission, selectedMissionKey);
            button.CustomMinimumSize = new Vector2(0, 56);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            ordinary.AddChild(button);
        }
        _rightContent.AddChild(ordinary);

        AddCaption(_rightContent, "SPECIAL MISSIONS");
        List<AvailableMission> availableSpecial = model.SpecialMissions
            .Where(mission => !IsActiveMission(mission, model.ActiveOrders)).ToList();
        if (availableSpecial.Count == 0)
        {
            string message = model.SpecialMissions.Count == 0
                ? "No intelligence-discovered opportunities are active here."
                : "All discovered special missions are already active here.";
            AddHint(_rightContent, message);
        }
        foreach (AvailableMission mission in availableSpecial)
        {
            Button button = MissionButton(mission, selectedMissionKey);
            button.CustomMinimumSize = new Vector2(0, 52);
            button.Text += "\n" + SpecialMissionPresentation.FormatRecommendedForce(
                mission.SpecialMission, GameDataSingleton.Instance?.Date?.GetTotalWeeks() ?? 0);
            _rightContent.AddChild(button);
        }

        if (selectedOrder != null) AddLiveOrderEditor(selectedOrder, specialists);
        else
        {
            AddCaption(_rightContent, "ORDER REPORTING");
            AddHint(_rightContent, selectedMissionKey == null
                ? "Select an active order or an available mission."
                : "Click an eligible squad or company in the force tree. The first squad creates the order immediately.");
        }
        DisplayReportingBar(selectedOrder, undoDescription);
    }

    public void DisplayMovement(
        PlanetaryOperationsVerb verb,
        IReadOnlyList<DossierCardData> regionCards,
        IReadOnlyList<HierarchyTreeItem> forceTree,
        string filter,
        ForceTreeGrouping grouping,
        IReadOnlyList<ShipCapacityChoice> ships,
        int? selectedShipId,
        int selectedCount)
    {
        CaptureForceTreeState();
        Clear(_leftContent);
        Clear(_rightContent);
        AddForceTree(_leftContent,
            verb == PlanetaryOperationsVerb.Land ? "ORBITING FORCE" : "SURFACE FORCE",
            forceTree, filter, verb == PlanetaryOperationsVerb.Land, grouping);
        if (verb == PlanetaryOperationsVerb.Land)
        {
            DisplayLandingDestination(regionCards);
        }
        else
        {
            AddCaption(_rightContent, "EMBARKATION ORIGIN");
            AddCards(_rightContent, regionCards);
            AddCaption(_rightContent, "DESTINATION SHIP");
            AddShipChoices(ships, selectedShipId);
        }
        bool canCommit = selectedCount > 0
            && (verb == PlanetaryOperationsVerb.Land
                || selectedShipId.HasValue && ships.Any(choice => choice.Ship.Id == selectedShipId && choice.Fits));
        DisplayMovementBar(selectedCount, canCommit,
            selectedCount == 0 ? "Select at least one squad."
            : verb == PlanetaryOperationsVerb.Embark && !selectedShipId.HasValue
                ? "Choose a ship with enough capacity." : "Confirm the selected squads.");
    }

    // Region changes during Land only affect the destination panel. The orbiting force tree and
    // its multi-selection are independent of the highlighted destination, so leave _leftContent
    // untouched here.
    public void UpdateLandingDestination(IReadOnlyList<DossierCardData> regionCards)
    {
        Clear(_rightContent);
        DisplayLandingDestination(regionCards);
    }

    private void DisplayLandingDestination(IReadOnlyList<DossierCardData> regionCards)
    {
        AddCaption(_rightContent, "LANDING DESTINATION");
        AddCards(_rightContent, regionCards);
        AddCaption(_rightContent, "LANDING CONSEQUENCE");
        AddHint(_rightContent, "Landing changes location only. Select orders after the force reaches the surface.");
    }

    public void DisplayDetach(
        IReadOnlyList<DossierCardData> regionCards,
        IReadOnlyList<PlayerSoldier> casualties,
        IReadOnlySet<int> selectedIds,
        IReadOnlyList<ShipCapacityChoice> ships,
        int? selectedShipId)
    {
        CaptureForceTreeState();
        Clear(_leftContent);
        Clear(_rightContent);
        AddCaption(_leftContent, "REGIONAL CASUALTIES");
        AddHint(_leftContent, "Detach wounded individuals to a ship in orbit; their squad remains on the surface.");
        foreach (PlayerSoldier soldier in casualties) AddCasualtyRow(soldier, selectedIds.Contains(soldier.Id));
        if (casualties.Count == 0) AddHint(_leftContent, "No wounded, undetached personnel are present in this region.");
        AddCaption(_rightContent, "SOURCE REGION");
        AddCards(_rightContent, regionCards);
        AddCaption(_rightContent, "SHIP IN ORBIT");
        AddShipChoices(ships, selectedShipId);
        AddHint(_rightContent, "Treatment choice and onward care remain in Recovery Operations.");
        bool canCommit = selectedIds.Count > 0 && selectedShipId.HasValue
            && ships.Any(choice => choice.Ship.Id == selectedShipId && choice.Fits);
        DisplayMovementBar(selectedIds.Count, canCommit,
            selectedIds.Count == 0 ? "Select at least one casualty."
            : !selectedShipId.HasValue ? "Choose a ship with enough berths." : "Confirm the selected casualties.");
    }

    public void ShowWorldDossier(WorldDossierViewModel model)
    {
        Clear(_dossierContent);
        AddCaption(_dossierContent, "WORLD DOSSIER");
        AddCards(_dossierContent, model.ProfileCards);
        AddCards(_dossierContent, model.StrengthCards);
        _dossierOverlay.Visible = true;
        _dossierOverlay.MoveToFront();
    }

    public void FocusMap() => _map.FocusSelectedRegion();

    public void ResetRightPanelScroll() => _rightScroll.ScrollVertical = 0;

    private void BuildShell()
    {
        MarginContainer margin = new();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);
        Control root = new()
        {
            ClipContents = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddChild(root);

        HBoxContainer header = new() { CustomMinimumSize = new Vector2(0, 42) };
        header.AnchorRight = 1f;
        header.OffsetRight = -52;
        header.OffsetBottom = 42;
        _worldName = new Button
        {
            Text = "PLANETARY OPERATIONS",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _worldName.AddThemeFontSizeOverride("font_size", 19);
        _worldName.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        _worldName.Pressed += () => WorldDossierRequested?.Invoke(this, EventArgs.Empty);
        header.AddChild(_worldName);
        _aggregateStrip = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
            CustomMinimumSize = new Vector2(500, 24),
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        _aggregateStrip.AddThemeFontSizeOverride("font_size", 12);
        _aggregateStrip.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        header.AddChild(_aggregateStrip);
        root.AddChild(header);

        _verbBar = new HBoxContainer();
        _verbBar.AnchorRight = 1f;
        _verbBar.OffsetTop = 50;
        _verbBar.OffsetBottom = 86;
        _verbBar.AddThemeConstantOverride("separation", 6);
        foreach (PlanetaryOperationsVerb verb in Enum.GetValues<PlanetaryOperationsVerb>())
        {
            Button button = new()
            {
                Text = verb.ToString().ToUpperInvariant(),
                ToggleMode = true,
                CustomMinimumSize = new Vector2(120, 36)
            };
            button.SetMeta("verb", (int)verb);
            string icon = verb switch
            {
                PlanetaryOperationsVerb.Land => "land_squads",
                PlanetaryOperationsVerb.Embark => "load_squads",
                PlanetaryOperationsVerb.Detach => "medical",
                _ => "objective"
            };
            IconAtlas.Apply(button, icon, 128);
            button.Pressed += () => VerbSelected?.Invoke(this, verb);
            _verbBar.AddChild(button);
        }
        root.AddChild(_verbBar);

        HBoxContainer body = new()
        {
            AnchorRight = 1f,
            AnchorBottom = 1f
        };
        body.OffsetTop = 94;
        body.OffsetBottom = 0;
        body.AddThemeConstantOverride("separation", 8);
        root.AddChild(body);

        PanelContainer leftPanel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.27f,
            CustomMinimumSize = new Vector2(320, 0)
        };
        OnlyWarStyle.ApplyContentPanel(leftPanel);
        Control leftSurface = new() { ClipContents = true };
        leftSurface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        leftPanel.AddChild(leftSurface);
        VBoxContainer leftStack = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ClipContents = true
        };
        leftStack.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        leftSurface.AddChild(leftStack);
        ScrollContainer leftScroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        leftStack.AddChild(leftScroll);
        _leftContent = CreateScrollStack(leftScroll);

        _bottomPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 64),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkEnd,
            ClipContents = true
        };
        OnlyWarStyle.ApplyContentPanel(_bottomPanel);
        _bottomContent = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _bottomPanel.AddChild(_bottomContent);
        leftStack.AddChild(_bottomPanel);
        body.AddChild(leftPanel);

        _map = new PlanetRegionMapView
        {
            Name = "PlanetRegionMapView",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.41f,
            CustomMinimumSize = new Vector2(500, 0)
        };
        _map.RegionSelected += (_, region) => RegionSelected?.Invoke(this, region);
        _map.RegionActivated += (_, region) => RegionActivated?.Invoke(this, region);
        body.AddChild(_map);
        _rightScroll = CreateSidePanel(body, 390, 0.32f);
        _rightContent = CreateScrollStack(_rightScroll);

        BuildDossierOverlay();
    }

    private void BuildDossierOverlay()
    {
        _dossierOverlay = new PanelContainer
        {
            Visible = false,
            ZIndex = 20,
            AnchorLeft = 0.12f,
            AnchorTop = 0.06f,
            AnchorRight = 0.88f,
            AnchorBottom = 0.94f
        };
        OnlyWarStyle.ApplyContentPanel((PanelContainer)_dossierOverlay);
        VBoxContainer stack = new();
        stack.AddThemeConstantOverride("separation", 8);
        _dossierOverlay.AddChild(stack);
        Button close = ActionButton("CLOSE DOSSIER", "close");
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        close.Pressed += () => _dossierOverlay.Visible = false;
        stack.AddChild(close);
        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        stack.AddChild(scroll);
        _dossierContent = CreateScrollStack(scroll);
        AddChild(_dossierOverlay);
    }

    private void AddForceTree(Container parent, string title,
        IReadOnlyList<HierarchyTreeItem> entries, string filter,
        bool showGrouping, ForceTreeGrouping grouping)
    {
        AddCaption((VBoxContainer)parent, title);
        LineEdit search = new()
        {
            PlaceholderText = "Filter force…",
            Text = filter ?? "",
            ClearButtonEnabled = true
        };
        search.TextChanged += value => ForceFilterChanged?.Invoke(this, value);
        parent.AddChild(search);
        if (showGrouping)
        {
            HBoxContainer toggle = new();
            foreach (ForceTreeGrouping option in Enum.GetValues<ForceTreeGrouping>())
            {
                Button button = SelectableButton(option.ToString().ToUpperInvariant(), option == grouping);
                button.Pressed += () => GroupingChanged?.Invoke(this, option);
                toggle.AddChild(button);
            }
            parent.AddChild(toggle);
        }
        HierarchyTreeView tree = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 360),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = Tree.SelectModeEnum.Multi,
            AllowReselect = true
        };
        tree.SelectionChanged += (_, key) => ForceNodePressed?.Invoke(this, key);
        tree.Activated += (_, key) => ForceNodeActivated?.Invoke(this, key);
        parent.AddChild(tree);
        tree.Populate(entries, preserveUiState: false, suppressSelectionSignals: true);
        tree.SetCollapsedStates(_forceTreeCollapsedStates);
        if (_forceTreeScrollVertical > 0)
        {
            // The replacement tree has not been laid out yet, so restoring immediately would be
            // clamped back to zero. Defer until its content size is available.
            tree.SetVerticalScrollOffset(_forceTreeScrollVertical);
        }
        _forceTree = tree;
        if (entries.Count == 0) AddHint(parent, "No matching formations.");
    }

    private void CaptureForceTreeState()
    {
        if (_forceTree != null && IsInstanceValid(_forceTree))
        {
            _forceTreeCollapsedStates = _forceTree.GetCollapsedStatesByKey();
            _forceTreeScrollVertical = _forceTree.GetVerticalScrollOffset();
        }
    }

    private void AddLiveOrderEditor(Order order, IReadOnlyList<SpecialistOption> specialists)
    {
        AddCaption(_rightContent, "LIVE ORDER");
        PanelContainer panel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        OnlyWarStyle.ApplyTintedListRow(panel, true, OnlyWarStyle.PlayerAccent);
        VBoxContainer stack = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        stack.AddThemeConstantOverride("separation", 6);
        panel.AddChild(stack);
        stack.AddChild(new Label
        {
            Text = $"{MissionAvailability.GetOrderLabel(order.Mission).ToUpperInvariant()}\n"
                + $"{order.AssignedSquads.Count} squads · {order.AttachedSoldiers.Count} specialists"
        });
        AddHint(stack, "Edits take effect immediately and are free before turn resolution.");
        AddCaption(stack, "AGGRESSION");
        VBoxContainer aggression = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        aggression.AddThemeConstantOverride("separation", 2);
        ButtonGroup aggressionGroup = new();
        foreach (Aggression level in Enum.GetValues<Aggression>())
        {
            CheckBox radio = new()
            {
                Text = level.ToString().ToUpperInvariant(),
                ButtonGroup = aggressionGroup,
                ButtonPressed = order.LevelOfAggression == level,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 26)
            };
            radio.AddThemeFontSizeOverride("font_size", 11);
            radio.Toggled += pressed =>
            {
                if (pressed) AggressionSelected?.Invoke(this, level);
            };
            aggression.AddChild(radio);
        }
        stack.AddChild(aggression);
        AddCaption(stack, "ASSIGNED SQUADS");
        AddHint(stack, "Use UNASSIGN beside a squad to release it from this order.");
        foreach (var squad in order.AssignedSquads.ToList())
        {
            HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(new Label
            {
                Text = squad.Name,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            });
            Button remove = ActionButton("UNASSIGN", "close");
            remove.CustomMinimumSize = new Vector2(140, 34);
            remove.TooltipText = $"Release {squad.Name} from this order.";
            int id = squad.Id;
            remove.Pressed += () => RemoveSquadRequested?.Invoke(this, id);
            row.AddChild(remove);
            stack.AddChild(row);
        }
        AddCaption(stack, "ATTACHED SPECIALISTS");
        List<SpecialistOption> attachedSpecialists = specialists
            .Where(option => ReferenceEquals(option.Soldier.AttachedOrder, order))
            .ToList();
        if (attachedSpecialists.Count == 0)
            AddHint(stack, "No specialists attached.");
        foreach (SpecialistOption option in attachedSpecialists)
        {
            HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Label label = new()
            {
                Text = option.Soldier.Name,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                TooltipText = $"{option.Label}\nAttached to this order."
            };
            row.AddChild(label);
            Button detach = ActionButton("DETACH", "close");
            detach.CustomMinimumSize = new Vector2(96, 32);
            int id = option.Soldier.Id;
            detach.Pressed += () => SpecialistToggleRequested?.Invoke(this, id);
            row.AddChild(detach);
            stack.AddChild(row);
        }
        AddCaption(stack, "AVAILABLE SPECIALISTS");
        List<SpecialistOption> availableSpecialists = specialists
            .Where(option => option.IsAvailable
                && !ReferenceEquals(option.Soldier.AttachedOrder, order))
            .ToList();
        if (availableSpecialists.Count == 0)
        {
            AddHint(stack, specialists.Count == 0
                ? "No co-located specialist pool is available."
                : "No specialists are currently available.");
        }
        else
        {
            OptionButton picker = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 36),
                TooltipText = "Select a specialist to attach immediately."
            };
            picker.AddItem("SELECT SPECIALIST TO ATTACH…");
            foreach (SpecialistOption option in availableSpecialists)
                picker.AddItem(option.Soldier.Name, option.Soldier.Id);
            picker.ItemSelected += index =>
            {
                if (index <= 0) return;
                int selectedId = (int)picker.GetItemId((int)index);
                SpecialistToggleRequested?.Invoke(this, selectedId);
            };
            stack.AddChild(picker);
        }
        Button cancel = ActionButton("CANCEL ORDER", "alert");
        cancel.Pressed += () => CancelOrderRequested?.Invoke(this, order.Id);
        stack.AddChild(cancel);
        _rightContent.AddChild(panel);
    }

    private void AddOrderChoice(
        Order order,
        bool selected,
        IReadOnlyList<AvailableMission> missionOptions)
    {
        Button button = new()
        {
            Text = $"{MissionAvailability.GetOrderLabel(order.Mission).ToUpperInvariant()} · "
                + $"{order.AssignedSquads.Count} SQUADS · {order.LevelOfAggression.ToString().ToUpperInvariant()}",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 34),
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        button.AddThemeFontSizeOverride("font_size", 11);
        button.TooltipText = BuildMissionTooltip(order, missionOptions);
        int id = order.Id;
        button.Pressed += () => OrderSelected?.Invoke(this, id);
        OnlyWarStyle.ApplyListRow(button, selected);
        IconAtlas.Apply(button, MissionIconKey(order.Mission), 112);
        _rightContent.AddChild(button);
    }

    private Button MissionButton(AvailableMission mission, string selectedKey)
    {
        Button button = SelectableButton(mission.Label.ToUpperInvariant(), mission.IdentityKey == selectedKey);
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        button.ClipText = false;
        button.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        button.AddThemeFontSizeOverride("font_size", 11);
        button.TooltipText = BuildMissionTooltip(mission);
        IconAtlas.Apply(button, MissionIconKey(mission), 112);
        string key = mission.IdentityKey;
        button.Pressed += () => MissionSelected?.Invoke(this, key);
        return button;
    }

    private static bool IsActiveMission(
        AvailableMission mission,
        IReadOnlyList<Order> activeOrders) =>
        mission != null && activeOrders?.Any(order => mission.RepresentsOrder(order)) == true;

    private void AddShipChoices(IReadOnlyList<ShipCapacityChoice> ships, int? selectedId)
    {
        if (ships.Count > 0)
        {
            IReadOnlyList<HierarchyTreeItem> entries = ships
                .GroupBy(choice => choice.Ship.Fleet?.Id ?? -1)
                .Select(group => new HierarchyTreeItem(
                    $"fleet:{group.Key}",
                    group.First().Ship.Fleet == null ? "UNASSIGNED SHIPS" : $"TASK FORCE {group.Key}",
                    group.Select(choice =>
                    {
                        string capacity = $"{choice.CurrentPassengers}/{choice.Capacity} + "
                            + $"{choice.SelectedPassengers} = {choice.ResultingPassengers}/{choice.Capacity}";
                        return new HierarchyTreeItem(
                            $"ship:{choice.Ship.Id}",
                            choice.Ship.Name,
                            iconKey: "ship",
                            badge: choice.Fits ? capacity : $"{capacity} · SHORT {choice.Shortfall}",
                            tooltip: choice.Fits ? "The complete selection fits."
                                : $"Short by {choice.Shortfall} passenger spaces.",
                            selectable: choice.Fits,
                            isSelected: choice.Ship.Id == selectedId,
                            badgeColor: choice.Fits ? OnlyWarStyle.BodyText : OnlyWarStyle.MedicalWarning,
                            rowHeight: 36);
                    }).ToList(),
                    iconKey: "fleet",
                    badge: $"{group.Count()} ships",
                    selectable: false,
                    collapsedByDefault: true))
                .ToList();
            int selectedFleetId = selectedId.HasValue
                ? ships.FirstOrDefault(choice => choice.Ship.Id == selectedId.Value)?.Ship.Fleet?.Id ?? -1
                : int.MinValue;
            int expandedShipCount = selectedId.HasValue
                ? ships.Count(choice => (choice.Ship.Fleet?.Id ?? -1) == selectedFleetId)
                : 0;
            int visibleRows = entries.Count + expandedShipCount;
            HierarchyTreeView tree = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, Math.Min(360, 70 + (visibleRows + 2) * 38)),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                AllowReselect = true
            };
            tree.SelectionChanged += (_, key) =>
            {
                if (key?.StartsWith("ship:") == true && int.TryParse(key[5..], out int id))
                    ShipSelected?.Invoke(this, id);
            };
            _rightContent.AddChild(tree);
            tree.Populate(entries, preserveUiState: false, suppressSelectionSignals: true);
        }
        else
        {
            AddHint(_rightContent, "No Chapter transport is currently in orbit.");
            Button management = ActionButton("OPEN SHIP MANAGEMENT", "ship");
            management.Pressed += () => OpenShipManagementRequested?.Invoke(this, EventArgs.Empty);
            _rightContent.AddChild(management);
        }
    }

    private void AddCasualtyRow(PlayerSoldier soldier, bool selected)
    {
        PanelContainer panel = new();
        OnlyWarStyle.ApplyListRow(panel, selected);
        HBoxContainer row = new();
        panel.AddChild(row);
        Button toggle = new()
        {
            Text = $"{soldier.Name}\n{soldier.AssignedSquad?.Name} · WOUNDED",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        int id = soldier.Id;
        toggle.Pressed += () => CasualtyToggled?.Invoke(this, id);
        row.AddChild(toggle);
        Button recovery = ActionButton("RECOVERY OPS", "medical");
        recovery.Pressed += () => RecoveryRequested?.Invoke(this, id);
        row.AddChild(recovery);
        _leftContent.AddChild(panel);
    }

    private void DisplayReportingBar(Order order, string undoDescription)
    {
        _bottomPanel.Visible = true;
        Clear(_bottomContent);
        HBoxContainer row = new()
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        Label status = new()
        {
            Text = order == null
                ? "Select a mission, then add squads"
                : $"LIVE · {order.AssignedSquads.Count} squads · {order.LevelOfAggression}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = false,
            TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming
        };
        status.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        row.AddChild(status);
        Button undo = ActionButton("UNDO", "route");
        undo.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        undo.CustomMinimumSize = new Vector2(132, 34);
        undo.AddThemeFontSizeOverride("font_size", 12);
        undo.Disabled = string.IsNullOrWhiteSpace(undoDescription);
        undo.TooltipText = string.IsNullOrWhiteSpace(undoDescription)
            ? "Nothing to undo."
            : $"Undo the last change: {undoDescription}.";
        undo.Pressed += () => UndoRequested?.Invoke(this, EventArgs.Empty);
        row.AddChild(undo);
        _bottomContent.AddChild(row);
    }

    private void DisplayMovementBar(int selectedCount, bool enabled, string reason)
    {
        _bottomPanel.Visible = true;
        Clear(_bottomContent);
        HBoxContainer row = new()
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        Button confirm = ActionButton(MovementConfirmText(selectedCount),
            _verb == PlanetaryOperationsVerb.Land ? "land_squads"
            : _verb == PlanetaryOperationsVerb.Embark ? "load_squads" : "medical");
        confirm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        confirm.CustomMinimumSize = new Vector2(0, 34);
        confirm.ClipText = false;
        confirm.Disabled = !enabled;
        confirm.TooltipText = enabled ? "Confirm this movement." : reason;
        confirm.Pressed += () => ConfirmMovementRequested?.Invoke(this, EventArgs.Empty);
        row.AddChild(confirm);
        _bottomContent.AddChild(row);
    }

    private string MovementConfirmText(int selectedCount)
    {
        string verb = _verb switch
        {
            PlanetaryOperationsVerb.Land => "LANDING",
            PlanetaryOperationsVerb.Embark => "EMBARKING",
            PlanetaryOperationsVerb.Detach => "DETACHING",
            _ => $"{_verb.ToString().ToUpperInvariant()}ING"
        };
        string noun = _verb == PlanetaryOperationsVerb.Detach ? "CASUALTY" : "SQUAD";
        string plural = selectedCount == 1
            ? noun
            : noun == "CASUALTY" ? "CASUALTIES" : "SQUADS";
        return $"CONFIRM {verb} {selectedCount} {plural}";
    }

    private static string MissionIconKey(AvailableMission mission)
    {
        if (mission?.Kind == MissionAvailabilityKind.Special)
            return MissionIconKey(mission.SpecialMission);
        return mission?.Kind switch
        {
            MissionAvailabilityKind.Recon => "mission_recon",
            MissionAvailabilityKind.Defend => "mission_defend",
            MissionAvailabilityKind.Patrol => "mission_patrol",
            MissionAvailabilityKind.Attack => "mission_attack",
            MissionAvailabilityKind.Diversion => "mission_diversion",
            MissionAvailabilityKind.FortifyEntrenchment => "fortification_entrenchment",
            MissionAvailabilityKind.BuildListeningPost => "fortification_listening_post",
            MissionAvailabilityKind.BuildAntiAir => "fortification_anti_air",
            MissionAvailabilityKind.Move => "route",
            _ => "objective"
        };
    }

    private static string MissionIconKey(OnlyWar.Models.Missions.Mission mission)
    {
        if (mission is OnlyWar.Models.Missions.ConstructionMission construction)
        {
            return construction.ConstructionType switch
            {
                OnlyWar.Models.Missions.DefenseType.Entrenchment => "fortification_entrenchment",
                OnlyWar.Models.Missions.DefenseType.ListeningPost => "fortification_listening_post",
                OnlyWar.Models.Missions.DefenseType.AntiAir => "fortification_anti_air",
                _ => "objective"
            };
        }

        return mission?.MissionType switch
        {
            OnlyWar.Models.Missions.MissionType.Recon => "mission_recon",
            OnlyWar.Models.Missions.MissionType.DefenseInDepth => "mission_defend",
            OnlyWar.Models.Missions.MissionType.Patrol => "mission_patrol",
            OnlyWar.Models.Missions.MissionType.Advance =>
                mission.TargetFaction?.IsPlayerFaction == true ? "route" : "mission_attack",
            OnlyWar.Models.Missions.MissionType.Diversion => "mission_diversion",
            OnlyWar.Models.Missions.MissionType.Ambush => "mission_ambush",
            OnlyWar.Models.Missions.MissionType.Extermination => "mission_ambush",
            OnlyWar.Models.Missions.MissionType.Sabotage => "mission_sabotage",
            OnlyWar.Models.Missions.MissionType.ShowOfForce => "mission_show_of_force",
            _ => "objective"
        };
    }

    internal static string BuildMissionTooltip(AvailableMission mission)
    {
        if (mission?.SpecialMission == null) return mission?.Label ?? "";
        string recommended = SpecialMissionPresentation.FormatRecommendedForce(
            mission.SpecialMission, GameDataSingleton.Instance?.Date?.GetTotalWeeks() ?? 0);
        return $"{mission.Label}\n{recommended}";
    }

    internal static string BuildMissionTooltip(
        Order order,
        IReadOnlyList<AvailableMission> missionOptions)
    {
        AvailableMission mission = missionOptions?.FirstOrDefault(
            option => option.RepresentsOrder(order));
        if (mission != null) return BuildMissionTooltip(mission);

        // Keep the active row useful if an already-created order is no longer represented by
        // the current availability snapshot (for example, after intelligence changes).
        OnlyWar.Models.Missions.Mission orderMission = order?.Mission;
        if (orderMission == null) return "";
        Region region = orderMission.RegionFaction?.Region;
        bool isSpecial = region?.SpecialMissions?.Any(
            candidate => candidate?.Id == orderMission.Id) == true;
        if (!isSpecial) return MissionAvailability.GetOrderLabel(orderMission);

        return BuildMissionTooltip(new AvailableMission(
            SpecialMissionPresentation.Format(orderMission, region),
            MissionAvailabilityKind.Special,
            orderMission));
    }

    private static ScrollContainer CreateSidePanel(Container parent, float width, float ratio)
    {
        PanelContainer panel = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = ratio,
            CustomMinimumSize = new Vector2(width, 0)
        };
        OnlyWarStyle.ApplyContentPanel(panel);
        ScrollContainer scroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        panel.AddChild(scroll);
        parent.AddChild(panel);
        return scroll;
    }

    private static VBoxContainer CreateScrollStack(ScrollContainer scroll)
    {
        VBoxContainer stack = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        stack.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(stack);
        return stack;
    }

    private static Button ActionButton(string text, string icon)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(118, 34),
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        OnlyWarStyle.ApplyAccentButtonRow(button, false, OnlyWarStyle.Gold);
        IconAtlas.Apply(button, icon, 130);
        return button;
    }

    private static Button SelectableButton(string text, bool selected)
    {
        Button button = new()
        {
            Text = text,
            ToggleMode = true,
            ButtonPressed = selected,
            CustomMinimumSize = new Vector2(122, 38),
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        OnlyWarStyle.ApplyListRow(button, selected);
        return button;
    }

    private static void AddCards(VBoxContainer parent, IReadOnlyList<DossierCardData> cards)
    {
        foreach (DossierCardData card in cards ?? [])
            parent.AddChild(DossierCard.Create(card, extraBottomSpacing: 5));
    }

    private static void AddCaption(VBoxContainer parent, string text)
    {
        Label label = new() { Text = text };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        parent.AddChild(label);
    }

    private static void AddHint(Container parent, string text)
    {
        Label hint = new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        parent.AddChild(hint);
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
