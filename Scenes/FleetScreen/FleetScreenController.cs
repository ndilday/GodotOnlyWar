using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FleetScreenController : DialogController
{
    private FleetScreenView _view;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetScreenView>("FleetScreenView");
        PopulateFleetData();
    }

    public void PopulateFleetData()
    {
        if (_view == null) return;

        List<TreeNode> fleetNodes = GameDataSingleton.Instance.Sector.Fleets.Values
            .Where(taskForce => taskForce.Faction == GameDataSingleton.Instance.Sector.PlayerForce.Faction)
            .OrderBy(taskForce => taskForce.Id)
            .Select(CreateFleetNode)
            .ToList();

        _view.PopulateFleetTree(fleetNodes);
    }

    private static TreeNode CreateFleetNode(TaskForce taskForce)
    {
        string destinationName = taskForce.Destination?.Name ?? taskForce.Planet?.Name ?? "Unknown";
        string status = GetFleetStatus(taskForce);
        List<TreeNode> shipNodes = taskForce.Ships
            .Select(ship =>
            {
                string shipText = $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})";
                List<TreeNode> squadNodes = ship.LoadedSquads
                    .Where(squad => squad.Members.Count > 0)
                    .Select(squad => new TreeNode(squad.Id, squad.Name, []))
                    .ToList();
                return new TreeNode(ship.Id, shipText, squadNodes);
            })
            .ToList();

        return new TreeNode(taskForce.Id, $"Task Force {taskForce.Id}: {status} - {destinationName}", shipNodes);
    }

    private static string GetFleetStatus(TaskForce taskForce)
    {
        return taskForce.TravelPhase switch
        {
            FleetTravelPhase.OutboundSystemTransit => $"Departing ({taskForce.CurrentPhaseWeeksRemaining}w to warp translation)",
            FleetTravelPhase.InWarp => $"In the Warp ({taskForce.TravelWeeksRemaining}w projected)",
            FleetTravelPhase.InboundSystemTransit => $"Arriving ({taskForce.CurrentPhaseWeeksRemaining}w to orbit)",
            _ => taskForce.Planet == null ? "In transit" : "In orbit"
        };
    }
}
