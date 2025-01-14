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
	private VBoxContainer _planetDataVBox;
	
	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_closeButton = GetNode<Button>("CloseButton");
		_closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
		_planetDataVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
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

	public void PopulatePlanetData(IReadOnlyList<Tuple<string, string>> stringPairs)
	{
		var existingLines = _planetDataVBox.GetChildren();
		if (existingLines != null)
		{
			foreach (var line in existingLines)
			{
				_planetDataVBox.RemoveChild(line);
				line.QueueFree();
			}
		}
		foreach (Tuple<string, string> line in stringPairs)
		{
			AddLine(line.Item1, line.Item2);
		}
	}

	private void AddLine(string label, string value)
	{
		Panel linePanel = new Panel();
		linePanel.SizeFlagsHorizontal = SizeFlags.Fill;
		linePanel.SizeFlagsVertical = SizeFlags.Fill;
		linePanel.CustomMinimumSize = new Vector2(0, 20);
		Label lineLabel = new Label();
		lineLabel.Text = label;
		lineLabel.HorizontalAlignment = HorizontalAlignment.Left;
		lineLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		linePanel.AddChild(lineLabel);
		Label lineValue = new Label();
		lineValue.Text = value;
		lineValue.HorizontalAlignment = HorizontalAlignment.Right;
		lineValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		linePanel.AddChild(lineValue);
		_planetDataVBox.AddChild(linePanel);
	}
}
