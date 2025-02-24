using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

public partial class SquadScreenController : DialogController
{
    private Squad _squad;
    private SquadScreenView _view;
    private OrderDialogController _orderController;
    private TacticalRegionController _currentRegion;
    private Dictionary<string, TacticalRegionController> _adjacenTacticalRegionMap;
    private int _ableBodied;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SquadScreenView>("DialogView");
        _view.WeaponSetSelectionWeaponSetCountChanged += OnWeaponSetSelectionWeaponSetCountChanged;
        _view.OpenOrders += OnOpenOrders;
        _orderController = GetNode<OrderDialogController>("DialogView/OrderDialogController");
        _orderController.OrdersConfirmed += OnOrdersConfirmed;
        _currentRegion = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionCenter");
        _adjacenTacticalRegionMap = GetAdjacentRegionDisplayMap();
    }
    private void OnOpenOrders(object sender, EventArgs e)
    {
        _orderController.PopulateOrderData(_squad);
        _orderController.Visible = true;
    }

    private void OnOrdersConfirmed(object sender, EventArgs e)
    {
        PopulateOrderDetails();
        _view.SetOpenOrdersButtonText("Edit Current Orders");
    }

    private void OnWeaponSetSelectionWeaponSetCountChanged(object sender, Tuple<string, int> args)
    {
        WeaponSetSelectionView view = (WeaponSetSelectionView)sender;
        string optionName = view.OptionName;
        var matchingLoadouts = _squad.Loadout.Where(ws => ws.Name == args.Item1);
        if(matchingLoadouts.Count() > args.Item2)
        {
            // remove the difference between the current count and the new count
            for (int i = 0; i < matchingLoadouts.Count() - args.Item2; i++)
            {
                _squad.Loadout.Remove(matchingLoadouts.First());
                _squad.Loadout.Add(_squad.SquadTemplate.DefaultWeapons);
            }
        }
        else if(matchingLoadouts.Count() < args.Item2)
        {
            // add the difference between the current count and the new count
            for (int i = 0; i < args.Item2 - matchingLoadouts.Count(); i++)
            {
                _squad.Loadout.Remove(_squad.SquadTemplate.DefaultWeapons);
                _squad.Loadout.Add(_squad.SquadTemplate.WeaponOptions.First(o => o.Name == optionName).Options.First(o => o.Name == args.Item1));
            }
        }
        int defaultCount = _ableBodied - _squad.Loadout.Where(ws => ws != _squad.SquadTemplate.DefaultWeapons).Count();
        _view.SetDefaultWeaponSetCount(defaultCount);
        _view.DisableCountIncreases(defaultCount == 0);
    }

    public void SetSquad(Squad squad)
    {
        _squad = squad;
        _ableBodied = _squad.Members.Where(s => CanFight(s)).Count();
        PopulateSquadDetails();
        PopulateSquadLoadout();
        PopulateSquadOrders();
        PopulateTacticalRegions();
    }

    private void PopulateSquadDetails()
    {
        List<Tuple<string, string>> lines = [];
        lines.Add(new Tuple<string, string>("Squad Name", _squad.Name));
        lines.Add(new Tuple<string, string>("Squad Type", _squad.SquadTemplate.Name));
        if (_squad.CurrentRegion != null)
        {
            lines.Add(new Tuple<string, string>("Location", _squad.CurrentRegion.Name));
        }
        else if (_squad.BoardedLocation != null)
        {
            lines.Add(new Tuple<string, string>("Ship", _squad.BoardedLocation.Name));
        }
        else
        {
            lines.Add(new Tuple<string, string>("Location", "Unknown"));
        }
        lines.Add(new Tuple<string, string>("Squad Size", _squad.Members.Count.ToString()));
        lines.Add(new Tuple<string, string>("Combat Ready Brothers", _squad.Members.Where(s => CanFight(s)).Count().ToString()));

        _view.PopulateSquadData(lines);
    }

    private void PopulateSquadLoadout()
    {
        List<Tuple<List<string>, string, int, int, int>> weaponSets = new List<Tuple<List<string>, string, int, int, int>>();
        WeaponSet defaultWs = _squad.SquadTemplate.DefaultWeapons;
        
        Dictionary<SquadWeaponOption, int> weaponSetCounts = new Dictionary<SquadWeaponOption, int>();
        foreach (WeaponSet ws in _squad.Loadout)
        {
            if(ws != defaultWs)
            {
                var option = _squad.SquadTemplate.WeaponOptions.First(o => o.Options.Contains(ws));
                if(weaponSetCounts.ContainsKey(option))
                {
                    weaponSetCounts[option]++;
                }
                else
                {
                    weaponSetCounts[option] = 1;
                }
            }
        }
        foreach (var weaponOptions in _squad.SquadTemplate.WeaponOptions)
        {
            Tuple<List<string>, string, int, int, int> options =
                new Tuple<List<string>, string, int, int, int>(
                    weaponOptions.Options.Select(o => o.Name).ToList(),
                    weaponOptions.Name,
                    weaponOptions.MinNumber,
                    Math.Min(weaponOptions.MaxNumber, _ableBodied),
                    weaponSetCounts.ContainsKey(weaponOptions) ? weaponSetCounts[weaponOptions] : 0);
            weaponSets.Add(options);
        }
        int defaultCount = _ableBodied - weaponSetCounts.Values.Sum();
        Tuple<string, int> defaultOptions = new Tuple<string, int>(defaultWs.Name, defaultCount);
        _view.PopulateSquadLoadout(weaponSets, defaultOptions);
    }

    private void PopulateSquadOrders()
    {
        PopulateOrderDetails();
    }

    private void PopulateOrderDetails()
    {
        List<Tuple<string, string>> lines = [];
        if (_squad.CurrentOrders != null)
        {
            lines.Add(new Tuple<string, string>("Mission Type", _squad.CurrentOrders.MissionType.ToString()));
            lines.Add(new Tuple<string, string>("Mission Target", _squad.CurrentOrders.TargetRegion.Name));
            lines.Add(new Tuple<string, string>("Size of Operation", "This Squad"));
            lines.Add(new Tuple<string, string>("Engagement Level", _squad.CurrentOrders.LevelOfAggression.ToString()));
            _view.SetOpenOrdersButtonText("Edit Current Orders");
        }
        else
        {
            _view.SetOpenOrdersButtonText("Assign Orders");
        }
        _view.PopulateOrderDetails(lines);
    }

    private void PopulateTacticalRegions()
    {
        _currentRegion.Populate(_squad.CurrentRegion);
        var adjacentRegions = RegionExtensions.GetAdjacentRegions(_squad.CurrentRegion);
        foreach(Region region in adjacentRegions)
        {
            string direction = GetDirectionFromCurrentToNeighbour(_squad.CurrentRegion, region);
            _adjacenTacticalRegionMap[direction].Populate(region);
            _adjacenTacticalRegionMap[direction].Visible = true;
        }
    }

    private bool CanFight(ISoldier soldier)
    {
        bool canWalk = !soldier.Body.HitLocations.Where(hl => hl.Template.IsMotive)
                                                .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFuncion = !soldier.Body.HitLocations.Where(hl => hl.Template.IsVital)
                                                    .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFight = !soldier.Body.HitLocations.Where(hl => hl.Template.IsRangedWeaponHolder)
                                                .All(hl => hl.IsCrippled || hl.IsSevered);
        return canWalk && canFuncion && canFight;
    }

    private Dictionary<string, TacticalRegionController> GetAdjacentRegionDisplayMap()
    {
        var regionDirectionMap = new Dictionary<string, TacticalRegionController>();
        regionDirectionMap["NW"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionNorthwest");
        regionDirectionMap["N"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionNorth");
        regionDirectionMap["NE"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionNortheast");
        regionDirectionMap["SW"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionSouthwest");
        regionDirectionMap["S"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionSouth");
        regionDirectionMap["SE"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionSoutheast");
        foreach(var region in regionDirectionMap.Values)
        {
            region.Visible = false;
        }
        return regionDirectionMap;
    }

    private string GetDirectionFromCurrentToNeighbour(Region currentRegion, Region neighbourRegion)
    {
        // **You need to implement the logic to determine the direction (e.g., "N", "NE", "E", etc.)**
        // **based on the relative positions or relationships of `currentRegion` and `neighbourRegion`.**

        // **Example (Conceptual -  you'll need to adapt this to your Region/Grid system):**
        int dx = neighbourRegion.Coordinates.Item1 - currentRegion.Coordinates.Item1; // Example: Grid coordinates
        int dy = neighbourRegion.Coordinates.Item2 - currentRegion.Coordinates.Item2;

        if (dy == 1 && dx == 0) return "N";
        if (dy == 1 && dx == 1) return "NE";
        if (dy == 0 && dx == 1) return "SE";
        if (dy == -1 && dx == 0) return "S";
        if (dy == -1 && dx == -1) return "SW";
        if (dy == 0 && dx == -1) return "NW";

        return null; // Or throw an exception if direction cannot be determined (error case)
    }
}
