using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

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

    public int IconMaxWidth { get; set; } = DefaultIconMaxWidth;

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
        int trailingColumnMinimumWidth = 0)
    {
        Columns = Math.Max(1, columnCount);
        SetColumnExpand(0, true);

        if (Columns > 1)
        {
            SetColumnExpand(1, false);
            SetColumnCustomMinimumWidth(1, secondaryColumnMinimumWidth);
        }

        if (Columns > 2)
        {
            SetColumnExpand(2, false);
            SetColumnCustomMinimumWidth(2, trailingColumnMinimumWidth);
        }
    }

    public void Populate(IReadOnlyList<HierarchyTreeItem> entries, bool preserveUiState = true)
    {
        Dictionary<string, bool> collapsedByPath = preserveUiState
            ? CaptureCollapsedStates()
            : [];
        HashSet<string> selectedKeys = preserveUiState
            ? [.. GetSelectedKeys()]
            : [];

        Clear();
        HideRoot = true;
        TreeItem root = CreateItem();
        AddTreeItems(root, entries ?? Array.Empty<HierarchyTreeItem>(), "", collapsedByPath, selectedKeys);
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

    public void ClearSelection()
    {
        DeselectAll();
    }

    public void SetSelectedKeys(IReadOnlyCollection<string> keys)
    {
        HashSet<string> wanted = keys == null ? [] : [.. keys];
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

            item.Select(0);
        }
    }

    private void OnTreeItemSelected()
    {
        SelectionChanged?.Invoke(this, GetItemKey(GetSelected()));
    }

    // Godot emits the deselection half of a multi-select change before it clears the TreeItem's
    // selected flag. Defer the notification until the complete click transaction has settled.
    private void OnTreeItemMultiSelected(TreeItem item, long column, bool selected)
    {
        _pendingMultiSelectionKey = GetItemKey(item);
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
            item.SetText(0, entry.Text);
            item.SetMetadata(0, Variant.From(entry.Key));

            if (!string.IsNullOrWhiteSpace(entry.Tooltip))
            {
                item.SetTooltipText(0, entry.Tooltip);
            }

            if (!string.IsNullOrWhiteSpace(entry.IconKey))
            {
                item.SetIcon(0, IconAtlas.GetIcon(entry.IconKey));
                item.SetIconMaxWidth(0, entry.IconMaxWidth > 0 ? entry.IconMaxWidth : IconMaxWidth);
            }

            if (Columns > 1)
            {
                item.SetText(1, entry.Badge ?? "");
                item.SetTextAlignment(1, HorizontalAlignment.Right);
                item.SetSelectable(1, false);
                if (entry.BadgeColor.HasValue)
                {
                    item.SetCustomColor(1, entry.BadgeColor.Value);
                }
            }

            if (!entry.Selectable)
            {
                item.SetSelectable(0, false);
                item.SetCustomColor(0, OnlyWarStyle.MutedText);
            }

            if (entry.RowHeight > 0)
            {
                item.SetCustomMinimumHeight(entry.RowHeight);
            }

            if (entry.Selectable && (entry.IsSelected || selectedKeys.Contains(entry.Key)))
            {
                item.Select(0);
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
}
