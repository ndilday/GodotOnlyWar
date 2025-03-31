using Godot;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;

public partial class RegionScreenView : DialogView
{
    private VBoxContainer _regionDetailsVBox;
    private Tree _squadTree;
    private VBoxContainer _orderDetailsVBox;
    private Button _unassignButton;
    private Button _openOrdersButton;
    private Button _assignToExistingButton;
    private Button _copyOrdersButton;
    private Button _pasteOrdersButton;

    private TacticalRegionController _centerRegionController;
    private TacticalRegionController _northRegionController;
    private TacticalRegionController _northeastRegionController;
    private TacticalRegionController _southeastRegionController;
    private TacticalRegionController _southRegionController;
    private TacticalRegionController _southwestRegionController;
    private TacticalRegionController _northwestRegionController;

    // Signals for Controller
    public event EventHandler<int> SquadSelected;
    public event EventHandler<int> SquadDoubleClicked;
    public event EventHandler<Region> AdjacentRegionClicked; // For clicking on mini-map regions
    public event EventHandler UnassignButtonClicked;
    public event EventHandler OpenOrdersButtonClicked;
    public event EventHandler AssignToExistingButtonClicked;
    public event EventHandler CopyOrdersButtonClicked;
    public event EventHandler PasteOrdersButtonClicked;

    public override void _Ready()
    {
        base._Ready(); // Call base class _Ready to connect CloseButton

        _regionDetailsVBox = GetNode<VBoxContainer>("DataPanel/VBoxContainer");
        _squadTree = GetNode<Tree>("SquadTreePanel/ScrollContainer/Tree");
        _orderDetailsVBox = GetNode<VBoxContainer>("OrdersPanel/VBoxContainer");

        _unassignButton = GetNode<Button>("OrdersPanel/ButtonVBox/UnassignButton");
        _openOrdersButton = GetNode<Button>("OrdersPanel/ButtonVBox/OpenOrdersButton");
        _assignToExistingButton = GetNode<Button>("OrdersPanel/ButtonVBox/AssignToExistingButton");
        _copyOrdersButton = GetNode<Button>("OrdersPanel/ButtonVBox/CopyOrdersButton");
        _pasteOrdersButton = GetNode<Button>("OrdersPanel/ButtonVBox/PasteOrdersButton");

        _centerRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionCenter");
        _northRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorth");
        _northeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNortheast");
        _southeastRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSoutheast");
        _southRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouth");
        _southwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionSouthwest");
        _northwestRegionController = GetNode<TacticalRegionController>("RegionPanel/TacticalRegionNorthwest");

        // Connect internal UI signals to handlers that emit public signals
        _squadTree.ItemSelected += OnSquadTreeItemSelected;
        _squadTree.ItemActivated += OnSquadTreeItemActivated; // Double click

        _unassignButton.Pressed += () => UnassignButtonClicked?.Invoke(this, EventArgs.Empty);
        _openOrdersButton.Pressed += () => OpenOrdersButtonClicked?.Invoke(this, EventArgs.Empty);
        _assignToExistingButton.Pressed += () => AssignToExistingButtonClicked?.Invoke(this, EventArgs.Empty); // You'll need logic for this
        _copyOrdersButton.Pressed += () => CopyOrdersButtonClicked?.Invoke(this, EventArgs.Empty);
        _pasteOrdersButton.Pressed += () => PasteOrdersButtonClicked?.Invoke(this, EventArgs.Empty);

        // Connect adjacent region clicks (assuming TacticalRegionController has a signal like 'TacticalRegionPressed')
        ConnectAdjacentRegionSignal(_northRegionController);
        ConnectAdjacentRegionSignal(_northeastRegionController);
        ConnectAdjacentRegionSignal(_southeastRegionController);
        ConnectAdjacentRegionSignal(_southRegionController);
        ConnectAdjacentRegionSignal(_southwestRegionController);
        ConnectAdjacentRegionSignal(_northwestRegionController);
    }

    private void ConnectAdjacentRegionSignal(TacticalRegionController controller)
    {
        if (controller != null)
        {
            // Assuming TacticalRegionController has a signal like this
            // You might need to adjust the signal name if it's different
            controller.TacticalRegionPressed += (sender, region) => AdjacentRegionClicked?.Invoke(this, region);
        }
    }

    // --- UI Population Methods ---

    public void PopulateRegionDetails(IReadOnlyList<Tuple<string, string>> details)
    {
        ClearContainer(_regionDetailsVBox);
        foreach (var detail in details)
        {
            AddDetailLine(_regionDetailsVBox, detail.Item1, detail.Item2);
        }
    }

    public void PopulateSquadList(IReadOnlyList<TreeNode> squadNodes)
    {
        _squadTree.Clear();
        TreeItem root = _squadTree.CreateItem();
        _squadTree.HideRoot = true;
        AddTreeChildren(_squadTree, root, squadNodes, 0); // Assuming level 0 for top-level units/squads
    }

    public void PopulateOrderDetails(IReadOnlyList<Tuple<string, string>> details, bool hasOrder)
    {
        ClearContainer(_orderDetailsVBox);
        foreach (var detail in details)
        {
            AddDetailLine(_orderDetailsVBox, detail.Item1, detail.Item2);
        }
        // Enable/disable buttons based on whether an order exists for the selected squad
        _unassignButton.Disabled = !hasOrder;
        _openOrdersButton.Disabled = false; // Can always open to assign or edit
        _assignToExistingButton.Disabled = true; // Needs implementation
        _copyOrdersButton.Disabled = !hasOrder;
    }

    public void PopulateAdjacentRegions(Region centerRegion, Dictionary<string, Region> adjacentRegions)
    {
        _centerRegionController.Populate(centerRegion);

        // Helper to populate or hide adjacent regions
        void SetupAdjacentRegion(TacticalRegionController controller, string direction)
        {
            if (adjacentRegions.TryGetValue(direction, out Region region))
            {
                controller.Populate(region);
                controller.Visible = true;
            }
            else
            {
                controller.Visible = false;
            }
        }

        SetupAdjacentRegion(_northRegionController, "N");
        SetupAdjacentRegion(_northeastRegionController, "NE");
        SetupAdjacentRegion(_southeastRegionController, "SE");
        SetupAdjacentRegion(_southRegionController, "S");
        SetupAdjacentRegion(_southwestRegionController, "SW");
        SetupAdjacentRegion(_northwestRegionController, "NW");
    }

    public void SetPasteOrdersButtonDisabled(bool disabled)
    {
        _pasteOrdersButton.Disabled = disabled;
    }

    public void SetSelectedSquad(int squadId)
    {
        // Find the tree item corresponding to the squad ID and select it
        TreeItem item = FindTreeItemById(_squadTree.GetRoot(), squadId, 1); // Start search at level 1 if root is hidden
        if (item != null)
        {
            _squadTree.SetSelected(item, 0);
            // Ensure visible? _squadTree.ScrollToItem(item);
        }
    }


    // --- Signal Handlers ---

    private void OnSquadTreeItemSelected()
    {
        TreeItem selected = _squadTree.GetSelected();
        if (selected != null)
        {
            Vector2I meta = selected.GetMetadata(0).As<Vector2I>();
            // Only emit if it's a squad (assuming level 1 or deeper indicates a squad/unit)
            if (meta.X >= 1) // Adjust level check as needed based on your tree structure
            {
                SquadSelected?.Invoke(this, meta.Y); // Send the ID (meta.Y)
            }
            else // deselect order details if unit is selected?
            {
                ClearContainer(_orderDetailsVBox);
                _unassignButton.Disabled = true;
                _openOrdersButton.Disabled = true;
                _assignToExistingButton.Disabled = true;
                _copyOrdersButton.Disabled = true;
                // _pasteOrdersButton state depends on clipboard
            }
        }
    }

    private void OnSquadTreeItemActivated() // Double Click
    {
        TreeItem selected = _squadTree.GetSelected();
        if (selected != null)
        {
            Vector2I meta = selected.GetMetadata(0).As<Vector2I>();
            // Only emit if it's a squad (assuming level 1 or deeper)
            if (meta.X >= 1) // Adjust level check as needed
            {
                SquadDoubleClicked?.Invoke(this, meta.Y); // Send the ID (meta.Y)
            }
        }
    }

    // --- Helper Methods ---

    private void ClearContainer(Container container)
    {
        foreach (Node child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void AddDetailLine(VBoxContainer container, string label, string value)
    {
        Panel linePanel = new Panel { CustomMinimumSize = new Vector2(0, 20) };
        Label labelNode = new Label { Text = label, HorizontalAlignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Label valueNode = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        linePanel.AddChild(labelNode);
        linePanel.AddChild(valueNode);
        container.AddChild(linePanel);
    }

    private void AddTreeChildren(Tree tree, TreeItem parentItem, IReadOnlyList<TreeNode> nodes, int level)
    {
        if (nodes == null) return;
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

    private TreeItem FindTreeItemById(TreeItem parent, int id, int targetLevel)
    {
        if (parent == null) return null;

        TreeItem current = parent.GetFirstChild();
        while (current != null)
        {
            Vector2I meta = current.GetMetadata(0).As<Vector2I>();
            if (meta.X == targetLevel && meta.Y == id)
            {
                return current;
            }

            // Recursively search children if this item is not the target level
            if (meta.X < targetLevel)
            {
                TreeItem found = FindTreeItemById(current, id, targetLevel);
                if (found != null)
                {
                    return found;
                }
            }
            current = current.GetNext();
        }
        return null;
    }
}
