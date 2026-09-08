using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Fleets;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Application;

public sealed class FleetScreenApplication : CampaignScreenApplication, IFleetScreenApplication
{
    private const string NoCampaignMessage = "No campaign is active.";
    private const string StaleSessionMessage = "This campaign is no longer active.";

    private readonly FleetScreenProjector _fleetProjector = new();
    private FleetCommandContext Fleet => Context.FleetCommand;

    public FleetScreenApplication(CampaignApplicationContext context) : base(context) { }

    public FleetRosterView QueryFleetScreen()
    {
        FleetCommandContext fleet = Fleet;
        return new(
            SessionToken,
            fleet == null
                ? []
                : _fleetProjector.BuildPlayerFleets(
                    fleet.TaskForces,
                    fleet.PlayerFaction,
                    fleet.RecruitmentProgram,
                    fleet.OperationalDoctrine));
    }

    public bool CanTransferSquadToShip(int squadId, int shipId) =>
        FleetTransferService.CanTransferSquadToShip(FindLoadedSquad(squadId), FindShip(shipId));

    public bool CanTransferUnitToShip(int unitId, int sourceShipId, int destinationShipId)
    {
        Ship sourceShip = FindShip(sourceShipId);
        return FleetTransferService.CanTransferUnitToShip(
            FindUnitOnShip(unitId, sourceShip), sourceShip, FindShip(destinationShipId));
    }

    public FleetCommandResult TransferSquadToShip(Guid sessionToken, int squadId, int shipId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;

        Squad squad = FindLoadedSquad(squadId);
        Ship destination = FindShip(shipId);
        if (!FleetTransferService.CanTransferSquadToShip(squad, destination))
        {
            return FleetCommandResult.Failed("That squad cannot berth on that ship.");
        }

        FleetTransferService.TransferSquadToShip(squad, destination);
        return FleetCommandResult.Ok();
    }

    public FleetCommandResult TransferUnitToShip(
        Guid sessionToken, int unitId, int sourceShipId, int destinationShipId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;

        Ship sourceShip = FindShip(sourceShipId);
        Ship destinationShip = FindShip(destinationShipId);
        Unit unit = FindUnitOnShip(unitId, sourceShip);
        if (!FleetTransferService.CanTransferUnitToShip(unit, sourceShip, destinationShip))
        {
            return FleetCommandResult.Failed("That formation cannot berth on that ship together.");
        }

        FleetTransferService.TransferUnitToShip(unit, sourceShip, destinationShip);
        return FleetCommandResult.Ok();
    }

    public FleetActionAvailability QueryFleetActions(int fleetId)
    {
        if (!TryGetActionableFleet(fleetId, out TaskForce taskForce))
        {
            return FleetActionAvailability.None;
        }

        return new FleetActionAvailability(
            true,
            CanPlotCourse: true,
            CanDivide: taskForce.Ships.Count > 1,
            CanMerge: MergeCandidates(taskForce).Any());
    }

    public FleetLocationView QueryFleetLocation(int fleetId) =>
        TryGetActionableFleet(fleetId, out TaskForce taskForce)
            ? new FleetLocationView(true, taskForce.Planet?.Id)
            : new FleetLocationView(false, null);

    public FleetMoveOptionsView QueryFleetMoveOptions(int fleetId)
    {
        if (!TryGetActionableFleet(fleetId, out TaskForce taskForce))
        {
            return FleetMoveOptionsView.Unavailable;
        }

        FleetCommandContext fleet = Fleet;
        List<FleetDestinationOption> destinations = fleet.Planets
            .Where(planet => planet != taskForce.Planet)
            .OrderBy(planet => FleetRouteCalculator.CalculateDistance(taskForce.Planet, planet))
            .Select(planet => new FleetDestinationOption(planet.Id, planet.Name))
            .ToList();
        return new FleetMoveOptionsView(
            true,
            $"Task Force {taskForce.Id} — Plot Course from {taskForce.Planet?.Name ?? "Unknown"}",
            destinations);
    }

    public FleetRouteView QueryFleetRoute(int fleetId, int destinationPlanetId)
    {
        if (!TryGetPlottedRoute(fleetId, destinationPlanetId, out _, out Planet destination,
            out FleetRoute route))
        {
            return FleetRouteView.Unavailable;
        }

        return new FleetRouteView(true, DescribeRoute(destination, route));
    }

    public FleetCommandResult PlotCourse(Guid sessionToken, int fleetId, int destinationPlanetId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;

        if (!TryGetPlottedRoute(fleetId, destinationPlanetId, out TaskForce taskForce,
            out Planet destination, out FleetRoute route))
        {
            return FleetCommandResult.Failed("That course cannot be plotted.");
        }

        taskForce.OrderMoveTo(destination, route);
        return FleetCommandResult.Ok();
    }

    public FleetDivideOptionsView QueryFleetDivideOptions(int fleetId)
    {
        if (!TryGetActionableFleet(fleetId, out TaskForce taskForce))
        {
            return FleetDivideOptionsView.Unavailable;
        }

        List<FleetShipOption> ships = taskForce.Ships
            .OrderBy(ship => ship.Template.Id)
            .Select(ship => new FleetShipOption(
                ship.Id, $"{ship.Name} ({ship.LoadedSoldierCount}/{ship.Template.SoldierCapacity})"))
            .ToList();
        return new FleetDivideOptionsView(true, $"Task Force {taskForce.Id} — Divide", ships);
    }

    public FleetDivideSelectionView EvaluateDivideSelection(
        int fleetId, IReadOnlyList<int> shipIds)
    {
        if (!TryGetActionableFleet(fleetId, out TaskForce taskForce))
        {
            return new FleetDivideSelectionView(false, string.Empty);
        }

        int selected = SelectedShips(taskForce, shipIds).Count;
        int total = taskForce.Ships.Count;
        if (selected == 0)
        {
            return new FleetDivideSelectionView(
                false, "Select the ships to peel off into a new task force.");
        }

        if (selected >= total)
        {
            return new FleetDivideSelectionView(
                false, "At least one ship must remain in the original task force.");
        }

        return new FleetDivideSelectionView(
            true,
            $"{selected} ship(s) will form a new task force; {total - selected} will remain.");
    }

    public FleetCommandResult DivideFleet(
        Guid sessionToken, int fleetId, IReadOnlyList<int> shipIds)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;

        if (!TryGetActionableFleet(fleetId, out TaskForce taskForce))
        {
            return FleetCommandResult.Failed("That task force cannot be reorganized.");
        }

        List<Ship> ships = SelectedShips(taskForce, shipIds);
        if (ships.Count == 0 || ships.Count >= taskForce.Ships.Count)
        {
            return FleetCommandResult.Failed(
                "At least one ship must remain in the original task force.");
        }

        Fleet.Split(taskForce, ships);
        return FleetCommandResult.Ok();
    }

    public FleetMergeOptionsView QueryFleetMergeOptions(int fleetId)
    {
        if (!TryGetActionableFleet(fleetId, out TaskForce taskForce))
        {
            return FleetMergeOptionsView.Unavailable;
        }

        List<FleetMergeTargetOption> targets = MergeCandidates(taskForce)
            .Select(candidate => new FleetMergeTargetOption(
                candidate.Id, $"Task Force {candidate.Id} ({candidate.Ships.Count} ship(s))"))
            .ToList();
        return new FleetMergeOptionsView(true, $"Task Force {taskForce.Id} — Merge", targets);
    }

    public FleetMergeSelectionView EvaluateMergeSelection(int fleetId, int targetFleetId)
    {
        if (!TryGetMergePair(fleetId, targetFleetId, out TaskForce taskForce, out TaskForce target))
        {
            return new FleetMergeSelectionView(false, string.Empty);
        }

        return new FleetMergeSelectionView(
            true,
            $"Merge Task Force {target.Id} ({target.Ships.Count} ship(s)) "
                + $"into Task Force {taskForce.Id}.");
    }

    public FleetCommandResult MergeFleet(Guid sessionToken, int fleetId, int targetFleetId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;

        if (!TryGetMergePair(fleetId, targetFleetId, out TaskForce taskForce, out TaskForce target))
        {
            return FleetCommandResult.Failed("Those task forces cannot be merged.");
        }

        // The clicked task force is retained; the selected target is folded into it.
        Fleet.Combine(taskForce, target);
        return FleetCommandResult.Ok();
    }

    private FleetCommandResult RejectFleetCommand(Guid sessionToken)
    {
        if (Fleet == null) return FleetCommandResult.Failed(NoCampaignMessage);
        if (sessionToken != SessionToken) return FleetCommandResult.Failed(StaleSessionMessage);
        return null;
    }

    // Only player task forces sitting in orbit can be re-tasked; a fleet already in transit
    // cannot change course or be reorganized until it arrives.
    private bool TryGetActionableFleet(int fleetId, out TaskForce taskForce)
    {
        taskForce = null;
        FleetCommandContext fleet = Fleet;
        if (fleet == null) return false;
        TaskForce found = fleet.FindTaskForce(fleetId);
        if (found == null) return false;
        if (found.Faction != fleet.PlayerFaction) return false;
        if (found.TravelPhase != FleetTravelPhase.InOrbit || found.Planet == null) return false;

        taskForce = found;
        return true;
    }

    private IEnumerable<TaskForce> MergeCandidates(TaskForce taskForce) =>
        Fleet.TaskForces
            .Where(other => other.Id != taskForce.Id
                && other.Faction == taskForce.Faction
                && other.Planet == taskForce.Planet
                && other.TravelPhase == FleetTravelPhase.InOrbit)
            .OrderBy(other => other.Id);

    private bool TryGetMergePair(int fleetId, int targetFleetId,
        out TaskForce taskForce, out TaskForce target)
    {
        target = null;
        if (!TryGetActionableFleet(fleetId, out taskForce)) return false;
        target = MergeCandidates(taskForce).FirstOrDefault(other => other.Id == targetFleetId);
        return target != null;
    }

    private bool TryGetPlottedRoute(int fleetId, int destinationPlanetId,
        out TaskForce taskForce, out Planet destination, out FleetRoute route)
    {
        destination = null;
        route = null;
        if (!TryGetActionableFleet(fleetId, out taskForce)) return false;
        FleetCommandContext fleet = Fleet;
        destination = fleet.FindPlanet(destinationPlanetId);
        if (destination == null || destination == taskForce.Planet)
        {
            destination = null;
            return false;
        }

        route = fleet.CalculateRoute(taskForce, destination);
        return route != null;
    }

    private static List<Ship> SelectedShips(TaskForce taskForce, IReadOnlyList<int> shipIds)
    {
        if (shipIds == null || shipIds.Count == 0) return [];
        HashSet<int> requested = shipIds.ToHashSet();
        return taskForce.Ships.Where(ship => requested.Contains(ship.Id)).ToList();
    }

    private Ship FindShip(int shipId) =>
        Fleet?.FindShip(shipId);

    private Squad FindLoadedSquad(int squadId) =>
        Fleet?.FindLoadedSquad(squadId);

    private static Unit FindUnitOnShip(int unitId, Ship ship) =>
        ship?.LoadedSquads
            .Select(squad => squad.ParentUnit)
            .FirstOrDefault(unit => unit?.Id == unitId);

    private static string DescribeRoute(Planet destination, FleetRoute route)
    {
        string routeType = route.RouteType == FleetRouteType.WarpLane ? "Warp Lane" : "Direct";
        string scope = route.Scope switch
        {
            FleetRouteScope.SameSubsector => "Same subsector",
            FleetRouteScope.AdjacentSubsector => "Adjacent subsector",
            _ => "Distant subsector"
        };
        string arrival = route.EstimatedMinTurns == route.EstimatedMaxTurns
            ? $"{route.EstimatedMinTurns} weeks"
            : $"{route.EstimatedMinTurns}–{route.EstimatedMaxTurns} weeks";

        return $"Destination: {destination.Name}\n"
            + $"Route: {routeType}\n"
            + $"Distance: {scope} ({route.TotalDistance:0.0} ly)\n"
            + $"Estimated transit: {arrival}";
    }
}
