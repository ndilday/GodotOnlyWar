using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public enum PlanetCommandMode
{
    Overview,
    Forces,
    Orders,
    Logistics,
    Intel
}

public class PlanetCommandTreeNode
{
    public string Key { get; }
    public string Text { get; }
    public IReadOnlyList<PlanetCommandTreeNode> Children { get; }

    public PlanetCommandTreeNode(string key, string text, IReadOnlyList<PlanetCommandTreeNode> children = null)
    {
        Key = key;
        Text = text;
        Children = children ?? Array.Empty<PlanetCommandTreeNode>();
    }
}

public class PlanetCommandAction
{
    public string Key { get; }
    public string Text { get; }
    public string IconKey { get; }
    public bool Enabled { get; }

    public PlanetCommandAction(string key, string text, string iconKey, bool enabled)
    {
        Key = key;
        Text = text;
        IconKey = iconKey;
        Enabled = enabled;
    }
}

public partial class PlanetTacticalScreenView : DialogView
{
    private readonly Dictionary<PlanetCommandMode, Button> _modeButtons = [];
    private readonly Dictionary<string, Button> _commandButtons = [];

    private Panel _legacyDataPanel;
    private Panel _legacyOrbitPanel;
    private Panel _legacyButtonPanel;
    private Control _tacticalRegionPanel;
    private VBoxContainer _modeButtonStack;
    private Label _selectionTitleLabel;
    private Tree _selectionTree;
    private Label _selectionHintLabel;
    private Label _contextTitleLabel;
    private Label _contextSubtitleLabel;
    private VBoxContainer _contextStack;
    private HBoxContainer _commandBar;

    public event EventHandler<PlanetCommandMode> ModeSelected;
    public event EventHandler<string> SelectionTreeItemSelected;
    public event EventHandler<string> SelectionTreeItemActivated;
    public event EventHandler<string> CommandPressed;

    public override void _Ready()
    {
        base._Ready();
        ClipContents = true;
        _legacyDataPanel = GetNodeOrNull<Panel>("DataPanel");
        _legacyOrbitPanel = GetNodeOrNull<Panel>("OrbitPanel");
        _legacyButtonPanel = GetNodeOrNull<Panel>("ButtonPanel");
        _tacticalRegionPanel = GetNode<Control>("TacticalRegionPanel");

        HideLegacyPanels();
        ConfigureMapPanel();
        BuildWorkspaceShell();
        CallDeferred(nameof(LayoutRegionHexGrid));
        SetMode(PlanetCommandMode.Overview);
    }

    public void SetMode(PlanetCommandMode mode)
    {
        foreach (KeyValuePair<PlanetCommandMode, Button> pair in _modeButtons)
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

    public void PopulateSelectionTree(IReadOnlyList<PlanetCommandTreeNode> entries)
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

    public void SetCommands(IReadOnlyList<PlanetCommandAction> actions)
    {
        ClearContainer(_commandBar);
        _commandButtons.Clear();

        foreach (PlanetCommandAction action in actions)
        {
            Button button = new()
            {
                Text = action.Text,
                Disabled = !action.Enabled,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(150, 42)
            };
            IconAtlas.Apply(button, action.IconKey, 150);
            button.Pressed += () => CommandPressed?.Invoke(this, action.Key);
            _commandButtons[action.Key] = button;
            _commandBar.AddChild(button);
        }
    }

    private void HideLegacyPanels()
    {
        if (_legacyDataPanel != null) _legacyDataPanel.Visible = false;
        if (_legacyOrbitPanel != null) _legacyOrbitPanel.Visible = false;
        if (_legacyButtonPanel != null) _legacyButtonPanel.Visible = false;
    }

    private void ConfigureMapPanel()
    {
        _tacticalRegionPanel.AnchorLeft = 0.245f;
        _tacticalRegionPanel.AnchorTop = 0.08f;
        _tacticalRegionPanel.AnchorRight = 0.705f;
        _tacticalRegionPanel.AnchorBottom = 0.785f;
        _tacticalRegionPanel.OffsetLeft = 0;
        _tacticalRegionPanel.OffsetTop = 0;
        _tacticalRegionPanel.OffsetRight = 0;
        _tacticalRegionPanel.OffsetBottom = 0;
        _tacticalRegionPanel.SelfModulate = Colors.White;

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
        _tacticalRegionPanel.AddThemeStyleboxOverride("panel", mapStyle);
    }

    public void LayoutRegionHexGrid()
    {
        int[] rowCounts = [1, 2, 3, 4, 3, 2, 1];
        const float visibleGridWidth = 0.82f;
        const float flatTopRatio = 2f / 1.7320508f;
        const float spacingFactor = 1.04f;
        float panelWidth = Mathf.Max(_tacticalRegionPanel.Size.X, 736f);
        float panelHeight = Mathf.Max(_tacticalRegionPanel.Size.Y, 538f);
        float panelAspect = panelWidth / panelHeight;
        float hexWidth = visibleGridWidth / (1f + 3f * 1.5f * spacingFactor);
        float hexHeight = hexWidth * panelAspect / flatTopRatio;
        float xStep = hexWidth * 1.5f * spacingFactor;
        float yStep = hexHeight * 0.5f * spacingFactor;
        float tileWidth = hexWidth + 12f / panelWidth;
        float tileHeight = hexHeight + 12f / panelHeight;
        float totalVisibleHeight = hexHeight + (rowCounts.Length - 1) * yStep;
        float startY = (1f - totalVisibleHeight) * 0.5f;
        int regionIndex = 1;

        for (int row = 0; row < rowCounts.Length; row++)
        {
            int count = rowCounts[row];
            float yCenter = startY + hexHeight * 0.5f + row * yStep;
            float rowHalf = (count - 1) * 0.5f;
            for (int col = 0; col < count; col++)
            {
                Control regionControl = _tacticalRegionPanel.GetNodeOrNull<Control>($"TacticalRegionController{regionIndex}");
                if (regionControl != null)
                {
                    float xCenter = 0.5f + (col - rowHalf) * xStep;
                    regionControl.AnchorLeft = xCenter - tileWidth * 0.5f;
                    regionControl.AnchorRight = xCenter + tileWidth * 0.5f;
                    regionControl.AnchorTop = yCenter - tileHeight * 0.5f;
                    regionControl.AnchorBottom = yCenter + tileHeight * 0.5f;
                    regionControl.OffsetLeft = 0;
                    regionControl.OffsetTop = 0;
                    regionControl.OffsetRight = 0;
                    regionControl.OffsetBottom = 0;
                }
                regionIndex++;
            }
        }
    }

    private void BuildWorkspaceShell()
    {
        PanelContainer leftPanel = CreatePanel("ModeAndSelectionPanel", 0.01f, 0.08f, 0.235f, 0.91f);
        VBoxContainer leftStack = new();
        leftStack.AddThemeConstantOverride("separation", 8);
        leftPanel.AddChild(leftStack);

        Label modeTitle = CreateCaption("PLANET COMMAND");
        leftStack.AddChild(modeTitle);

        _modeButtonStack = new VBoxContainer();
        _modeButtonStack.AddThemeConstantOverride("separation", 5);
        leftStack.AddChild(_modeButtonStack);
        AddModeButton(PlanetCommandMode.Overview, "Overview", "planet");
        AddModeButton(PlanetCommandMode.Forces, "Forces", "infantry");
        AddModeButton(PlanetCommandMode.Orders, "Orders", "objective");
        AddModeButton(PlanetCommandMode.Logistics, "Logistics", "land_squads");
        AddModeButton(PlanetCommandMode.Intel, "Intel", "threat");

        _selectionTitleLabel = CreateCaption("SELECTIONS");
        leftStack.AddChild(_selectionTitleLabel);

        _selectionTree = new Tree
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 260)
        };
        _selectionTree.ItemSelected += OnSelectionTreeItemSelected;
        _selectionTree.ItemActivated += OnSelectionTreeItemActivated;
        leftStack.AddChild(_selectionTree);

        _selectionHintLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Select a region or force to inspect it."
        };
        _selectionHintLabel.AddThemeFontSizeOverride("font_size", 12);
        _selectionHintLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        leftStack.AddChild(_selectionHintLabel);

        PanelContainer contextPanel = CreatePanel("ContextPanel", 0.715f, 0.08f, 0.99f, 0.91f);
        VBoxContainer contextOuter = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        contextOuter.AddThemeConstantOverride("separation", 8);
        contextPanel.AddChild(contextOuter);

        _contextTitleLabel = new Label
        {
            Text = "Planet Detail",
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

        PanelContainer commandPanel = CreatePanel("CommandPanel", 0.245f, 0.805f, 0.705f, 0.91f);
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

    private void AddModeButton(PlanetCommandMode mode, string text, string iconKey)
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

    private static void AddTreeChildren(Tree tree, TreeItem parentItem, IReadOnlyList<PlanetCommandTreeNode> nodes)
    {
        foreach (PlanetCommandTreeNode node in nodes)
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
}
