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
    private PanelContainer _headerBadgePanel;
    private Label _selectionTitleLabel;
    private HFlowContainer _filterRow;
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

    // Switches the roster Tree between single- and multi-selection. Multi-select uses Godot's
    // standard ctrl/shift-click semantics; non-selectable rows (unit headers) are unaffected.
    public void SetSelectionMultiSelect(bool multiSelect)
    {
        _selectionTree.SelectMode = multiSelect ? Tree.SelectModeEnum.Multi : Tree.SelectModeEnum.Single;
    }

    // Returns the metadata keys of every currently-selected row in the roster Tree (order not
    // guaranteed). Callers filter by key prefix (e.g. "squad:") to resolve model objects.
    public IReadOnlyList<string> GetSelectedKeys()
    {
        List<string> keys = [];
        TreeItem root = _selectionTree.GetRoot();
        if (root != null)
        {
            CollectSelectedKeys(root.GetFirstChild(), keys);
        }
        return keys;
    }

    // Clears the roster Tree's selection without rebuilding it (used after a successful multi-squad
    // assignment so the next PopulateSelectionTree doesn't resurrect the old selection).
    public void ClearSelection()
    {
        _selectionTree.DeselectAll();
    }

    // Programmatically replaces the roster Tree's selection with the rows whose metadata keys are
    // in the given set (used when jumping to an order's origin region to pre-select its squads).
    public void SetSelectedKeys(IReadOnlyCollection<string> keys)
    {
        HashSet<string> wanted = [.. keys];
        _selectionTree.DeselectAll();
        TreeItem root = _selectionTree.GetRoot();
        if (root == null) return;

        foreach (TreeItem item in EnumerateTreeItems(root.GetFirstChild()))
        {
            string key = item.GetMetadata(0).AsString();
            if (!string.IsNullOrEmpty(key) && wanted.Contains(key))
            {
                item.Select(0);
            }
        }
    }

    private static void CollectSelectedKeys(TreeItem item, List<string> keys)
    {
        while (item != null)
        {
            if (item.IsSelected(0) || item.IsSelected(1))
            {
                string key = item.GetMetadata(0).AsString();
                if (!string.IsNullOrEmpty(key))
                {
                    keys.Add(key);
                }
            }
            CollectSelectedKeys(item.GetFirstChild(), keys);
            item = item.GetNext();
        }
    }

    public void PopulateSelectionTree(IReadOnlyList<CommandTreeNode> entries)
    {
        Dictionary<string, bool> collapsedByKey = CaptureSelectionTreeCollapsedStates();
        // Capture the full selection set (not just GetSelected()'s single anchor) so a rebuild —
        // e.g. after a filter change or refresh — preserves a multi-squad selection intact.
        HashSet<string> selectedKeys = [.. GetSelectedKeys()];

        _selectionTree.Clear();
        TreeItem root = _selectionTree.CreateItem();
        _selectionTree.HideRoot = true;
        AddTreeChildren(_selectionTree, root, entries, collapsedByKey, selectedKeys);
    }

    // Renders the right-hand context panel as accent-tinted dossier cards, matching the Region Ops
    // "selected target" panel. Title/subtitle head the panel; each card groups related rows under a
    // muted category label with an accent-colored subtitle and optional strength bar.
    public void SetContextCards(string title, string subtitle, IReadOnlyList<DossierCardData> cards)
    {
        _contextTitleLabel.Text = title;
        _contextSubtitleLabel.Text = subtitle;
        ClearContainer(_contextStack);

        foreach (DossierCardData card in cards)
        {
            _contextStack.AddChild(DossierCard.Create(card));
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
                    // ClipText keeps the button's minimum width at the 116px floor (rather than the
                    // full text width), so a long dynamic label like "Land From Immortal" can't push
                    // the right-aligned row past the panel's left edge and over the roster tree.
                    ClipText = true,
                    // ExpandFill lets the buttons share the command bar's free width evenly, so they
                    // grow to fit their text instead of shrinking to 116px and truncating.
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
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
        if (_headerBreadcrumbLabel == null) return;
        _headerBreadcrumbLabel.Text = breadcrumb;
        _headerBadgeLabel.Visible = !string.IsNullOrEmpty(badgeText);
        _headerBadgeLabel.Text = badgeText ?? "";
        _headerBadgePanel.Visible = _headerBadgeLabel.Visible;
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

    // includeHeader=false skips the breadcrumb/layer-toggle header bar entirely (for screens whose
    // identity already lives in the app's top menu, e.g. Region Ops); topAnchor lets those screens
    // reclaim the header band by starting their panels higher.
    protected void BuildWorkspaceShell(float mapRightAnchor, float contextBottomAnchor, float commandTopAnchor = 0.805f, bool includeHeader = true, float topAnchor = 0.08f)
    {
        if (includeHeader)
        {
            BuildHeaderBar();
        }

        PanelContainer leftPanel = CreatePanel("RosterPanel", 0.01f, topAnchor, 0.235f, 0.91f);
        VBoxContainer leftStack = new();
        leftStack.AddThemeConstantOverride("separation", 8);
        leftPanel.AddChild(leftStack);

        _selectionTitleLabel = CreateCaption("ROSTER");
        leftStack.AddChild(_selectionTitleLabel);

        // A flow container so a filter set wider than the roster panel wraps onto a second
        // row instead of forcing the panel's minimum width past its anchors and under the map.
        _filterRow = new HFlowContainer();
        _filterRow.AddThemeConstantOverride("separation", 4);
        leftStack.AddChild(_filterRow);

        _selectionTree = new Tree
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 260),
            Columns = 2
        };
        _selectionTree.SetColumnExpand(0, true);
        _selectionTree.SetColumnExpand(1, false);
        // Wider badge column so target-region names ("Terra Lambda") aren't clipped, and a smaller
        // per-level indent so squad rows sit closer under their company header — the reclaimed
        // horizontal space goes to the badge.
        _selectionTree.SetColumnCustomMinimumWidth(1, 116);
        _selectionTree.AddThemeConstantOverride("item_margin", 6);
        _selectionTree.ItemSelected += OnSelectionTreeItemSelected;
        // In SELECT_MULTI mode Godot emits multi_selected (not item_selected) for every selection
        // change, so screens using multi-select (e.g. the Region roster) must listen here too or
        // they never learn the selection changed. Single-select screens simply never fire it.
        _selectionTree.MultiSelected += OnSelectionTreeMultiSelected;
        _selectionTree.ItemActivated += OnSelectionTreeItemActivated;
        leftStack.AddChild(_selectionTree);

        _selectionHintLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _selectionHintLabel.AddThemeFontSizeOverride("font_size", 12);
        _selectionHintLabel.AddThemeColorOverride("font_color", OnlyWarStyle.MutedText);
        leftStack.AddChild(_selectionHintLabel);

        PanelContainer contextPanel = CreatePanel("ContextPanel", mapRightAnchor + 0.01f, topAnchor, 0.99f, contextBottomAnchor);
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
        _contextStack.AddThemeConstantOverride("separation", 8);
        _contextStack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contextScroll.AddChild(_contextStack);

        // Inset the command bar's left edge past the roster's 0.235 right edge with a clear gutter.
        // The map above shares the roster's 0.245 neighbour but hides it (subtle bg, inset hexes);
        // the command bar's opaque panel does not, so at 0.245 it reads as crowding/overlapping the
        // roster tree. 0.26 gives ~40px of clear space regardless of the roster's content bleed.
        PanelContainer commandPanel = CreatePanel("CommandPanel", 0.26f, commandTopAnchor, mapRightAnchor, 0.91f);
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

        // Hidden until a badge is set; otherwise the empty inset panel renders as a
        // small dark box at the header's right edge.
        _headerBadgePanel = new PanelContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Visible = false
        };
        OnlyWarStyle.ApplyInsetPanel(_headerBadgePanel);
        headerRow.AddChild(_headerBadgePanel);

        _headerBadgeLabel = new Label
        {
            Visible = false
        };
        _headerBadgeLabel.AddThemeFontSizeOverride("font_size", 12);
        _headerBadgeLabel.AddThemeColorOverride("font_color", OnlyWarStyle.Gold);
        _headerBadgePanel.AddChild(_headerBadgeLabel);
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

    private void OnSelectionTreeItemSelected()
    {
        TreeItem item = _selectionTree.GetSelected();
        if (item == null) return;
        SelectionTreeItemSelected?.Invoke(this, item.GetMetadata(0).AsString());
    }

    // Fires on every add/remove in multi-select mode. The key is only informational — callers
    // recompute the whole selection from GetSelectedKeys() — so it's enough to signal "changed".
    private void OnSelectionTreeMultiSelected(TreeItem item, long column, bool selected)
    {
        string key = item != null ? item.GetMetadata(0).AsString() : "";
        SelectionTreeItemSelected?.Invoke(this, key);
    }

    private void OnSelectionTreeItemActivated()
    {
        TreeItem item = _selectionTree.GetSelected();
        if (item == null) return;
        SelectionTreeItemActivated?.Invoke(this, item.GetMetadata(0).AsString());
    }

    private Dictionary<string, bool> CaptureSelectionTreeCollapsedStates()
    {
        Dictionary<string, bool> collapsedByKey = [];
        TreeItem root = _selectionTree.GetRoot();
        if (root == null)
        {
            return collapsedByKey;
        }

        foreach (TreeItem item in EnumerateTreeItems(root.GetFirstChild()))
        {
            string key = item.GetMetadata(0).AsString();
            if (!string.IsNullOrEmpty(key))
            {
                collapsedByKey[key] = item.Collapsed;
            }
        }

        return collapsedByKey;
    }

    private static IEnumerable<TreeItem> EnumerateTreeItems(TreeItem item)
    {
        while (item != null)
        {
            yield return item;

            foreach (TreeItem child in EnumerateTreeItems(item.GetFirstChild()))
            {
                yield return child;
            }

            item = item.GetNext();
        }
    }

    private static void AddTreeChildren(
        Tree tree,
        TreeItem parentItem,
        IReadOnlyList<CommandTreeNode> nodes,
        IReadOnlyDictionary<string, bool> collapsedByKey,
        IReadOnlySet<string> selectedKeys)
    {
        foreach (CommandTreeNode node in nodes)
        {
            TreeItem item = tree.CreateItem(parentItem);
            item.SetText(0, node.Text);
            item.SetMetadata(0, Variant.From(node.Key));

            if (node.IconKey != null)
            {
                item.SetIcon(0, IconAtlas.GetIcon(node.IconKey));
                item.SetIconMaxWidth(0, 20);
            }

            if (node.Badge != null)
            {
                item.SetText(1, node.Badge);
                item.SetTextAlignment(1, HorizontalAlignment.Right);
                item.SetCustomColor(1, OnlyWarStyle.MutedText);
                // The badge is a passive label, not its own selection target: make the cell
                // non-selectable so a click anywhere on the row selects the row (column 0) rather
                // than the badge cell in isolation. This also keeps multi-select selections landing
                // on column 0 so GetSelectedKeys sees every picked squad.
                item.SetSelectable(1, false);
            }

            if (!node.Selectable)
            {
                item.SetSelectable(0, false);
                item.SetCustomColor(0, OnlyWarStyle.MutedText);
            }

            if (node.Selectable && selectedKeys.Contains(node.Key))
            {
                item.Select(0);
            }
            if (node.Children.Count > 0)
            {
                AddTreeChildren(tree, item, node.Children, collapsedByKey, selectedKeys);
                if (collapsedByKey.TryGetValue(node.Key, out bool wasCollapsed))
                {
                    item.Collapsed = wasCollapsed;
                }
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
