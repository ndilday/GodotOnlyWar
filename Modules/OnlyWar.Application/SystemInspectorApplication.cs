using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;

namespace OnlyWar.Application;

public sealed partial class CampaignApplication : ISystemInspectorApplication
{
    public SystemInspectorView QuerySystemInspector(
        int? planetId, int? selectedFleetId, bool includeDossier)
    {
        GameSession session = _activeSession;
        if (session == null
            || !planetId.HasValue
            || !session.Sector.Planets.TryGetValue(planetId.Value, out Planet planet))
        {
            return SystemInspectorView.Empty;
        }

        Faction playerFaction = session.Sector.PlayerForce.Faction;
        List<TaskForce> orbiting = session.Sector.Fleets.Values
            .Where(fleet => fleet.Planet == planet
                && fleet.TravelPhase == FleetTravelPhase.InOrbit)
            .OrderByDescending(fleet => fleet.Faction == playerFaction)
            .ThenBy(fleet => fleet.Id)
            .ToList();

        // The player's explicit pick wins while that fleet is still in orbit; otherwise fall back
        // to the first task force this screen could actually act on.
        int? chosenId = selectedFleetId.HasValue
            && orbiting.Any(fleet => fleet.Id == selectedFleetId.Value)
                ? selectedFleetId
                : orbiting.FirstOrDefault(fleet => IsActionablePlayerFleet(fleet, playerFaction))?.Id;
        TaskForce chosen = chosenId.HasValue
            ? orbiting.FirstOrDefault(fleet => fleet.Id == chosenId.Value)
            : null;

        int openRequests = session.Sector.PlayerForce.Requests
            .Count(request => request.TargetPlanet == planet
                && request.Status is RequestStatus.Open or RequestStatus.InProgress);
        Faction controllingFaction = planet.GetControllingFaction();

        return new SystemInspectorView(
            true,
            BuildSystemNameLabel(session.Sector, planet),
            controllingFaction != null
                ? $"Controlled by {controllingFaction.Name}"
                : "Control unknown",
            orbiting.Count == 1
                ? "1 task force in orbit"
                : $"{orbiting.Count} task forces in orbit",
            openRequests == 1 ? "1 active request" : $"{openRequests} active requests",
            planet.Governor?.ActiveRequest is IRequest request
                && request.Status is RequestStatus.Open or RequestStatus.InProgress,
            orbiting
                .Select(fleet => new OrbitingFleetRow(
                    fleet.Id,
                    DescribeOrbitingFleet(fleet, playerFaction),
                    fleet.Faction == playerFaction))
                .ToList(),
            chosen?.Id,
            DescribeSelectedFleet(chosen, orbiting.Count, playerFaction),
            chosen == null
                ? FleetActionAvailability.None
                : QueryFleetActions(chosen.Id),
            includeDossier ? QueryWorldDossier(planet.Id, -1) : null);
    }

    public int? QueryFleetContextPlanet(int fleetId)
    {
        if (_activeSession == null
            || !_activeSession.Sector.Fleets.TryGetValue(fleetId, out TaskForce fleet))
        {
            return null;
        }

        return (fleet.Planet ?? fleet.Origin ?? fleet.Destination)?.Id;
    }

    private static bool IsActionablePlayerFleet(TaskForce fleet, Faction playerFaction) =>
        fleet != null
        && fleet.Faction == playerFaction
        && fleet.TravelPhase == FleetTravelPhase.InOrbit
        && fleet.Planet != null;

    private static string BuildSystemNameLabel(Sector sector, Planet planet)
    {
        Subsector subsector = sector.Subsectors
            .FirstOrDefault(candidate => candidate.Planets.Contains(planet));
        if (subsector == null)
        {
            return planet.Name;
        }

        Planet capital = WarpLaneBuilder.SelectCapital(subsector);
        return $"{planet.Name}, Subsector {capital.Name}";
    }

    private static string DescribeOrbitingFleet(TaskForce fleet, Faction playerFaction)
    {
        string ownership = fleet.Faction == playerFaction ? "Chapter" : fleet.Faction.Name;
        string shipText = fleet.Ships.Count == 1 ? "1 ship" : $"{fleet.Ships.Count} ships";
        int capacity = fleet.Ships.Sum(ship => ship.Template.SoldierCapacity);
        return $"TF {fleet.Id} | {ownership} | {shipText} | Cap {capacity}";
    }

    private static string DescribeSelectedFleet(
        TaskForce fleet, int orbitingCount, Faction playerFaction)
    {
        if (fleet == null)
        {
            return orbitingCount == 0
                ? "No task forces are in orbit."
                : "Select a task force for fleet actions.";
        }

        string ownership = fleet.Faction == playerFaction ? "Chapter fleet" : fleet.Faction.Name;
        int capacity = fleet.Ships.Sum(ship => ship.Template.SoldierCapacity);
        int loaded = fleet.Ships.Sum(ship => ship.LoadedSoldierCount);
        return $"TF {fleet.Id} | {ownership} | {fleet.Ships.Count} ships | {loaded}/{capacity} aboard";
    }
}
