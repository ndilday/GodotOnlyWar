using Godot;
using System.Collections.Generic;

public partial class DiplomacyScreenView : DialogView
{
    private Tree _requestTree;

    public override void _Ready()
    {
        base._Ready();
        _requestTree = GetNode<Tree>("RequestPanel/Tree");
    }

    public void PopulateRequestTree(IReadOnlyList<TreeNode> entries)
    {
        _requestTree.Clear();
        TreeItem root = _requestTree.CreateItem();
        _requestTree.HideRoot = true;
        AddTreeChildren(root, entries);
    }

    private void AddTreeChildren(TreeItem parentItem, IReadOnlyList<TreeNode> nodes)
    {
        foreach (TreeNode childNode in nodes)
        {
            TreeItem childItem = _requestTree.CreateItem(parentItem);
            childItem.SetText(0, childNode.Name);
            childItem.SetSelectable(0, childNode.Selectable);
            if (childNode.Children?.Count > 0)
            {
                AddTreeChildren(childItem, childNode.Children);
            }
        }
    }
}
