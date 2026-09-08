using Godot;
using System.Collections.Generic;

public partial class DiplomacyScreenView : MainScreenView
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

    public void FocusRequest(int requestId)
    {
        if (_requestTree == null || requestId <= 0)
        {
            return;
        }

        TreeItem root = _requestTree.GetRoot();
        foreach (TreeItem item in EnumerateTreeItems(root?.GetFirstChild()))
        {
            if (item.GetMetadata(0).AsString() != requestId.ToString())
            {
                continue;
            }

            item.Select(0);
            return;
        }
    }

    private void AddTreeChildren(TreeItem parentItem, IReadOnlyList<TreeNode> nodes)
    {
        foreach (TreeNode childNode in nodes)
        {
            TreeItem childItem = _requestTree.CreateItem(parentItem);
            childItem.SetText(0, childNode.Name);
            childItem.SetMetadata(0, Variant.From(childNode.Id.ToString()));
            childItem.SetSelectable(0, childNode.Selectable);
            if (childNode.Children?.Count > 0)
            {
                AddTreeChildren(childItem, childNode.Children);
            }
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
}
