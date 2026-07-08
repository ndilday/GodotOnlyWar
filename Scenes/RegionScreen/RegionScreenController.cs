using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RegionScreenController : DialogController
{
    private const string ActionOpenAdjacentRegion = "open_adjacent_region";
    private const string ActionOpenSquad = "open_squad";
    private const string ActionEditOrders = "edit_orders";
    private const string ActionUnassign = "unassign";
    private const string ActionCopyOrders = "copy_orders";
    private const string ActionPasteOrders = "paste_orders";

    private static readonly (MapLayer Layer, string Label, string IconKey)[] MapLayerOptions =
    [
        (MapLayer.Forces, "Forces", "infantry"),
        (MapLayer.Orders, "Orders", "objective"),
        (MapLayer.Intel, "Intel", "threat")
    ];

    private static readonly (string Key, string Label)[] RosterFilters =
    [
        ("all", "All"),
        ("unassigned", "Unassigned"),
        ("injured", "Injured")
    ];

    private RegionScreenView _view;
    private Region _currentRegion;
    private Region _selectedAdjacentRegion;
    private Squad _selectedSquad;
    private Order _selectedOrder;
    private Order _copiedOrder;
    private OrderDialogController _orderDialog;
    private MapLayer _activeLayers = MapLayer.Forces | MapLayer.Orders;
    private string _rosterFilter = "all";

    public event EventHandler<Squad> SquadDoubleClicked;
    public event EventHandler<Region> AdjacentRegionChangeRequested;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<RegionScreenView>("DialogView");
        _orderDialog = GetNode<OrderDialogController>("DialogView/OrderDialogController");

        _view.SelectionTreeItemSelected += OnSelectionTreeItemSelected;
        _view.SelectionTreeItemActivated += OnSelectionTreeItemActivated;
        _view.CommandPressed += OnCommandPressed;
        _view.AdjacentRegionClicked += OnAdjacentRegionClicked;
        _view.MapLayerToggled += OnMapLayerToggled;
        _view.RosterFilterSelected += OnRosterFilterSelected;
        _orderDialog.OrdersConfirmed += OnOrdersConfirmed;

        _view.SetMapLayerOptions(MapLayerOptions);
        _view.SetActiveMapLayers(_activeLayers);
        _view.SetRosterFilters(RosterFilters);
        _view.SetActiveRosterFilter(_rosterFilter);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_view))
        {
            _view.SelectionTreeItemSelected -= OnSelectionTreeItemSelected;
            _view.SelectionTreeItemActivated -= OnSelectionTreeItemActivated;
            _view.CommandPressed -= OnCommandPressed;
            _view.AdjacentRegionClicked -= OnAdjacentRegionClicked;
            _view.MapLayerToggled -= OnMapLayerToggled;
            _view.RosterFilterSelected -= OnRosterFilterSelected;
        }
        if (GodotObject.IsInstanceValid(_orderDialog))
        {
            _orderDialog.OrdersConfirmed -= OnOrdersConfirmed;
        }
    }

    private void OnMapLayerToggled(object sender, MapLayer layer)
    {
        _activeLayers ^= layer;
        _view.SetActiveMapLayers(_activeLayers);
        PopulateAdjacentRegions();
    }

    private void OnRosterFilterSelected(object sender, string key)
    {
        _rosterFilter = key;
        _view.SetActiveRosterFilter(_rosterFilter);
        RefreshWorkspace();
    }

    public void DisplayRegion(Region region)
    {
        _currentRegion = region;
        _selectedAdjacentRegion = null;
        _selectedSquad = null;
        _selectedOrder = null;
        _copiedOrder = null;
        RefreshWorkspace();
    }

    private void OnSelectionTreeItemSelected(object sender, string key)
    {
        ApplySelectionKey(key);
        RefreshContextAndCommands();
    }

    private void OnSelectionTreeItemActivated(object sender, string key)
    {
        ApplySelectionKey(key);
        if (_selectedSquad != null)
        {
            SquadDoubleClicked?.Invoke(this, _selectedSquad);
            return;
        }

        if (_selectedAdjacentRegion != null)
        {
            AdjacentRegionChangeRequested?.Invoke(this, _selectedAdjacentRegion);
        }
    }

    private void OnCommandPressed(object sender, string key)
    {
        switch (key)
        {
            case ActionOpenAdjacentRegion:
                if (_selectedAdjacentRegion != null)
                {
                    AdjacentRegionChangeRequested?.Invoke(this, _selectedAdjacentRegion);
                }
                break;
            case ActionOpenSquad:
                if (_selectedSquad != null)
                {
                    SquadDoubleClicked?.Invoke(this, _selectedSquad);
                }
                break;
            case ActionEditOrders:
                OpenOrdersDialog();
                break;
            case ActionUnassign:
                UnassignSelectedSquad();
                break;
            case ActionCopyOrders:
                CopySelectedOrders();
                break;
            case ActionPasteOrders:
                PasteCopiedOrders();
                break;
        }
    }

    private void OnAdjacentRegionClicked(object sender, Region region)
    {
        AdjacentRegionChangeRequested?.Invoke(this, region);
    }

    private void OnOrdersConfirmed(object sender, EventArgs e)
    {
        RefreshWorkspace();
    }

    private void RefreshWorkspace()
    {
        if (_currentRegion == null) return;

        _view.SetHeader($"{_currentRegion.Planet.Name} / {_currentRegion.Name}");
        _view.SetSelectionTitle("ROSTER", "Select a squad, contact, or adjacent region. Edit, copy, or paste orders from the command bar.");
        _view.PopulateSelectionTree(BuildRoster());
        PopulateAdjacentRegions();
        RefreshContextAndCommands();
    }

    private void RefreshContextAndCommands()
    {
        if (_currentRegion == null) return;

        PopulateAdjacentRegions();
        _view.SetContext(GetContextTitle(), GetContextSubtitle(), BuildContextRows());
        _view.SetCommands(BuildCommands());
    }

    private IReadOnlyList<CommandTreeNode> BuildRoster()
    {
        if (_currentRegion == null) return Array.Empty<CommandTreeNode>();

        return
        [
            BuildDeployedForcesGroup(),
            BuildKnownPresenceGroup(),
            new CommandTreeNode("group:adjacent", "Adjacent Regions", BuildAdjacentRegionNodes())
        ];
    }

    private CommandTreeNode BuildDeployedForcesGroup()
    {
        RegionFaction playerFaction = GetPlayerRegionFaction();
        List<CommandTreeNode> units = [];
        if (playerFaction != null)
        {
            foreach (IGrouping<OnlyWar.Models.Units.Unit, Squad> group in playerFaction.LandedSquads
                .Where(squad => squad.Members.Count > 0 && RosterFormat.MatchesFilter(squad, _rosterFilter))
                .GroupBy(squad => squad.ParentUnit))
            {
                units.Add(new CommandTreeNode(
                    $"unit:{group.Key.Id}",
                    $"{group.Key.Name} | {group.Sum(squad => squad.Members.Count)} marines",
                    group.Select(squad => new CommandTreeNode(SquadKey(squad.Id), RosterFormat.SquadLabel(squad))).ToList()));
            }
        }

        return new CommandTreeNode("group:marines", "Deployed Forces", units);
    }

    private CommandTreeNode BuildKnownPresenceGroup()
    {
        List<CommandTreeNode> presences = [];
        foreach (RegionFaction faction in _currentRegion.RegionFactionMap.Values.Where(rf => rf.IsPublic && !rf.PlanetFaction.Faction.IsPlayerFaction))
        {
            string role = faction.PlanetFaction.Faction.IsDefaultFaction ? "Allied/PDF" : "Enemy";
            string strength = faction.PlanetFaction.Faction.IsDefaultFaction
                ? $"{faction.Garrison:N0} garrison"
                : faction.GetPopulationDescription();
            presences.Add(new CommandTreeNode($"presence:{faction.PlanetFaction.Faction.Id}", $"{role}: {faction.PlanetFaction.Faction.Name} | {strength}"));
        }
        return new CommandTreeNode("group:presence", "Known Presence", presences);
    }

    private IReadOnlyList<CommandTreeNode> BuildAdjacentRegionNodes()
    {
        return _currentRegion.GetAdjacentRegions()
            .OrderBy(region => region.Name)
            .Select(region => new CommandTreeNode(AdjacentRegionKey(region.Id), $"{region.Name} | {GetRegionControlLabel(region)}"))
            .ToList();
    }

    private string GetContextTitle()
    {
        if (_selectedSquad != null) return _selectedSquad.Name;
        if (_selectedOrder != null) return $"{_selectedOrder.Mission.MissionType} Orders";
        if (_selectedAdjacentRegion != null) return _selectedAdjacentRegion.Name;
        return _currentRegion?.Name ?? "Region Detail";
    }

    private string GetContextSubtitle()
    {
        if (_selectedSquad != null) return $"{_selectedSquad.ParentUnit?.Name ?? "Unknown Unit"} deployed in {_currentRegion.Name}";
        if (_selectedOrder != null) return $"Mission target: {_selectedOrder.Mission.RegionFaction.Region.Name}";
        if (_selectedAdjacentRegion != null) return "Adjacent region on the same planet";
        return $"{_currentRegion.Planet.Name} surface command";
    }

    private IReadOnlyList<Tuple<string, string>> BuildContextRows()
    {
        if (_selectedSquad != null) return BuildSquadRows(_selectedSquad);
        if (_selectedOrder != null) return BuildOrderRows(_selectedOrder);
        if (_selectedAdjacentRegion != null) return BuildRegionRows(_selectedAdjacentRegion);
        return BuildRegionRows(_currentRegion);
    }

    private IReadOnlyList<Tuple<string, string>> BuildRegionRows(Region region)
    {
        List<Tuple<string, string>> rows = [];
        rows.Add(Row("Region Name", region.Name));
        rows.Add(Row("Planet", region.Planet.Name));
        rows.Add(Row("Control", GetRegionControlLabel(region)));
        rows.Add(Row("Coordinates", $"({region.Coordinates.X}, {region.Coordinates.Y})"));
        float visibleIntel = region.GetPlayerVisibleIntel();
        rows.Add(Row("Intelligence", $"{visibleIntel:0.##}"));

        RegionFaction playerFaction = GetPlayerRegionFaction(region);
        RegionFaction defaultFaction = region.RegionFactionMap.Values.FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
        RegionFaction enemyFaction = GetEnemyRegionFaction(region);

        long civilianPop = (defaultFaction?.Population ?? 0) + (playerFaction?.Population ?? 0);
        if (enemyFaction != null && !enemyFaction.IsPublic)
        {
            civilianPop += enemyFaction.Population;
        }
        rows.Add(Row("Civilian Population", civilianPop > 0 ? civilianPop.ToString("N0") : "None"));
        rows.Add(Row("Marines", playerFaction?.LandedSquads.Sum(squad => squad.Members.Count).ToString() ?? "0"));
        rows.Add(Row("Assigned Orders", playerFaction?.LandedSquads.Count(squad => squad.CurrentOrders != null).ToString() ?? "0"));

        if (enemyFaction != null && enemyFaction.IsPublic)
        {
            rows.Add(Row("Enemy Faction", enemyFaction.PlanetFaction.Faction.Name));
            rows.Add(Row("Enemy Population", enemyFaction.GetPopulationDescription()));
            if (visibleIntel > 1)
            {
                rows.Add(Row("Enemy Entrenchment", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.Entrenchment)));
                rows.Add(Row("Enemy Listening Posts", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.ListeningPost)));
                rows.Add(Row("Enemy Anti-Air", RegionFactionExtensions.GetDefenseLevelDescription(enemyFaction.AntiAir)));
            }
        }
        else
        {
            rows.Add(Row("Enemy Presence", "None Detected"));
        }

        rows.Add(Row("Adjacent Regions", region.GetAdjacentRegions().Count().ToString()));
        return rows;
    }

    private IReadOnlyList<Tuple<string, string>> BuildSquadRows(Squad squad)
    {
        List<Tuple<string, string>> rows = [];
        rows.Add(Row("Unit", squad.ParentUnit?.Name ?? "Unknown"));
        rows.Add(Row("Fighting Strength", $"{squad.Members.Count(member => member.CanFight)}/{squad.Members.Count}"));
        rows.Add(Row("Orders", squad.CurrentOrders?.Mission.MissionType.ToString() ?? "Unassigned"));
        if (squad.CurrentOrders != null)
        {
            rows.Add(Row("Target Region", squad.CurrentOrders.Mission.RegionFaction.Region.Name));
            rows.Add(Row("Target Faction", squad.CurrentOrders.Mission.RegionFaction.PlanetFaction?.Faction.Name ?? "Unknown"));
            rows.Add(Row("Aggression", squad.CurrentOrders.LevelOfAggression.ToString()));
            rows.Add(Row("Squads Assigned", squad.CurrentOrders.AssignedSquads.Count.ToString()));
        }
        return rows;
    }

    private static IReadOnlyList<Tuple<string, string>> BuildOrderRows(Order order)
    {
        return
        [
            Row("Mission Type", order.Mission.MissionType.ToString()),
            Row("Target Region", order.Mission.RegionFaction.Region.Name),
            Row("Target Faction", order.Mission.RegionFaction.PlanetFaction?.Faction.Name ?? "Unknown"),
            Row("Aggression", order.LevelOfAggression.ToString()),
            Row("Squads Assigned", order.AssignedSquads.Count.ToString())
        ];
    }

    private IReadOnlyList<CommandAction> BuildCommands()
    {
        return
        [
            new CommandAction(ActionOpenAdjacentRegion, "Open Region", "map_pin", _selectedAdjacentRegion != null),
            new CommandAction(ActionOpenSquad, "Open Squad", "infantry", _selectedSquad != null),
            new CommandAction(ActionEditOrders, "Edit Orders", "objective", _selectedSquad != null),
            new CommandAction(ActionUnassign, "Unassign", "locked", _selectedSquad?.CurrentOrders != null),
            new CommandAction(ActionCopyOrders, "Copy", "archive", _selectedSquad?.CurrentOrders != null),
            new CommandAction(ActionPasteOrders, GetPasteCommandText(), "save", CanPasteOrders())
        ];
    }

    private void ApplySelectionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("group:") || key.StartsWith("presence:")) return;

        _selectedAdjacentRegion = null;
        _selectedSquad = null;
        _selectedOrder = null;

        string[] parts = key.Split(':');
        switch (parts[0])
        {
            case "adjacent":
                _selectedAdjacentRegion = _currentRegion.GetAdjacentRegions().FirstOrDefault(region => region.Id == int.Parse(parts[1]));
                break;
            case "squad":
                _selectedSquad = GetPlayerRegionFaction()?.LandedSquads.FirstOrDefault(squad => squad.Id == int.Parse(parts[1]));
                _selectedOrder = _selectedSquad?.CurrentOrders;
                break;
        }
    }

    private void OpenOrdersDialog()
    {
        if (_selectedSquad != null)
        {
            _orderDialog.PopulateOrderData(_selectedSquad);
            _orderDialog.Visible = true;
        }
        else
        {
            GD.PrintErr("Cannot open orders dialog: No squad selected.");
        }
    }

    private void UnassignSelectedSquad()
    {
        if (_selectedSquad?.CurrentOrders == null) return;

        Order order = _selectedSquad.CurrentOrders;
        order.AssignedSquads.Remove(_selectedSquad);
        _selectedSquad.CurrentOrders = null;
        if (order.AssignedSquads.Count == 0)
        {
            GameDataSingleton.Instance.Sector.RemoveOrder(order);
        }

        _selectedOrder = null;
        RefreshWorkspace();
    }

    private void CopySelectedOrders()
    {
        if (_selectedSquad?.CurrentOrders == null) return;

        _copiedOrder = _selectedSquad.CurrentOrders;
        RefreshContextAndCommands();
    }

    private void PasteCopiedOrders()
    {
        if (!CanPasteOrders()) return;

        if (_selectedSquad.CurrentOrders != null)
        {
            Order oldOrder = _selectedSquad.CurrentOrders;
            oldOrder.AssignedSquads.Remove(_selectedSquad);
            if (oldOrder.AssignedSquads.Count == 0)
            {
                GameDataSingleton.Instance.Sector.RemoveOrder(oldOrder);
            }
        }

        _selectedSquad.CurrentOrders = _copiedOrder;
        if (!_copiedOrder.AssignedSquads.Contains(_selectedSquad))
        {
            _copiedOrder.AssignedSquads.Add(_selectedSquad);
        }
        _selectedOrder = _copiedOrder;
        RefreshWorkspace();
    }

    private string GetPasteCommandText()
    {
        return _copiedOrder != null ? $"Paste ({_copiedOrder.Mission.MissionType})" : "Paste";
    }

    private bool CanPasteOrders()
    {
        return _selectedSquad != null
            && _copiedOrder != null
            && _selectedSquad.Members.Count(member => member.CanFight) >= 5;
    }

    private void PopulateAdjacentRegions()
    {
        Dictionary<string, Region> adjacentRegionMap = _currentRegion.GetAdjacentRegions()
            .Select(region => new { Direction = GetDirectionFromCurrentToNeighbour(_currentRegion, region), Region = region })
            .Where(entry => entry.Direction != null)
            .ToDictionary(entry => entry.Direction, entry => entry.Region);

        _view.PopulateAdjacentRegions(_currentRegion, adjacentRegionMap, _activeLayers);
    }

    private RegionFaction GetPlayerRegionFaction()
    {
        return GetPlayerRegionFaction(_currentRegion);
    }

    private static RegionFaction GetPlayerRegionFaction(Region region)
    {
        if (region == null) return null;
        Faction playerFaction = GameDataSingleton.Instance.Sector.PlayerForce.Faction;
        region.RegionFactionMap.TryGetValue(playerFaction.Id, out RegionFaction regionFaction);
        return regionFaction;
    }

    private static RegionFaction GetEnemyRegionFaction(Region region)
    {
        return region.RegionFactionMap.Values.FirstOrDefault(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
    }

    private static string GetRegionControlLabel(Region region)
    {
        return region.ControllingFaction?.PlanetFaction.Faction.Name ?? "Contested";
    }

    private static Tuple<string, string> Row(string label, string value)
    {
        return new Tuple<string, string>(label, value);
    }

    private static string AdjacentRegionKey(int regionId) => $"adjacent:{regionId}";
    private static string SquadKey(int squadId) => $"squad:{squadId}";

    private string GetDirectionFromCurrentToNeighbour(Region currentRegion, Region neighbourRegion)
    {
        int dx = neighbourRegion.Coordinates.X - currentRegion.Coordinates.X;
        int dy = neighbourRegion.Coordinates.Y - currentRegion.Coordinates.Y;

        if (dx == 0 && dy > 0) return "N";
        if (dx > 0 && dy > 0) return "NE";
        if (dx > 0 && dy == 0) return "SE";
        if (dx == 0 && dy < 0) return "S";
        if (dx < 0 && dy < 0) return "SW";
        if (dx < 0 && dy == 0) return "NW";

        return null;
    }
}
