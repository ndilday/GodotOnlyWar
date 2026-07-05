using Godot;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
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

    internal static TreeNode CreateFleetNode(TaskForce taskForce)
    {
        // A task force in the Warp is out of contact: it, its ships, and the marines
        // aboard are listed for accounting but cannot be selected or inspected.
        bool isInWarp = taskForce.TravelPhase == FleetTravelPhase.InWarp;
        string status = GetFleetStatus(taskForce);
        List<TreeNode> shipNodes = taskForce.Ships
            .OrderByDescending(ship => ship.Template.SoldierCapacity)
            .ThenBy(ship => ship.Template.Id)
            .ThenBy(ship => ship.Name)
            .ThenBy(ship => ship.Id)
            .Select(ship =>
            {
                string shipText = $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})";
                List<TreeNode> squadNodes = isInWarp
                    ? []
                    : CreateLoadedUnitNodes(ship).ToList();
                return new TreeNode(ship.Id, shipText, squadNodes, selectable: !isInWarp);
            })
            .ToList();

        return new TreeNode(taskForce.Id, $"Task Force {taskForce.Id}: {status}", shipNodes, selectable: !isInWarp);
    }

    internal static IReadOnlyList<TreeNode> CreateLoadedUnitNodes(Ship ship)
    {
        return ship.LoadedSquads
            .Where(squad => squad.Members.Count > 0)
            .OrderBy(squad => GetUnitOrderKey(squad.ParentUnit))
            .ThenBy(squad => GetSquadOrder(squad))
            .ThenBy(squad => squad.Name)
            .GroupBy(squad => squad.ParentUnit)
            .Select(group =>
            {
                Unit unit = group.Key;
                List<TreeNode> squadNodes = group
                    .Select(squad => new TreeNode(squad.Id, squad.Name, []))
                    .ToList();
                return new TreeNode(unit?.Id ?? 0, unit?.Name ?? "Unassigned Unit", squadNodes, selectable: false);
            })
            .ToList();
    }

    private static string GetUnitOrderKey(Unit unit)
    {
        if (unit == null) return "zzzzzzzz";

        Stack<string> segments = [];
        Unit current = unit;
        while (current != null)
        {
            Unit parent = current.ParentUnit;
            if (parent == null)
            {
                segments.Push($"root:{current.Name}:{current.Id:D8}");
                break;
            }

            int index = parent.ChildUnits?.IndexOf(current) ?? -1;
            segments.Push(index >= 0 ? $"{index:D8}" : $"unknown:{current.Name}:{current.Id:D8}");
            current = parent;
        }

        return string.Join("/", segments);
    }

    private static int GetSquadOrder(Squad squad)
    {
        if (squad?.ParentUnit?.Squads == null) return int.MaxValue;

        List<Squad> orderedSquads = squad.ParentUnit.Squads.ToList();
        int index = orderedSquads.IndexOf(squad);
        return index >= 0 ? index : int.MaxValue;
    }

    private static string GetFleetStatus(TaskForce taskForce)
    {
        string destinationName = taskForce.Destination?.Name ?? "Unknown";
        return taskForce.TravelPhase switch
        {
            FleetTravelPhase.OutboundSystemTransit => $"Departing for {destinationName} ({taskForce.CurrentPhaseWeeksRemaining}w to warp translation)",
            FleetTravelPhase.InWarp => $"In Warp to {destinationName}",
            FleetTravelPhase.InboundSystemTransit => $"Arriving at {destinationName} ({taskForce.CurrentPhaseWeeksRemaining}w to orbit)",
            _ => taskForce.Planet != null ? $"In orbit at {taskForce.Planet.Name}" : "In transit"
        };
    }
}
