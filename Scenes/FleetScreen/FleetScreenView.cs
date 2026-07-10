using Godot;
using System;
using System.Collections.Generic;

public partial class FleetScreenView : DialogView
{
    public event EventHandler<Tuple<int, int>> SquadDroppedOnShip;

    private FleetTransferTree _fleetTree;

    public override void _Ready()
    {
        base._Ready();
        _fleetTree = GetNode<FleetTransferTree>("FleetPanel/Tree");
        _fleetTree.CanTransferSquadToShip = (squadId, shipId) => CanTransferSquadToShip?.Invoke(squadId, shipId) == true;
        _fleetTree.TransferSquadToShip = (squadId, shipId) => SquadDroppedOnShip?.Invoke(this, new Tuple<int, int>(squadId, shipId));
    }

    public Func<int, int, bool> CanTransferSquadToShip { get; set; }

    public void PopulateFleetTree(IReadOnlyList<TreeNode> entries)
    {
        Dictionary<string, bool> collapsedByKey = CaptureCollapsedStates();

        _fleetTree.Clear();
        TreeItem root = _fleetTree.CreateItem();
        _fleetTree.HideRoot = true;
        AddTreeChildren(root, entries, collapsedByKey);
    }

    private void AddTreeChildren(TreeItem parentItem, IReadOnlyList<TreeNode> nodes, IReadOnlyDictionary<string, bool> collapsedByKey)
    {
        foreach (TreeNode childNode in nodes)
        {
            TreeItem childItem = _fleetTree.CreateItem(parentItem);
            childItem.SetText(0, childNode.Name);
            string key = $"{childNode.Kind}:{childNode.Id}";
            childItem.SetMetadata(0, Variant.From(key));
            childItem.SetSelectable(0, childNode.Selectable);
            if (childNode.Children?.Count > 0)
            {
                AddTreeChildren(childItem, childNode.Children, collapsedByKey);
                if (collapsedByKey.TryGetValue(key, out bool wasCollapsed))
                {
                    childItem.Collapsed = wasCollapsed;
                }
            }
        }
    }

    private Dictionary<string, bool> CaptureCollapsedStates()
    {
        Dictionary<string, bool> collapsedByKey = [];
        TreeItem root = _fleetTree.GetRoot();
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
}
