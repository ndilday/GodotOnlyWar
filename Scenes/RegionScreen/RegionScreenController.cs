using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RegionScreenController : DialogController
{
    private RegionScreenView _view;
    private Region _currentRegion;
    private Squad _selectedSquad;
    private Order _copiedOrder; // For copy/paste functionality
    private OrderDialogController _orderDialog;

    // Signals to communicate outside the screen
    public event EventHandler<Squad> SquadDoubleClicked;
    public event EventHandler<Region> AdjacentRegionChangeRequested; // Signal when an adjacent region is clicked

    public override void _Ready()
    {
        base._Ready(); // Sets up CloseButton handling from DialogController
        _view = GetNode<RegionScreenView>("DialogView");
        _orderDialog = GetNode<OrderDialogController>("DialogView/OrderDialogController");

        // Connect view signals to controller methods
        _view.SquadSelected += OnSquadSelected;
        _view.SquadDoubleClicked += OnSquadDoubleClicked; // Renamed from OnSquadTreeItemActivated
        _view.AdjacentRegionClicked += OnAdjacentRegionClicked;
        _view.UnassignButtonClicked += OnUnassignButtonClicked;
        _view.OpenOrdersButtonClicked += OnOpenOrdersButtonClicked;
        _view.AssignToExistingButtonClicked += OnAssignToExistingButtonClicked;
        _view.CopyOrdersButtonClicked += OnCopyOrdersButtonClicked;
        _view.PasteOrdersButtonClicked += OnPasteOrdersButtonClicked;

        _orderDialog.OrdersConfirmed += OnOrdersConfirmed;

        _view.SetPasteOrdersButtonDisabled(_copiedOrder == null); // Initial state
    }

    public override void _ExitTree()
    {
        // Disconnect signals if the view exists
        if (GodotObject.IsInstanceValid(_view))
        {
            _view.SquadSelected -= OnSquadSelected;
            _view.SquadDoubleClicked -= OnSquadDoubleClicked;
            _view.AdjacentRegionClicked -= OnAdjacentRegionClicked;
            _view.UnassignButtonClicked -= OnUnassignButtonClicked;
            _view.OpenOrdersButtonClicked -= OnOpenOrdersButtonClicked;
            _view.AssignToExistingButtonClicked -= OnAssignToExistingButtonClicked;
            _view.CopyOrdersButtonClicked -= OnCopyOrdersButtonClicked;
            _view.PasteOrdersButtonClicked -= OnPasteOrdersButtonClicked;
        }
        if (GodotObject.IsInstanceValid(_orderDialog))
        {
            _orderDialog.OrdersConfirmed -= OnOrdersConfirmed;
        }
    }

    public void DisplayRegion(Region region)
    {
        _currentRegion = region;
        _selectedSquad = null; // Reset selection when showing a new region
        _copiedOrder = null; // Reset copied order
        _view.SetPasteOrdersButtonDisabled(true);

        PopulateRegionDetails();
        PopulateSquadList();
        PopulateAdjacentRegions();
        // Clear order details initially, will be populated when a squad is selected
        _view.PopulateOrderDetails(new List<Tuple<string, string>>(), false);
        _view.SetSelectedSquad(-1); // Deselect squad in the view
    }

    private void PopulateRegionDetails()
    {
        if (_currentRegion == null) return;

        List<Tuple<string, string>> details = new List<Tuple<string, string>>
        {
            new Tuple<string, string>("Region Name", _currentRegion.Name),
            new Tuple<string, string>("Planet", _currentRegion.Planet.Name),
            new Tuple<string, string>("Coordinates", $"({_currentRegion.Coordinates.Item1}, {_currentRegion.Coordinates.Item2})")
            // Add more details as needed (e.g., Terrain Type, Intelligence Level)
        };

        // Faction Presence & Defenses
        RegionFaction playerFaction = GetPlayerRegionFaction();
        RegionFaction defaultFaction = _currentRegion.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        RegionFaction enemyFaction = _currentRegion.RegionFactionMap.Values.FirstOrDefault(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);

        long civilianPop = (defaultFaction?.Population ?? 0) + (playerFaction?.Population ?? 0);
        if (enemyFaction != null && !enemyFaction.IsPublic)
        {
            civilianPop += enemyFaction.Population; // Add hidden enemy pop to civilian count
        }
        details.Add(new Tuple<string, string>("Civilian Population", civilianPop > 0 ? civilianPop.ToString("N0") : "None"));


        if (enemyFaction != null)
        {
            details.Add(new Tuple<string, string>("Enemy Faction", enemyFaction.PlanetFaction.Faction.Name + (enemyFaction.IsPublic ? "" : " (Hidden)")));
            if (enemyFaction.IsPublic)
            {
                details.Add(new Tuple<string, string>("Enemy Population", enemyFaction.GetPopulationDescription())); // Uses the extension method
                details.Add(new Tuple<string, string>("Enemy Garrison", enemyFaction.Garrison.ToString("N0")));
                details.Add(new Tuple<string, string>("Enemy Entrenchment", enemyFaction.Entrenchment.ToString()));
                details.Add(new Tuple<string, string>("Enemy Detection", enemyFaction.Detection.ToString()));
                details.Add(new Tuple<string, string>("Enemy Anti-Air", enemyFaction.AntiAir.ToString()));
            }
        }
        else
        {
            details.Add(new Tuple<string, string>("Enemy Presence", "None Detected"));
        }


        _view.PopulateRegionDetails(details);
    }

    private void PopulateSquadList()
    {
        if (_currentRegion == null)
        {
            _view.PopulateSquadList(new List<TreeNode>()); // Clear the view if no region
            return;
        }

        List<TreeNode> topLevelNodes = new List<TreeNode>();
        RegionFaction playerFaction = GetPlayerRegionFaction();
        List<Squad> unassignedSquads = new List<Squad>();
        Dictionary<Order, List<Squad>> squadsByOrder = new Dictionary<Order, List<Squad>>();

        // group squads by orders
        if (playerFaction.LandedSquads.Count > 0)
        {
            foreach (Squad squad in playerFaction.LandedSquads.OrderBy(s => s.Id)) // Order squads by SquadId, for now
            {
                if (squad.Members.Count == 0) continue; // Skip empty squads

                if (squad.CurrentOrders == null)
                {
                    unassignedSquads.Add(squad);
                }
                else
                {
                    if (!squadsByOrder.ContainsKey(squad.CurrentOrders))
                    {
                        squadsByOrder[squad.CurrentOrders] = new List<Squad>();
                    }
                    squadsByOrder[squad.CurrentOrders].Add(squad);
                }
            }
        }

        // create nodes for assigned squads
        foreach (var orderGroup in squadsByOrder.OrderBy(kvp => kvp.Key.Mission.MissionType.ToString())) 
        {
            Order order = orderGroup.Key;
            List<TreeNode> squadNodes = new List<TreeNode>();

            foreach (Squad squad in orderGroup.Value)
            {
                string squadDisplayName = $"{squad.Name}, {squad.ParentUnit?.Name ?? "Unknown Unit"}";
                squadNodes.Add(new TreeNode(squad.Id, squadDisplayName, null));
            }

            if (squadNodes.Count > 0)
            {
                string orderName = $"Order: {order.Mission.MissionType} in {order.Mission.RegionFaction.Region.Name}";
                topLevelNodes.Add(new TreeNode(order.Id, orderName, squadNodes));
            }
        }

        // create node for unassigned squads
        if (unassignedSquads.Count > 0)
        {
            List<TreeNode> unassignedSquadNodes = new List<TreeNode>();
            foreach (Squad squad in unassignedSquads)
            {
                string squadDisplayName = $"{squad.Name}, {squad.ParentUnit?.Name ?? "Unknown Unit"}";
                // Squad nodes are level 1, use squad ID
                unassignedSquadNodes.Add(new TreeNode(squad.Id, squadDisplayName, null));
            }

            // "Unassigned" node is level 0, use a special ID like -1
            topLevelNodes.Add(new TreeNode(-1, "Unassigned", unassignedSquadNodes));
        }

        // 4. Update the view
        _view.PopulateSquadList(topLevelNodes);
    }

    private void PopulateOrderDetails()
    {
        if (_selectedSquad == null)
        {
            _view.PopulateOrderDetails(new List<Tuple<string, string>>(), false);
            return;
        }

        List<Tuple<string, string>> details = new List<Tuple<string, string>>();
        bool hasOrder = _selectedSquad.CurrentOrders != null;

        if (hasOrder)
        {
            Order order = _selectedSquad.CurrentOrders;
            details.Add(new Tuple<string, string>("Mission Type", order.Mission.MissionType.ToString()));
            details.Add(new Tuple<string, string>("Target Region", order.Mission.RegionFaction.Region.Name));
            // Potentially add target faction if relevant for the mission
            if (order.Mission.RegionFaction.PlanetFaction != null)
                details.Add(new Tuple<string, string>("Target Faction", order.Mission.RegionFaction.PlanetFaction.Faction.Name));
            details.Add(new Tuple<string, string>("Aggression", order.LevelOfAggression.ToString()));
            details.Add(new Tuple<string, string>("Squads Assigned", order.AssignedSquads.Count.ToString())); // Show how many squads are on this order
            // Add more details like IsQuiet, IsActivelyEngaging if needed
        }
        else
        {
            details.Add(new Tuple<string, string>("Orders", "None Assigned"));
        }

        _view.PopulateOrderDetails(details, hasOrder);
    }

    private void PopulateAdjacentRegions()
    {
        if (_currentRegion == null) return;

        var adjacentRegions = _currentRegion.GetAdjacentRegions();
        var adjacentRegionMap = adjacentRegions.ToDictionary(
            r => GetDirectionFromCurrentToNeighbour(_currentRegion, r), // Needs implementation
            r => r
        );
        // Filter out null directions if GetDirection... can return null
        adjacentRegionMap = adjacentRegionMap.Where(kvp => kvp.Key != null)
                                             .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _view.PopulateAdjacentRegions(_currentRegion, adjacentRegionMap);
    }


    // --- Signal Handlers from View ---

    private void OnSquadSelected(object sender, int squadId)
    {
        // Find the squad regardless of its order grouping
        RegionFaction playerFaction = GetPlayerRegionFaction();
        if (playerFaction != null)
        {
            _selectedSquad = playerFaction.LandedSquads.FirstOrDefault(s => s.Id == squadId);
            PopulateOrderDetails(); // Update order details based on the found squad
        }
        else
        {
            _selectedSquad = null;
            PopulateOrderDetails(); // Clear order details
        }
    }

    private void OnSquadDoubleClicked(object sender, int squadId)
    {
        RegionFaction playerFaction = GetPlayerRegionFaction();
        if (playerFaction != null)
        {
            Squad squad = playerFaction.LandedSquads.FirstOrDefault(s => s.Id == squadId);
            if (squad != null)
            {
                SquadDoubleClicked?.Invoke(this, squad); // Emit signal for MainGameScene
            }
        }
    }

    private void OnAdjacentRegionClicked(object sender, Region region)
    {
        AdjacentRegionChangeRequested?.Invoke(this, region);
        // Controller itself might change region directly or signal MainGameScene
        // For simplicity, let's assume it signals MainGameScene to handle the change
        // DisplayRegion(region); // Or let MainGameScene handle it via the signal
    }

    private void OnUnassignButtonClicked(object sender, EventArgs e)
    {
        if (_selectedSquad?.CurrentOrders != null)
        {
            Order order = _selectedSquad.CurrentOrders;
            order.AssignedSquads.Remove(_selectedSquad);
            _selectedSquad.CurrentOrders = null;

            // If this was the last squad on the order, remove the order entirely
            if (order.AssignedSquads.Count == 0)
            {
                GameDataSingleton.Instance.Sector.RemoveOrder(order);
            }

            PopulateOrderDetails(); // Update UI
        }
    }

    private void OnOpenOrdersButtonClicked(object sender, EventArgs e)
    {
        if (_selectedSquad != null)
        {
            _orderDialog.PopulateOrderData(_selectedSquad);
            _orderDialog.Visible = true;
        }
        else
        {
            GD.PrintErr("Cannot open orders dialog: No squad selected.");
            // Optionally show a user message
        }
    }

    private void OnAssignToExistingButtonClicked(object sender, EventArgs e)
    {
        // TODO: Implement logic to show a list/dialog of existing orders
        // and assign the _selectedSquad to the chosen order.
        GD.Print("Assign to Existing Order button pressed - Needs Implementation");
    }

    private void OnCopyOrdersButtonClicked(object sender, EventArgs e)
    {
        if (_selectedSquad?.CurrentOrders != null)
        {
            _copiedOrder = _selectedSquad.CurrentOrders;
            _view.SetPasteOrdersButtonDisabled(false);
            // Optionally provide user feedback (e.g., temporary text change)
        }
    }

    private void OnPasteOrdersButtonClicked(object sender, EventArgs e)
    {
        if (_selectedSquad != null && _copiedOrder != null)
        {
            // Remove squad from its current order, if any
            if (_selectedSquad.CurrentOrders != null)
            {
                _selectedSquad.CurrentOrders.AssignedSquads.Remove(_selectedSquad);
                if (_selectedSquad.CurrentOrders.AssignedSquads.Count == 0)
                {
                    GameDataSingleton.Instance.Sector.RemoveOrder(_selectedSquad.CurrentOrders);
                }
            }

            // Assign to the copied order
            _selectedSquad.CurrentOrders = _copiedOrder;
            if (!_copiedOrder.AssignedSquads.Contains(_selectedSquad)) // Avoid duplicates if pasting onto self
            {
                _copiedOrder.AssignedSquads.Add(_selectedSquad);
            }
            PopulateOrderDetails(); // Update UI
        }
    }

    private void OnOrdersConfirmed(object sender, EventArgs e)
    {
        // OrderDialog has already updated the squad's order and potentially the global order list.
        // We just need to refresh the display.
        PopulateOrderDetails();
    }

    // --- Helper Methods ---

    private RegionFaction GetPlayerRegionFaction()
    {
        if (_currentRegion == null) return null;
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        _currentRegion.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction regionFaction);
        return regionFaction;
    }

    private bool CanFight(ISoldier soldier)
    {
        // Basic check, same as in SquadScreenController - maybe move to an extension or helper
        bool canWalk = !soldier.Body.HitLocations.Where(hl => hl.Template.IsMotive)
                                                .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFuncion = !soldier.Body.HitLocations.Where(hl => hl.Template.IsVital)
                                                    .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFight = !soldier.Body.HitLocations.Where(hl => hl.Template.IsRangedWeaponHolder) // simplified check
                                                .All(hl => hl.IsCrippled || hl.IsSevered);
        return canWalk && canFuncion && canFight;
    }

    private string GetDirectionFromCurrentToNeighbour(Region currentRegion, Region neighbourRegion)
    {
        int dx = neighbourRegion.Coordinates.Item1 - currentRegion.Coordinates.Item1;
        int dy = neighbourRegion.Coordinates.Item2 - currentRegion.Coordinates.Item2;

        if (dx == 0 && dy > 0) return "N";    // Higher Y is North in many grid systems
        if (dx > 0 && dy > 0) return "NE";
        if (dx > 0 && dy == 0) return "SE"; // Assuming E/SE based on hex/diamond grid
        if (dx == 0 && dy < 0) return "S";    // Lower Y is South
        if (dx < 0 && dy < 0) return "SW";
        if (dx < 0 && dy == 0) return "NW"; // Assuming W/NW based on hex/diamond grid

        return null; // Should not happen for adjacent regions in this specific layout
    }
}
