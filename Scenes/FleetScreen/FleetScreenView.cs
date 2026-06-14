using Godot;
using System.Collections.Generic;

public partial class FleetScreenView : DialogView
{
    private Tree _fleetTree;

    public override void _Ready()
    {
        base._Ready();
        _fleetTree = GetNode<Tree>("FleetPanel/Tree");
    }

    public void PopulateFleetTree(IReadOnlyList<TreeNode> entries)
    {
        _fleetTree.Clear();
        TreeItem root = _fleetTree.CreateItem();
        _fleetTree.HideRoot = true;
        AddTreeChildren(root, entries);
    }

    private void AddTreeChildren(TreeItem parentItem, IReadOnlyList<TreeNode> nodes)
    {
        foreach (TreeNode childNode in nodes)
        {
            TreeItem childItem = _fleetTree.CreateItem(parentItem);
            childItem.SetText(0, childNode.Name);
            childItem.SetSelectable(0, childNode.Selectable);
            if (childNode.Children?.Count > 0)
            {
                AddTreeChildren(childItem, childNode.Children);
            }
        }
    }
}
