using Godot;
using OnlyWar.Helpers.UI;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;

public enum RegionCommandMode
{
    Overview,
    Forces,
    Orders,
    Intel
}

public class RegionCommandTreeNode
{
    public string Key { get; }
    public string Text { get; }
    public IReadOnlyList<RegionCommandTreeNode> Children { get; }

    public RegionCommandTreeNode(string key, string text, IReadOnlyList<RegionCommandTreeNode> children = null)
    {
        Key = key;
        Text = text;
        Children = children ?? Array.Empty<RegionCommandTreeNode>();
    }
}

public class RegionCommandAction
{
    public string Key { get; }
    public string Text { get; }
    public string IconKey { get; }
    public bool Enabled { get; }

    public RegionCommandAction(string key, string text, string iconKey, bool enabled)
    {
        Key = key;
        Text = text;
        IconKey = iconKey;
        Enabled = enabled;
    }
}

public partial class RegionScreenView : DialogView
{
    private readonly Dictionary<RegionCommandMode, Button> _modeButtons = [];

    private Panel _legacyDataPanel;
    private Panel _legacySquadTreePanel;
    private Panel _legacyOrdersPanel;
    private Panel _regionPanel;
    private VBoxContainer _modeButtonStack;
    private Label _selectionTitleLabel;
    private Tree _selectionTree;
    private Label _selectionHintLabel;
    private Label _contextTitleLabel;
    private Label _contextSubtitleLabel;
    private VBoxContainer _contextStack;
    private HBoxContainer _commandBar;

    private TacticalRegionController _centerRegionController;
    private TacticalRegionController _northRegionController;
    private TacticalRegionController _northeastRegionController;
    private TacticalRegionController _southeastRegionController;
    private TacticalRegionController _southRegionController;
    private TacticalRegionController _southwestRegionController;
    private TacticalRegionController _northwestRegionController;

    public event EventHandler<RegionCommandMode> ModeSelected;
    public event EventHandler<string> SelectionTreeItemSelected;
    public event EventHandler<string> SelectionTreeItemActivated;
    public event EventHandler<string> CommandPressed;
    public event EventHandler<Region> AdjacentRegionClicked;

    public override void _Ready()
    {
        base._Ready();
        ClipContents = true;

        _legacyDataPanel = GetNodeOrNull<Panel>("DataPanel");
        _legacySquadTreePanel = GetNodeOrNull<Panel>("SquadTreePanel");
        _legacyOrdersPanel = GetNodeOrNull<Panel>("OrdersPanel");
        _regionPanel = GetNode<Panel>("RegionPanel");

        _centerRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionCenter");
        _northRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorth");
        _northeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNortheast");
        _southeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSoutheast");
        _southRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouth");
        _southwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouthwest");
        _northwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorthwest");

        ConnectAdjacentRegionSignal(_northRegionController);
        ConnectAdjacentRegionSignal(_northeastRegionController);
        ConnectAdjacentRegionSignal(_southeastRegionController);
        ConnectAdjacentRegionSignal(_southRegionController);
        ConnectAdjacentRegionSignal(_southwestRegionController);
        ConnectAdjacentRegionSignal(_northwestRegionController);

        HideLegacyPanels();
        ConfigureRegionPanel();
        BuildWorkspaceShell();
        SetMode(RegionCommandMode.Overview);
    }

    public void SetMode(RegionCommandMode mode)
    {
        foreach (KeyValuePair<RegionCommandMode, Button> pair in _modeButtons)
        {
            pair.Value.ButtonPressed = pair.Key == mode;
            OnlyWarStyle.ApplyAccentButtonRow(pair.Value, pair.Key == mode, OnlyWarStyle.Gold);
        }
    }

    public void SetSelectionTitle(string title, string hint)
    {
        _selectionTitleLabel.Text = title;
        _selectionHintLabel.Text = hint;
    }

    public void PopulateSelectionTree(IReadOnlyList<RegionCommandTreeNode> entries)
    {
        _selectionTree.Clear();
        TreeItem root = _selectionTree.CreateItem();
        _selectionTree.HideRoot = true;
        AddTreeChildren(_selectionTree, root, entries);
    }

    public void SetContext(string title, string subtitle, IReadOnlyList<Tuple<string, string>> rows)
    {
        _contextTitleLabel.Text = title;
        _contextSubtitleLabel.Text = subtitle;
        ClearContainer(_contextStack);

        foreach (Tuple<string, string> row in rows)
        {
            _contextStack.AddChild(CreateContextRow(row.Item1, row.Item2));
        }
    }

    public void SetCommands(IReadOnlyList<RegionCommandAction> actions)
    {
        ClearContainer(_commandBar);

        foreach (RegionCommandAction action in actions)
        {
            Button button = new()
            {
                Text = action.Text,
                Disabled = !action.Enabled,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(116, 42)
            };
            IconAtlas.Apply(button, action.IconKey, 116);
            button.Pressed += () => CommandPressed?.Invoke(this, action.Key);
            _commandBar.AddChild(button);
        }
    }

    public void PopulateAdjacentRegions(Region centerRegion, Dictionary<string, Region> adjacentRegions, RegionCommandMode mode)
    {
        PlanetCommandMode mapMode = ToPlanetCommandMode(mode);
        _centerRegionController.Populate(centerRegion, mapMode, true);

        void SetupAdjacentRegion(TacticalRegionController controller, string direction)
        {
            if (adjacentRegions.TryGetValue(direction, out Region region))
            {
                controller.Populate(region, mapMode, false);
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

    private void HideLegacyPanels()
    {
        if (_legacyDataPanel != null) _legacyDataPanel.Visible = false;
        if (_legacySquadTreePanel != null) _legacySquadTreePanel.Visible = false;
        if (_legacyOrdersPanel != null) _legacyOrdersPanel.Visible = false;
    }

    private void ConfigureRegionPanel()
    {
        _regionPanel.AnchorLeft = 0.245f;
        _regionPanel.AnchorTop = 0.08f;
        _regionPanel.AnchorRight = 0.755f;
        _regionPanel.AnchorBottom = 0.785f;
        _regionPanel.OffsetLeft = 0;
        _regionPanel.OffsetTop = 0;
        _regionPanel.OffsetRight = 0;
        _regionPanel.OffsetBottom = 0;
        _regionPanel.MouseFilter = MouseFilterEnum.Pass;

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
        _regionPanel.AddThemeStyleboxOverride("panel", mapStyle);
    }

    private void BuildWorkspaceShell()
    {
        PanelContainer leftPanel = CreatePanel("RegionModeAndSelectionPanel", 0.01f, 0.08f, 0.235f, 0.91f);
        VBoxContainer leftStack = new();
        leftStack.AddThemeConstantOverride("separation", 8);
        leftPanel.AddChild(leftStack);

        leftStack.AddChild(CreateCaption("REGION COMMAND"));

        _modeButtonStack = new VBoxContainer();
        _modeButtonStack.AddThemeConstantOverride("separation", 5);
        leftStack.AddChild(_modeButtonStack);
        AddModeButton(RegionCommandMode.Overview, "Overview", "map_pin");
        AddModeButton(RegionCommandMode.Forces, "Forces", "infantry");
        AddModeButton(RegionCommandMode.Orders, "Orders", "objective");
        AddModeButton(RegionCommandMode.Intel, "Intel", "threat");

        _selectionTitleLabel = CreateCaption("SELECTIONS");
        leftStack.AddChild(_selectionTitleLabel);

        _selectionTree = new Tree
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 290)
        };
        _selectionTree.ItemSelected += OnSelectionTreeItemSelected;
        _selectionTree.ItemActivated += OnSelectionTreeItemActivated;
        leftStack.AddChild(_selectionTree);

        _selectionHintLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Select a squad, order, or adjacent region to inspect it."
        };
        _selectionHintLabel.AddThemeFontSizeOverride("font_size", 12);
        _selectionHintLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        leftStack.AddChild(_selectionHintLabel);

        PanelContainer contextPanel = CreatePanel("RegionContextPanel", 0.765f, 0.08f, 0.99f, 0.785f);
        VBoxContainer contextOuter = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        contextOuter.AddThemeConstantOverride("separation", 8);
        contextPanel.AddChild(contextOuter);

        _contextTitleLabel = new Label
        {
            Text = "Region Detail",
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        _contextTitleLabel.AddThemeFontSizeOverride("font_size", 22);
        _contextTitleLabel.AddThemeFontOverride("font", GetThemeFont("display"));
        contextOuter.AddChild(_contextTitleLabel);

        _contextSubtitleLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _contextSubtitleLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        contextOuter.AddChild(_contextSubtitleLabel);

        ScrollContainer contextScroll = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        contextOuter.AddChild(contextScroll);

        _contextStack = new VBoxContainer();
        _contextStack.AddThemeConstantOverride("separation", 6);
        _contextStack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contextScroll.AddChild(_contextStack);

        PanelContainer commandPanel = CreatePanel("RegionCommandPanel", 0.245f, 0.805f, 0.755f, 0.91f);
        _commandBar = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _commandBar.AddThemeConstantOverride("separation", 8);
        commandPanel.AddChild(_commandBar);
    }

    private PanelContainer CreatePanel(string name, float left, float top, float right, float bottom)
    {
        PanelContainer panel = new()
        {
            Name = name,
            AnchorLeft = left,
            AnchorTop = top,
            AnchorRight = right,
            AnchorBottom = bottom,
            OffsetLeft = 0,
            OffsetTop = 0,
            OffsetRight = 0,
            OffsetBottom = 0
        };
        OnlyWarStyle.ApplyContentPanel(panel);
        AddChild(panel);
        return panel;
    }

    private void AddModeButton(RegionCommandMode mode, string text, string iconKey)
    {
        Button button = new()
        {
            Text = text,
            ToggleMode = true,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(0, 40)
        };
        IconAtlas.Apply(button, iconKey);
        button.Pressed += () => ModeSelected?.Invoke(this, mode);
        _modeButtons[mode] = button;
        _modeButtonStack.AddChild(button);
    }

    private static Label CreateCaption(string text)
    {
        Label label = new() { Text = text };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        return label;
    }

    private Control CreateContextRow(string labelText, string valueText)
    {
        PanelContainer row = new()
        {
            CustomMinimumSize = new Vector2(0, 44),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        OnlyWarStyle.ApplyInsetPanel(row);

        HBoxContainer rowContent = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        rowContent.AddThemeConstantOverride("separation", 8);
        row.AddChild(rowContent);

        Label label = new()
        {
            Text = labelText,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(130, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        rowContent.AddChild(label);

        Label value = new()
        {
            Text = valueText,
            HorizontalAlignment = HorizontalAlignment.Right,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(130, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        rowContent.AddChild(value);

        return row;
    }

    private void ConnectAdjacentRegionSignal(TacticalRegionController controller)
    {
        if (controller != null)
        {
            controller.TacticalRegionPressed += (sender, region) => AdjacentRegionClicked?.Invoke(this, region);
        }
    }

    private void OnSelectionTreeItemSelected()
    {
        TreeItem item = _selectionTree.GetSelected();
        if (item == null) return;
        SelectionTreeItemSelected?.Invoke(this, item.GetMetadata(0).AsString());
    }

    private void OnSelectionTreeItemActivated()
    {
        TreeItem item = _selectionTree.GetSelected();
        if (item == null) return;
        SelectionTreeItemActivated?.Invoke(this, item.GetMetadata(0).AsString());
    }

    private static void AddTreeChildren(Tree tree, TreeItem parentItem, IReadOnlyList<RegionCommandTreeNode> nodes)
    {
        foreach (RegionCommandTreeNode node in nodes)
        {
            TreeItem item = tree.CreateItem(parentItem);
            item.SetText(0, node.Text);
            item.SetMetadata(0, Variant.From(node.Key));
            if (node.Children.Count > 0)
            {
                AddTreeChildren(tree, item, node.Children);
            }
        }
    }

    private static void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static PlanetCommandMode ToPlanetCommandMode(RegionCommandMode mode)
    {
        return mode switch
        {
            RegionCommandMode.Forces => PlanetCommandMode.Forces,
            RegionCommandMode.Orders => PlanetCommandMode.Orders,
            RegionCommandMode.Intel => PlanetCommandMode.Intel,
            _ => PlanetCommandMode.Overview
        };
    }
}
