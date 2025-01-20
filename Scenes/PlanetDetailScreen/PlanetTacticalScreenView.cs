using Godot;
using System;
using System.Collections.Generic;

public partial class PlanetTacticalScreenView : Control
{
	private Button _closeButton;
    private Tree _regionTree;
    private VBoxContainer _regionDetailsVBox;

	public event EventHandler CloseButtonPressed;

	public override void _Ready()
	{
		_closeButton = GetNode<Button>("CloseButton");
		_closeButton.Pressed += () => CloseButtonPressed?.Invoke(this, EventArgs.Empty);
        _regionTree = GetNode<Tree>("RegionSquadPanel/Tree");
        _regionDetailsVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
	}

    public void PopulateRegionTree(IReadOnlyList<TreeNode> entries)
    {
        _regionTree.Clear();
        TreeItem root = _regionTree.CreateItem();
        _regionTree.HideRoot = true;
        AddTreeChildren(_regionTree, root, entries, 0);
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
                AddTreeChildren(tree, childItem, childNode.Children, level + 1);
            }
        }
    }

    public void PopulateRegionData(IReadOnlyList<Tuple<string, string>> stringPairs)
    {
        var existingLines = _regionDetailsVBox.GetChildren();
        if (existingLines != null)
        {
            foreach (var line in existingLines)
            {
                _regionDetailsVBox.RemoveChild(line);
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
        _regionDetailsVBox.AddChild(linePanel);
    }
}
