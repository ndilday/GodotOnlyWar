using Godot;
using System;
using System.Collections.Generic;

public partial class FleetScreenView : MainScreenView
{
    public event EventHandler<ValueTuple<int, int>> SquadDroppedOnShip;
    public event EventHandler<ValueTuple<int, int, int>> UnitDroppedOnShip;

    private FleetTransferTree _fleetTree;

    public override void _Ready()
    {
        base._Ready();
        _fleetTree = GetNode<FleetTransferTree>("FleetPanel/Tree");
        _fleetTree.CanTransferSquadToShip = (squadId, shipId) => CanTransferSquadToShip?.Invoke(squadId, shipId) == true;
        _fleetTree.TransferSquadToShip = (squadId, shipId) => SquadDroppedOnShip?.Invoke(this, new ValueTuple<int, int>(squadId, shipId));
        _fleetTree.CanTransferUnitToShip = (unitId, sourceShipId, destinationShipId) =>
            CanTransferUnitToShip?.Invoke(unitId, sourceShipId, destinationShipId) == true;
        _fleetTree.TransferUnitToShip = (unitId, sourceShipId, destinationShipId) =>
            UnitDroppedOnShip?.Invoke(this, new ValueTuple<int, int, int>(unitId, sourceShipId, destinationShipId));
    }

    public Func<int, int, bool> CanTransferSquadToShip { get; set; }
    public Func<int, int, int, bool> CanTransferUnitToShip { get; set; }

    public void PopulateFleetTree(IReadOnlyList<TreeNode> entries)
    {
        Dictionary<string, bool> collapsedByKey = CaptureCollapsedStates();

        _fleetTree.Clear();
        TreeItem root = _fleetTree.CreateItem();
        _fleetTree.HideRoot = true;
        AddTreeChildren(root, entries, collapsedByKey, "");
    }

    public bool FocusSquad(int squadId)
    {
        TreeItem root = _fleetTree.GetRoot();
        TreeItem squadItem = FindTreeItem(root?.GetFirstChild(), $"Squad:{squadId}");
        if (squadItem == null)
        {
            return false;
        }

        _fleetTree.DeselectAll();
        for (TreeItem ancestor = squadItem.GetParent(); ancestor != null && ancestor != root; ancestor = ancestor.GetParent())
        {
            ancestor.Collapsed = false;
        }

        squadItem.Select(0);
        return true;
    }

    private void AddTreeChildren(TreeItem parentItem, IReadOnlyList<TreeNode> nodes, IReadOnlyDictionary<string, bool> collapsedByKey, string parentPath)
    {
        foreach (TreeNode childNode in nodes)
        {
            TreeItem childItem = _fleetTree.CreateItem(parentItem);
            childItem.SetText(0, childNode.Name);
            string key = $"{childNode.Kind}:{childNode.Id}";
            childItem.SetMetadata(0, Variant.From(key));
            childItem.SetSelectable(0, childNode.Selectable);
            // The same company (Unit) can appear under multiple ships, so its "{Kind}:{Id}"
            // key is not unique on its own. Track collapse state by full path from the root
            // so each ship's copy of a company keeps its own expanded/collapsed state.
            string path = $"{parentPath}/{key}";
            if (childNode.Children?.Count > 0)
            {
                AddTreeChildren(childItem, childNode.Children, collapsedByKey, path);
                if (collapsedByKey.TryGetValue(path, out bool wasCollapsed))
                {
                    childItem.Collapsed = wasCollapsed;
                }
            }
        }
    }

    private static TreeItem FindTreeItem(TreeItem item, string metadata)
    {
        while (item != null)
        {
            if (item.GetMetadata(0).AsString() == metadata)
            {
                return item;
            }

            TreeItem descendant = FindTreeItem(item.GetFirstChild(), metadata);
            if (descendant != null)
            {
                return descendant;
            }

            item = item.GetNext();
        }

        return null;
    }

    private Dictionary<string, bool> CaptureCollapsedStates()
    {
        Dictionary<string, bool> collapsedByKey = [];
        TreeItem root = _fleetTree.GetRoot();
        if (root == null)
        {
            return collapsedByKey;
        }

        CaptureCollapsedStates(root.GetFirstChild(), "", collapsedByKey);
        return collapsedByKey;
    }

    private static void CaptureCollapsedStates(TreeItem item, string parentPath, Dictionary<string, bool> collapsedByKey)
    {
        while (item != null)
        {
            string key = item.GetMetadata(0).AsString();
            if (!string.IsNullOrEmpty(key))
            {
                string path = $"{parentPath}/{key}";
                collapsedByKey[path] = item.Collapsed;
                CaptureCollapsedStates(item.GetFirstChild(), path, collapsedByKey);
            }

            item = item.GetNext();
        }
    }
}
