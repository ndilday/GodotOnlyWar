using Godot;
using System;
using System.Collections.Generic;

public class TreeNode
{
	public int Id;
	public string Name;
	public IReadOnlyList<TreeNode> Children;

	public TreeNode(int id, string name, IReadOnlyList<TreeNode> children)
    {
        Id = id;
        Name = name;
        Children = children;
    }
}

public partial class PlanetDetailScreenView : Control
{
	private Button _closeButton;
	private Tree _fleetTree;
	private Tree _regionTree;
	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_closeButton = GetNode<Button>("CloseButton");
		_closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
		_fleetTree = GetNode<Tree>("ShipListPanel/Tree");
		_regionTree = GetNode<Tree>("RegionListPanel/Tree");
	}

	public void PopulateShipTree(IReadOnlyList<TreeNode> entries)
	{
		_fleetTree.Clear();
        TreeItem root = _fleetTree.CreateItem();
        _fleetTree.HideRoot = true;
        foreach (TreeNode entry in entries)
		{
			// create a tree item from Item1, and children for the entries in Item2
			TreeItem parent = _fleetTree.CreateItem(root);
			parent.SetText(0, entry.Name);
			foreach(TreeNode childEntry in entry.Children)
			{
				TreeItem child = _fleetTree.CreateItem(parent);
				child.SetText(0, childEntry.Name);
			}
		}
	}

    public void PopulateRegionTree(IReadOnlyList<TreeNode> entries)
    {
        _regionTree.Clear();
        TreeItem root = _regionTree.CreateItem();
        _regionTree.HideRoot = true;
        foreach (TreeNode entry in entries)
        {
            // create a tree item from Item1, and children for the entries in Item2
            TreeItem parent = _regionTree.CreateItem(root);
            parent.SetText(0, entry.Name);
            foreach (TreeNode childEntry in entry.Children)
            {
                TreeItem child = _regionTree.CreateItem(parent);
                child.SetText(0, childEntry.Name);
				foreach(TreeNode grandChildEntry in  childEntry.Children)
				{
					TreeItem grandchild = _regionTree.CreateItem(child);
					grandchild.SetText(0, grandChildEntry.Name);
				}
            }
        }
    }
}
