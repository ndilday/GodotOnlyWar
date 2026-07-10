using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public partial class CommandWorkspaceView : DialogView
{
    private readonly Dictionary<MapLayer, Button> _layerButtons = [];
    private readonly Dictionary<string, Button> _filterButtons = [];

    private Panel _headerPanel;
    private Label _headerBreadcrumbLabel;
    private HBoxContainer _layerToggleRow;
    private Label _headerBadgeLabel;
    private Label _selectionTitleLabel;
    private HBoxContainer _filterRow;
    private Tree _selectionTree;
    private Label _selectionHintLabel;
    private Label _contextTitleLabel;
    private Label _contextSubtitleLabel;
    private VBoxContainer _contextStack;
    private VBoxContainer _commandStack;

    public event EventHandler<string> SelectionTreeItemSelected;
    public event EventHandler<string> SelectionTreeItemActivated;
    public event EventHandler<string> CommandPressed;
    public event EventHandler<MapLayer> MapLayerToggled;
    public event EventHandler<string> RosterFilterSelected;

    public void SetSelectionTitle(string title, string hint)
    {
        _selectionTitleLabel.Text = title;
        _selectionHintLabel.Text = hint;
    }

    public void PopulateSelectionTree(IReadOnlyList<CommandTreeNode> entries)
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

    public void SetCommands(IReadOnlyList<CommandAction> actions)
    {
        SetCommandRows([actions]);
    }

    public void SetCommandRows(IReadOnlyList<IReadOnlyList<CommandAction>> actionRows)
    {
        ClearContainer(_commandStack);

        foreach (IReadOnlyList<CommandAction> rowActions in actionRows)
        {
            HBoxContainer row = new()
            {
                Alignment = BoxContainer.AlignmentMode.End,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            row.AddThemeConstantOverride("separation", 8);
            _commandStack.AddChild(row);

            foreach (CommandAction action in rowActions)
            {
                Button button = new()
                {
                    Text = action.Text,
                    Disabled = !action.Enabled,
                    MouseDefaultCursorShape = CursorShape.PointingHand,
                    CustomMinimumSize = new Vector2(116, 42),
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    TooltipText = action.Text
                };
                IconAtlas.Apply(button, action.IconKey, 116);
                button.Pressed += () => CommandPressed?.Invoke(this, action.Key);
                row.AddChild(button);
            }
        }
    }

    public void SetHeader(string breadcrumb, string badgeText = null)
    {
        _headerBreadcrumbLabel.Text = breadcrumb;
        _headerBadgeLabel.Visible = !string.IsNullOrEmpty(badgeText);
        _headerBadgeLabel.Text = badgeText ?? "";
    }

    public void SetMapLayerOptions(IReadOnlyList<(MapLayer Layer, string Label, string IconKey)> options)
    {
        foreach (KeyValuePair<MapLayer, Button> pair in _layerButtons)
        {
            _layerToggleRow.RemoveChild(pair.Value);
            pair.Value.QueueFree();
        }
        _layerButtons.Clear();

        foreach ((MapLayer layer, string label, string iconKey) in options)
        {
            Button button = new()
            {
                Text = label,
                ToggleMode = true,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 28)
            };
            IconAtlas.Apply(button, iconKey, 90);
            button.Pressed += () => MapLayerToggled?.Invoke(this, layer);
            _layerButtons[layer] = button;
            _layerToggleRow.AddChild(button);
        }
    }

    public void SetActiveMapLayers(MapLayer active)
    {
        foreach (KeyValuePair<MapLayer, Button> pair in _layerButtons)
        {
            bool isActive = active.HasFlag(pair.Key);
            pair.Value.ButtonPressed = isActive;
            OnlyWarStyle.ApplyAccentButtonRow(pair.Value, isActive, OnlyWarStyle.PlayerAccent);
        }
    }

    public void SetRosterFilters(IReadOnlyList<(string Key, string Label)> filters)
    {
        foreach (KeyValuePair<string, Button> pair in _filterButtons)
        {
            _filterRow.RemoveChild(pair.Value);
            pair.Value.QueueFree();
        }
        _filterButtons.Clear();

        foreach ((string key, string label) in filters)
        {
            Button button = new()
            {
                Text = label,
                ToggleMode = true,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(0, 28)
            };
            button.Pressed += () => RosterFilterSelected?.Invoke(this, key);
            _filterButtons[key] = button;
            _filterRow.AddChild(button);
        }
    }

    public void SetActiveRosterFilter(string activeKey)
    {
        foreach (KeyValuePair<string, Button> pair in _filterButtons)
        {
            bool isActive = pair.Key == activeKey;
            pair.Value.ButtonPressed = isActive;
            OnlyWarStyle.ApplyAccentButtonRow(pair.Value, isActive, OnlyWarStyle.Gold);
        }
    }

    protected void BuildWorkspaceShell(float mapRightAnchor, float contextBottomAnchor, float commandTopAnchor = 0.805f)
    {
        BuildHeaderBar();

        PanelContainer leftPanel = CreatePanel("RosterPanel", 0.01f, 0.08f, 0.235f, 0.91f);
        VBoxContainer leftStack = new();
        leftStack.AddThemeConstantOverride("separation", 8);
        leftPanel.AddChild(leftStack);

        _selectionTitleLabel = CreateCaption("ROSTER");
        leftStack.AddChild(_selectionTitleLabel);

        _filterRow = new HBoxContainer();
        _filterRow.AddThemeConstantOverride("separation", 4);
        leftStack.AddChild(_filterRow);

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
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _selectionHintLabel.AddThemeFontSizeOverride("font_size", 12);
        _selectionHintLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        leftStack.AddChild(_selectionHintLabel);

        PanelContainer contextPanel = CreatePanel("ContextPanel", mapRightAnchor + 0.01f, 0.08f, 0.99f, contextBottomAnchor);
        VBoxContainer contextOuter = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        contextOuter.AddThemeConstantOverride("separation", 8);
        contextPanel.AddChild(contextOuter);

        _contextTitleLabel = new Label
        {
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

        PanelContainer commandPanel = CreatePanel("CommandPanel", 0.245f, commandTopAnchor, mapRightAnchor, 0.91f);
        _commandStack = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _commandStack.AddThemeConstantOverride("separation", 6);
        commandPanel.AddChild(_commandStack);
    }

    private void BuildHeaderBar()
    {
        _headerPanel = new Panel
        {
            Name = "HeaderBar",
            AnchorLeft = 0.01f,
            AnchorTop = 0.005f,
            AnchorRight = 0.87f,
            AnchorBottom = 0.07f,
            OffsetLeft = 0,
            OffsetTop = 0,
            OffsetRight = 0,
            OffsetBottom = 0,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_headerPanel);

        HBoxContainer headerRow = new()
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        headerRow.AddThemeConstantOverride("separation", 12);
        _headerPanel.AddChild(headerRow);

        _headerBreadcrumbLabel = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _headerBreadcrumbLabel.AddThemeFontSizeOverride("font_size", 16);
        _headerBreadcrumbLabel.AddThemeFontOverride("font", GetThemeFont("display"));
        headerRow.AddChild(_headerBreadcrumbLabel);

        _layerToggleRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        _layerToggleRow.AddThemeConstantOverride("separation", 4);
        headerRow.AddChild(_layerToggleRow);

        PanelContainer badgePanel = new()
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        OnlyWarStyle.ApplyInsetPanel(badgePanel);
        headerRow.AddChild(badgePanel);

        _headerBadgeLabel = new Label
        {
            Visible = false
        };
        _headerBadgeLabel.AddThemeFontSizeOverride("font_size", 12);
        _headerBadgeLabel.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        badgePanel.AddChild(_headerBadgeLabel);
    }

    protected PanelContainer CreatePanel(string name, float left, float top, float right, float bottom)
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

    private static void AddTreeChildren(Tree tree, TreeItem parentItem, IReadOnlyList<CommandTreeNode> nodes)
    {
        foreach (CommandTreeNode node in nodes)
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
