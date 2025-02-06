using Godot;
using System;
using System.Collections.Generic;

public partial class PlanetTacticalScreenView : DialogView
{
	private Button _closeButton;
    private Tree _squadTree;
    private VBoxContainer _regionDetailsVBox;

    public event EventHandler<Vector2I> SquadTreeItemDoubleClicked;

    public override void _Ready()
	{
        base._Ready();
        _squadTree = GetNode<Tree>("RegionSquadPanel/Tree");
        _squadTree.ItemActivated += OnItemActivated;
        _regionDetailsVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
	}

    private void OnItemActivated()
    {
        TreeItem item = _squadTree.GetSelected();
        Vector2I meta = item.GetMetadata(0).As<Vector2I>();
        SquadTreeItemDoubleClicked.Invoke(item, meta);
    }

    public void PopulateRegionSquadTree(IReadOnlyList<TreeNode> entries)
    {
        _squadTree.Clear();
        TreeItem root = _squadTree.CreateItem();
        _squadTree.HideRoot = true;
        AddTreeChildren(_squadTree, root, entries, 0);
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

    public void ClearRegionData()
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
    }

    public void PopulateRegionData(IReadOnlyList<Tuple<string, string>> stringPairs)
    {
        ClearRegionData();
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
