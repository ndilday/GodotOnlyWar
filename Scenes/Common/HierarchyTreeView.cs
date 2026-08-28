using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared Tree behavior for hierarchical rosters and selection lists.
///
/// Screens provide presentation rows and receive stable string keys back. This keeps selection,
/// activation, expansion restoration, and TreeItem metadata conventions out of individual views.
/// Fleet-specific drag/drop remains outside this component.
/// </summary>
public partial class HierarchyTreeView : Tree
{
    public const int DefaultIconMaxWidth = 20;

    private bool _multiSelectionNotificationPending;
    private string _pendingMultiSelectionKey = "";
    private bool _pendingMultiSelectionSelected;
    private bool _suppressSelectionSignals;

    public int IconMaxWidth { get; set; } = DefaultIconMaxWidth;

    /// <summary>
    /// When enabled for a three-column tree, the icon occupies a compact leading column, the
    /// row text occupies the expanding middle column, and the badge occupies the trailing column.
    /// Existing two-column trees retain the original icon-and-text layout.
    /// </summary>
    public bool UseLeadingIconColumn { get; set; }

    public event EventHandler<string> SelectionChanged;
    public event EventHandler<string> Activated;

    public override void _Ready()
    {
        base._Ready();
        ItemSelected += OnTreeItemSelected;
        MultiSelected += OnTreeItemMultiSelected;
        ItemActivated += OnTreeItemActivated;
    }

    public void ConfigureColumns(
        int columnCount,
        int secondaryColumnMinimumWidth = 0,
        int trailingColumnMinimumWidth = 0,
        bool useLeadingIconColumn = false,
        int leadingColumnMinimumWidth = 0)
    {
        Columns = Math.Max(1, columnCount);
        UseLeadingIconColumn = useLeadingIconColumn && Columns > 2;
        SetColumnExpand(0, !UseLeadingIconColumn);
        if (UseLeadingIconColumn)
        {
            SetColumnCustomMinimumWidth(0, leadingColumnMinimumWidth);
        }

        if (Columns > 1)
        {
            SetColumnExpand(1, UseLeadingIconColumn);
            SetColumnCustomMinimumWidth(1, secondaryColumnMinimumWidth);
        }

        if (Columns > 2)
        {
            SetColumnExpand(2, false);
            SetColumnCustomMinimumWidth(2, trailingColumnMinimumWidth);
        }
    }

    /// <summary>
    /// Rebuilds the tree. Restoring preserved selection calls <c>TreeItem.Select</c>, which Godot
    /// reports through <see cref="SelectionChanged"/> exactly as if the user had clicked the row.
    /// Pass <paramref name="suppressSelectionSignals"/> when the caller is going to render off the
    /// new state itself, so that programmatic restoration does not trigger a redundant rebuild.
    /// </summary>
    public void Populate(
        IReadOnlyList<HierarchyTreeItem> entries,
        bool preserveUiState = true,
        bool suppressSelectionSignals = false)
    {
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
            Clear();
            HideRoot = true;
            TreeItem root = CreateItem();
            AddTreeItems(root, entries ?? Array.Empty<HierarchyTreeItem>(), "", collapsedByPath, selectedKeys);
            ExpandSelectedAncestors(root.GetFirstChild());
        }
        finally
        {
            _suppressSelectionSignals = previous;
        }
    }

    public IReadOnlyList<string> GetSelectedKeys()
    {
        List<string> keys = [];
        TreeItem root = GetRoot();
        if (root != null)
        {
            CollectSelectedKeys(root.GetFirstChild(), keys);
        }

        return keys;
    }

    public int GetVerticalScrollOffset()
    {
        VScrollBar scrollbar = GetChildren(includeInternal: true)
            .OfType<VScrollBar>()
            .FirstOrDefault();
        return scrollbar == null ? 0 : (int)scrollbar.Value;
    }

    public void SetVerticalScrollOffset(int offset)
    {
        VScrollBar scrollbar = GetChildren(includeInternal: true)
            .OfType<VScrollBar>()
            .FirstOrDefault();
        scrollbar?.SetDeferred(Godot.Range.PropertyName.Value, Math.Max(0, offset));
    }

    public IReadOnlyDictionary<string, bool> GetCollapsedStatesByKey()
    {
        Dictionary<string, bool> states = [];
        TreeItem root = GetRoot();
        if (root != null)
        {
            CaptureCollapsedStatesByKey(root.GetFirstChild(), states);
        }

        return states;
    }

    public void SetCollapsedStates(IReadOnlyDictionary<string, bool> states)
    {
        if (states == null || states.Count == 0)
        {
            return;
        }

        TreeItem root = GetRoot();
        if (root == null)
        {
            return;
        }

        foreach (TreeItem item in EnumerateTreeItems(root.GetFirstChild()))
        {
            if (states.TryGetValue(GetItemKey(item), out bool collapsed))
            {
                item.Collapsed = collapsed;
            }
        }
    }

    public void ClearSelection()
    {
        DeselectAll();
    }

    /// <summary>
    /// Selects the rows whose metadata keys are in <paramref name="keys"/>. By default this reports
    /// through <see cref="SelectionChanged"/>, which callers such as
    /// <c>ApothecariumScreenView.FocusSoldier</c> rely on to drive navigation. Pass
    /// <paramref name="suppressSelectionSignals"/> when the caller renders off the new state itself.
    /// </summary>
    public void SetSelectedKeys(IReadOnlyCollection<string> keys, bool suppressSelectionSignals = false)
    {
        HashSet<string> wanted = keys == null ? [] : [.. keys];

        bool previous = _suppressSelectionSignals;
        _suppressSelectionSignals = suppressSelectionSignals;
        try
        {
            DeselectAll();

            TreeItem root = GetRoot();
            if (root == null)
            {
                return;
            }

            foreach (TreeItem item in EnumerateTreeItems(root.GetFirstChild()))
            {
                string key = GetItemKey(item);
                if (string.IsNullOrEmpty(key) || !wanted.Contains(key))
                {
                    continue;
                }

                for (TreeItem ancestor = item.GetParent(); ancestor != null && ancestor != root; ancestor = ancestor.GetParent())
                {
                    ancestor.Collapsed = false;
                }

                item.Select(GetSelectionColumn());
            }
        }
        finally
        {
            _suppressSelectionSignals = previous;
        }
    }

    private void OnTreeItemSelected()
    {
        if (_suppressSelectionSignals)
        {
            return;
        }

        SelectionChanged?.Invoke(this, GetItemKey(GetSelected()));
    }

    // Godot emits the deselection half of a multi-select change before it clears the TreeItem's
    // selected flag. Defer the notification until the complete click transaction has settled.
    private void OnTreeItemMultiSelected(TreeItem item, long column, bool selected)
    {
        // Checked here rather than in the deferred handler: this runs synchronously inside the
        // suppressed Select() call, whereas the deferred emit runs after the flag has been restored.
        if (_suppressSelectionSignals)
        {
            return;
        }

        // A click on a parent in multi-select mode can emit one selected event for the parent
        // followed by deselected events for all of its children. Keep the selected item as the
        // transaction key, otherwise the final child deselection is reported to the caller.
        if (!_multiSelectionNotificationPending
            || (selected && !_pendingMultiSelectionSelected))
        {
            _pendingMultiSelectionKey = GetItemKey(item);
            _pendingMultiSelectionSelected = selected;
        }
        if (_multiSelectionNotificationPending)
        {
            return;
        }

        _multiSelectionNotificationPending = true;
        CallDeferred(MethodName.EmitSettledMultiSelectionChanged);
    }

    private void EmitSettledMultiSelectionChanged()
    {
        _multiSelectionNotificationPending = false;
        string key = _pendingMultiSelectionKey;
        _pendingMultiSelectionKey = "";
        _pendingMultiSelectionSelected = false;
        SelectionChanged?.Invoke(this, key);
    }

    private void OnTreeItemActivated()
    {
        Activated?.Invoke(this, GetItemKey(GetSelected()));
    }

    private void AddTreeItems(
        TreeItem parent,
        IReadOnlyList<HierarchyTreeItem> entries,
        string parentPath,
        IReadOnlyDictionary<string, bool> collapsedByPath,
        IReadOnlySet<string> selectedKeys)
    {
        foreach (HierarchyTreeItem entry in entries)
        {
            TreeItem item = CreateItem(parent);
            int textColumn = UseLeadingIconColumn ? 1 : 0;
            int badgeColumn = UseLeadingIconColumn ? 2 : 1;
            item.SetText(textColumn, entry.Text);
            item.SetMetadata(0, Variant.From(entry.Key));

            if (!string.IsNullOrWhiteSpace(entry.Tooltip))
            {
                item.SetTooltipText(textColumn, entry.Tooltip);
            }

            if (!string.IsNullOrWhiteSpace(entry.IconKey))
            {
                int iconColumn = UseLeadingIconColumn ? 0 : textColumn;
                item.SetIcon(iconColumn, IconAtlas.GetIcon(entry.IconKey));
                item.SetIconMaxWidth(iconColumn,
                    entry.IconMaxWidth > 0 ? entry.IconMaxWidth : IconMaxWidth);
            }

            if (Columns > 1)
            {
                item.SetText(badgeColumn, entry.Badge ?? "");
                item.SetTextAlignment(badgeColumn, HorizontalAlignment.Right);
                item.SetSelectable(badgeColumn, false);
                if (entry.BadgeColor.HasValue)
                {
                    item.SetCustomColor(badgeColumn, entry.BadgeColor.Value);
                }
            }

            if (UseLeadingIconColumn)
            {
                item.SetSelectable(0, false);
            }

            if (!entry.Selectable)
            {
                item.SetSelectable(textColumn, false);
                item.SetCustomColor(textColumn, OnlyWarStyle.MutedText);
            }

            if (entry.RowHeight > 0)
            {
                item.SetCustomMinimumHeight(entry.RowHeight);
            }

            if (entry.Selectable && (entry.IsSelected || selectedKeys.Contains(entry.Key)))
            {
                item.Select(textColumn);
            }

            if (entry.Children.Count == 0)
            {
                continue;
            }

            string path = $"{parentPath}/{entry.Key}";
            AddTreeItems(item, entry.Children, path, collapsedByPath, selectedKeys);
            if (collapsedByPath.TryGetValue(path, out bool wasCollapsed))
            {
                item.Collapsed = wasCollapsed;
            }
            else
            {
                item.Collapsed = entry.CollapsedByDefault;
            }
        }
    }

    private Dictionary<string, bool> CaptureCollapsedStates()
    {
        Dictionary<string, bool> collapsedByPath = [];
        TreeItem root = GetRoot();
        if (root == null)
        {
            return collapsedByPath;
        }

        CaptureCollapsedStates(root.GetFirstChild(), "", collapsedByPath);
        return collapsedByPath;
    }

    private static void CaptureCollapsedStates(
        TreeItem item,
        string parentPath,
        Dictionary<string, bool> collapsedByPath)
    {
        while (item != null)
        {
            string key = GetItemKey(item);
            if (!string.IsNullOrEmpty(key))
            {
                string path = $"{parentPath}/{key}";
                collapsedByPath[path] = item.Collapsed;
                CaptureCollapsedStates(item.GetFirstChild(), path, collapsedByPath);
            }

            item = item.GetNext();
        }
    }

    private static void CaptureCollapsedStatesByKey(
        TreeItem item,
        Dictionary<string, bool> states)
    {
        while (item != null)
        {
            string key = GetItemKey(item);
            if (!string.IsNullOrEmpty(key))
            {
                states[key] = item.Collapsed;
            }

            CaptureCollapsedStatesByKey(item.GetFirstChild(), states);
            item = item.GetNext();
        }
    }

    private static void CollectSelectedKeys(TreeItem item, List<string> keys)
    {
        while (item != null)
        {
            if (item.IsSelected(0) || item.IsSelected(1))
            {
                string key = GetItemKey(item);
                if (!string.IsNullOrEmpty(key))
                {
                    keys.Add(key);
                }
            }

            CollectSelectedKeys(item.GetFirstChild(), keys);
            item = item.GetNext();
        }
    }

    private static bool ExpandSelectedAncestors(TreeItem item)
    {
        bool containsSelection = false;
        while (item != null)
        {
            bool containsSelectedDescendant = ExpandSelectedAncestors(item.GetFirstChild());
            bool selected = item.IsSelected(0) || item.IsSelected(1);
            if (containsSelectedDescendant)
            {
                item.Collapsed = false;
            }

            containsSelection |= selected || containsSelectedDescendant;
            item = item.GetNext();
        }

        return containsSelection;
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

    private static string GetItemKey(TreeItem item)
    {
        return item == null ? "" : item.GetMetadata(0).AsString();
    }

    private int GetSelectionColumn() => UseLeadingIconColumn ? 1 : 0;
}
