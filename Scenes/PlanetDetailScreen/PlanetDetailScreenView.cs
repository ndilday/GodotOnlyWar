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
	private Button _landingButton;
	private Tree _fleetTree;
	private Tree _regionTree;
	private VBoxContainer _planetDataVBox;
	
	public event EventHandler CloseButtonPressed;
	public event EventHandler LandingButtonPressed;
	public event EventHandler<int> SquadDoubleClicked;
	public event EventHandler<Vector2I> FleetTreeItemClicked;
	public event EventHandler FleetTreeDeselected;
	public event EventHandler<Vector2I> RegionTreeItemClicked;
	public event EventHandler RegionTreeDeselected;

	public override void _Ready()
	{
		_closeButton = GetNode<Button>("CloseButton");
		_closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
		_landingButton = GetNode<Button>("ButtonPanel/VBoxContainer/LandingButton");
		_landingButton.Pressed += () => LandingButtonPressed?.Invoke(this, EventArgs.Empty);
		_planetDataVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
		_fleetTree = GetNode<Tree>("ShipListPanel/Tree");
		_fleetTree.ItemSelected += OnFleetTreeItemSelected;
		_fleetTree.NothingSelected += () => FleetTreeDeselected(this, EventArgs.Empty);
		_regionTree = GetNode<Tree>("RegionListPanel/Tree");
		_regionTree.ItemSelected += OnRegionTreeItemSelected;
		_regionTree.NothingSelected += () => RegionTreeDeselected(this, EventArgs.Empty);
	}

	public void PopulateShipTree(IReadOnlyList<TreeNode> entries)
	{
		_fleetTree.Clear();
		TreeItem root = _fleetTree.CreateItem();
		_fleetTree.HideRoot = true;
		AddTreeChildren(_fleetTree, root, entries, 0);
	}

	public void PopulateRegionTree(IReadOnlyList<TreeNode> entries)
	{
		_regionTree.Clear();
		TreeItem root = _regionTree.CreateItem();
		_regionTree.HideRoot = true;
		AddTreeChildren(_regionTree, root, entries, 0);
	}

	public void EnableLandingButton(bool enable, string text)
	{
		_landingButton.Text = text;
		_landingButton.Disabled = !enable;
	}

	private void AddTreeChildren(Tree tree, TreeItem parentItem, IReadOnlyList<TreeNode> nodes, int level)
	{
		foreach (TreeNode childNode in nodes)
		{
			TreeItem childItem = tree.CreateItem(parentItem);
			childItem.SetText(0, childNode.Name);
			Vector2I vector = new Vector2I(level, childNode.Id);
			Variant meta = Variant.From(vector);
			childItem.SetMetadata(0, meta);
			if (childNode.Children?.Count > 0)
			{
				AddTreeChildren(tree, childItem, childNode.Children, level+1);
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
		lineLabel.AnchorLeft = 0;
		lineLabel.HorizontalAlignment = HorizontalAlignment.Left;
		lineLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		linePanel.AddChild(lineLabel);
		Label lineValue = new Label();
		lineValue.Text = value;
		lineValue.AnchorRight = 1;
		lineValue.HorizontalAlignment = HorizontalAlignment.Right;
		lineValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		linePanel.AddChild(lineValue);
		_planetDataVBox.AddChild(linePanel);
	}

	private void OnRegionTreeItemSelected()
	{
		TreeItem item = _regionTree.GetSelected();
		Vector2I meta = item.GetMetadata(0).As<Vector2I>();
		RegionTreeItemClicked.Invoke(item, meta);
	}

	private void OnFleetTreeItemSelected()
	{
		TreeItem item = _fleetTree.GetSelected();
		Vector2I meta = item.GetMetadata(0).As<Vector2I>();
		FleetTreeItemClicked.Invoke(item, meta);
	}
}
