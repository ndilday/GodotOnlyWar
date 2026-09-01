using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared hierarchical roster behavior rendered as full-width rows.
///
/// Screens provide presentation rows and receive stable string keys back. The view owns row
/// selection, activation, expansion, scroll restoration, and the common icon/name/badge layout.
/// Fleet-specific drag/drop remains outside this component.
/// </summary>
public partial class HierarchyTreeView : ScrollContainer
{
    public const int DefaultIconMaxWidth = 20;
    private const int IndentWidth = 8;
    private const int TextMinimumWidth = 96;
    private const int ExpanderWidth = 14;
    private const int RowDefaultHeight = 34;

    private VBoxContainer _content;
    private readonly List<RowState> _rows = [];
    private readonly Dictionary<string, RowState> _rowsByKey = [];
    private bool _suppressSelectionSignals;

    public int IconMaxWidth { get; set; } = DefaultIconMaxWidth;

    // Retain Godot's enum at the boundary so existing screens can continue to configure the
    // component without depending on a second selection-mode type. Row mode is treated as single
    // selection because every click target is already the complete row.
    public Tree.SelectModeEnum SelectMode { get; set; } = Tree.SelectModeEnum.Single;

    public bool AllowReselect { get; set; }

    public event EventHandler<string> SelectionChanged;
    public event EventHandler<string> Activated;

    public override void _Ready()
    {
        base._Ready();
        EnsureContent();
    }

    public void Populate(
        IReadOnlyList<HierarchyTreeItem> entries,
        bool preserveUiState = true,
        bool suppressSelectionSignals = false)
    {
        EnsureContent();

        Dictionary<string, bool> collapsedByPath = preserveUiState
            ? CaptureCollapsedStates()
            : [];
        HashSet<string> selectedKeys = preserveUiState
            ? [.. GetSelectedKeys()]
            : [];

        bool previous = _suppressSelectionSignals;
        _suppressSelectionSignals = suppressSelectionSignals;
        try
        {
            ClearRows();
            AddTreeItems(
                _content,
                entries ?? Array.Empty<HierarchyTreeItem>(),
                "",
                null,
                0,
                collapsedByPath,
                selectedKeys);

            ExpandSelectedAncestors();
        }
        finally
        {
            _suppressSelectionSignals = previous;
        }

        if (!suppressSelectionSignals)
        {
            string selectedKey = GetSelectedKeys().FirstOrDefault() ?? "";
            SelectionChanged?.Invoke(this, selectedKey);
        }
    }

    public IReadOnlyList<string> GetSelectedKeys()
    {
        return _rows
            .Where(row => row.Selected)
            .Select(row => row.Entry.Key)
            .ToList();
    }

    public int GetVerticalScrollOffset() => ScrollVertical;

    public void SetVerticalScrollOffset(int offset)
    {
        SetDeferred("scroll_vertical", Math.Max(0, offset));
    }

    public IReadOnlyDictionary<string, bool> GetCollapsedStatesByKey()
    {
        return _rows.ToDictionary(row => row.Entry.Key, row => !row.Expanded);
    }

    public void SetCollapsedStates(IReadOnlyDictionary<string, bool> states)
    {
        if (states == null || states.Count == 0)
        {
            return;
        }

        foreach (RowState row in _rows)
        {
            if (states.TryGetValue(row.Entry.Key, out bool collapsed))
            {
                SetExpanded(row, !collapsed);
            }
        }
    }

    public void ClearSelection()
    {
        bool changed = ClearSelectionInternal();
        if (changed && !_suppressSelectionSignals)
        {
            SelectionChanged?.Invoke(this, "");
        }
    }

    /// <summary>
    /// Selects the rows whose keys are in <paramref name="keys"/>. Selection restoration also
    /// expands each selected row's ancestors so the requested rows remain visible.
    /// </summary>
    public void SetSelectedKeys(IReadOnlyCollection<string> keys, bool suppressSelectionSignals = false)
    {
        HashSet<string> wanted = keys == null ? [] : [.. keys];
        bool previous = _suppressSelectionSignals;
        _suppressSelectionSignals = suppressSelectionSignals;
        try
        {
            ClearSelectionInternal();
            foreach (RowState row in _rows)
            {
                if (!row.Entry.Selectable || !wanted.Contains(row.Entry.Key))
                {
                    continue;
                }

                row.Selected = true;
                RefreshRowStyle(row);
                for (RowState ancestor = row.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    SetExpanded(ancestor, true);
                }
            }
        }
        finally
        {
            _suppressSelectionSignals = previous;
        }

        if (!suppressSelectionSignals)
        {
            SelectionChanged?.Invoke(this, wanted.FirstOrDefault(key => _rowsByKey.ContainsKey(key)) ?? "");
        }
    }

    private void EnsureContent()
    {
        if (_content != null && IsInstanceValid(_content))
        {
            return;
        }

        HorizontalScrollMode = ScrollMode.Disabled;
        VerticalScrollMode = ScrollMode.Auto;
        FollowFocus = true;
        ClipContents = true;

        _content = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _content.AddThemeConstantOverride("separation", 3);
        AddChild(_content);
    }

    private void ClearRows()
    {
        foreach (Node child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        _rows.Clear();
        _rowsByKey.Clear();
    }

    private bool AddTreeItems(
        Container parent,
        IReadOnlyList<HierarchyTreeItem> entries,
        string parentPath,
        RowState parentRow,
        int depth,
        IReadOnlyDictionary<string, bool> collapsedByPath,
        IReadOnlySet<string> selectedKeys)
    {
        bool containsSelection = false;
        foreach (HierarchyTreeItem entry in entries)
        {
            string path = $"{parentPath}/{entry.Key}";
            VBoxContainer branch = new()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore
            };
            parent.AddChild(branch);

            RowState row = CreateRow(entry, parentRow, depth);
            row.Path = path;
            branch.AddChild(row.Panel);
            _rows.Add(row);
            _rowsByKey[entry.Key] = row;

            bool childContainsSelection = false;
            if (entry.Children.Count > 0)
            {
                VBoxContainer childContainer = new()
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                childContainer.AddThemeConstantOverride("separation", 3);
                branch.AddChild(childContainer);
                row.Children = childContainer;
                childContainsSelection = AddTreeItems(
                    childContainer,
                    entry.Children,
                    path,
                    row,
                    depth + 1,
                    collapsedByPath,
                    selectedKeys);

                bool expanded = collapsedByPath.TryGetValue(path, out bool wasCollapsed)
                    ? !wasCollapsed
                    : !entry.CollapsedByDefault;
                SetExpanded(row, expanded);
            }

            containsSelection |= row.Selected || childContainsSelection;
            if (childContainsSelection)
            {
                SetExpanded(row, true);
            }
        }

        return containsSelection;
    }

    private RowState CreateRow(HierarchyTreeItem entry, RowState parent, int depth)
    {
        if (entry.SquadRow != null)
        {
            return CreateSquadRow(entry, parent, depth);
        }

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(0,
                entry.RowHeight > 0 ? entry.RowHeight : RowDefaultHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = entry.Tooltip ?? "",
            MouseDefaultCursorShape = entry.Selectable
                ? CursorShape.PointingHand
                : CursorShape.Arrow
        };
        OnlyWarStyle.ApplyListRow(panel, false);

        HBoxContainer content = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.AddThemeConstantOverride("separation", RosterRowStyle.ContentSeparation);
        panel.AddChild(content);

        content.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(depth * IndentWidth, 0),
            MouseFilter = MouseFilterEnum.Ignore
        });

        Button expander = null;
        if (entry.Children.Count > 0)
        {
            expander = new Button
            {
                CustomMinimumSize = new Vector2(ExpanderWidth, 0),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                Flat = true,
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                MouseFilter = MouseFilterEnum.Stop
            };
            expander.AddThemeFontSizeOverride("font_size", 12);
            content.AddChild(expander);
        }
        if (!string.IsNullOrWhiteSpace(entry.IconKey))
        {
            int iconWidth = Math.Max(1, entry.IconMaxWidth > 0 ? entry.IconMaxWidth : IconMaxWidth);
            TextureRect icon = new()
            {
                Texture = IconAtlas.GetIcon(entry.IconKey),
                CustomMinimumSize = new Vector2(iconWidth, iconWidth),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore
            };
            content.AddChild(icon);
        }

        Color primaryColor = entry.Selectable ? OnlyWarStyle.BodyText : OnlyWarStyle.MutedText;
        Label text = new()
        {
            Text = entry.Text,
            // Keep the name column from collapsing to the width of a single glyph when the
            // trailing badge has a larger minimum width. Rows are fixed-height, so wrapping
            // would only hide the rest of the name; trim it on one line instead.
            CustomMinimumSize = new Vector2(TextMinimumWidth, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore
        };
        text.AddThemeColorOverride("font_color", primaryColor);
        content.AddChild(text);

        Label badge = null;
        if (!string.IsNullOrWhiteSpace(entry.Badge))
        {
            badge = new Label
            {
                Text = entry.Badge,
                SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                // Metadata stays a single natural-width line. With wrapping enabled, ShrinkEnd
                // can collapse the label to one character and make every badge render vertically.
                AutowrapMode = TextServer.AutowrapMode.Off,
                ClipText = false,
                TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming,
                MouseFilter = MouseFilterEnum.Ignore
            };
            badge.AddThemeColorOverride("font_color", entry.BadgeColor ?? primaryColor);
            content.AddChild(badge);
        }

        RowState row = new(entry, parent, panel, expander);
        panel.GuiInput += input => OnRowGuiInput(row, input);
        panel.MouseEntered += () => SetRowHovered(row, true);
        panel.MouseExited += () => SetRowHovered(row, false);
        if (expander != null)
        {
            expander.Pressed += () => SetExpanded(row, !row.Expanded);
        }

        RefreshRowStyle(row);
        return row;
    }

    private RowState CreateSquadRow(HierarchyTreeItem entry, RowState parent, int depth)
    {
        SquadRowView panel = new()
        {
            CustomMinimumSize = new Vector2(0,
                entry.RowHeight > 0 ? entry.RowHeight : RowDefaultHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.SetIndent(depth * IndentWidth);
        panel.Configure(entry.SquadRow);

        RowState row = new(entry, parent, panel, null);
        panel.RowSelected += (_, _) => OnSquadRowSelected(row);
        panel.RowActivated += (_, key) => Activated?.Invoke(this, key);
        RefreshRowStyle(row);
        return row;
    }

    private void OnSquadRowSelected(RowState row)
    {
        if (!row.Entry.Selectable)
        {
            return;
        }

        bool controlPressed = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta);
        bool wasSelected = row.Selected;
        if (SelectMode == Tree.SelectModeEnum.Multi && controlPressed)
        {
            row.Selected = !row.Selected;
        }
        else
        {
            ClearSelectionInternal();
            row.Selected = true;
        }

        RefreshAllRowStyles();
        if (row.Selected != wasSelected || AllowReselect)
        {
            if (!_suppressSelectionSignals)
            {
                SelectionChanged?.Invoke(this, row.Entry.Key);
            }
        }
    }

    private void OnRowGuiInput(RowState row, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton mouseButton
            || mouseButton.ButtonIndex != MouseButton.Left
            || !mouseButton.Pressed)
        {
            return;
        }

        if (!row.Entry.Selectable)
        {
            return;
        }

        bool controlPressed = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta);
        bool wasSelected = row.Selected;
        if (SelectMode == Tree.SelectModeEnum.Multi && controlPressed)
        {
            row.Selected = !row.Selected;
        }
        else
        {
            ClearSelectionInternal();
            row.Selected = true;
        }

        RefreshAllRowStyles();
        if (mouseButton.DoubleClick)
        {
            Activated?.Invoke(this, row.Entry.Key);
        }

        if (row.Selected != wasSelected || AllowReselect)
        {
            SelectionChanged?.Invoke(this, row.Entry.Key);
        }
    }

    private void SetRowHovered(RowState row, bool hovered)
    {
        row.Hovered = hovered;
        RefreshRowStyle(row);
    }

    private void SetExpanded(RowState row, bool expanded)
    {
        if (row.Children == null)
        {
            return;
        }

        row.Expanded = expanded;
        row.Children.Visible = expanded;
        row.Expander.Text = expanded ? "⌄" : "›";
    }

    private bool ClearSelectionInternal()
    {
        bool changed = false;
        foreach (RowState row in _rows)
        {
            if (!row.Selected)
            {
                continue;
            }

            row.Selected = false;
            changed = true;
        }

        if (changed)
        {
            RefreshAllRowStyles();
        }

        return changed;
    }

    private void RefreshAllRowStyles()
    {
        foreach (RowState row in _rows)
        {
            RefreshRowStyle(row);
        }
    }

    private static void RefreshRowStyle(RowState row)
    {
        if (row.Panel is SquadRowView squadRow)
        {
            squadRow.SetSelected(row.Selected);
            squadRow.SetHovered(row.Hovered);
            return;
        }

        StyleBoxFlat style = OnlyWarStyle.GetListRowStyle(row.Selected || row.Hovered);
        style.ContentMarginLeft = 4;
        row.Panel.AddThemeStyleboxOverride("panel", style);
    }

    private Dictionary<string, bool> CaptureCollapsedStates()
    {
        return _rows.ToDictionary(row => row.Path, row => !row.Expanded);
    }

    private void ExpandSelectedAncestors()
    {
        foreach (RowState row in _rows.Where(row => row.Selected))
        {
            for (RowState ancestor = row.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                SetExpanded(ancestor, true);
            }
        }
    }

    private sealed class RowState
    {
        public RowState(
            HierarchyTreeItem entry,
            RowState parent,
            PanelContainer panel,
            Button expander)
        {
            Entry = entry;
            Parent = parent;
            Panel = panel;
            Expander = expander;
            Selected = entry.IsSelected;
            Expanded = entry.Children.Count == 0;
        }

        public HierarchyTreeItem Entry { get; }
        public RowState Parent { get; }
        public PanelContainer Panel { get; }
        public Button Expander { get; }
        public VBoxContainer Children { get; set; }
        public string Path { get; set; }
        public bool Selected { get; set; }
        public bool Hovered { get; set; }
        public bool Expanded { get; set; }
    }
}
