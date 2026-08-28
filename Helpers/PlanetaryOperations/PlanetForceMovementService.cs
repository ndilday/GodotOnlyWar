using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.PlanetaryOperations
{
    public enum ForceMovementKind
    {
        None,
        Landed,
        Embarked
    }

    public sealed record ForceMovementResult(
        bool Succeeded,
        string Message,
        ForceMovementKind Kind = ForceMovementKind.None,
        int SquadCount = 0,
        int PassengerCount = 0,
        int OrdersEnded = 0,
        int SpecialistsReleased = 0);

    public sealed record ShipCapacityChoice(
        Ship Ship,
        int CurrentPassengers,
        int SelectedPassengers,
        int ResultingPassengers,
        int Capacity,
        int Shortfall)
    {
        public bool Fits => Shortfall == 0;
    }

    /// <summary>
    /// Atomic whole-squad landing and embarkation for Planetary Operations. Every live-state and
    /// capacity check completes before the first relationship or order is mutated.
    /// </summary>
    public static class PlanetForceMovementService
    {
        public static IReadOnlyList<ShipCapacityChoice> BuildCapacityChoices(
            Planet planet,
            Faction playerFaction,
            IReadOnlyList<Squad> selectedSquads)
        {
            int passengers = (selectedSquads ?? [])
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .Sum(SoldierPresenceService.PresentCount);
            return GetOrbitingPlayerShips(planet, playerFaction)
                .Select(ship =>
                {
                    int current = ShipCapacityService.LoadedSoldierCount(ship);
                    int capacity = ship.Template?.SoldierCapacity ?? 0;
                    return new ShipCapacityChoice(
                        ship,
                        current,
                        passengers,
                        current + passengers,
                        capacity,
                        System.Math.Max(0, current + passengers - capacity));
                })
                .ToList();
        }

        public static ForceMovementResult Land(
            Sector sector,
            Planet planet,
            Region destination,
            IReadOnlyList<Squad> selectedSquads)
        {
            List<Squad> squads = Distinct(selectedSquads);
            if (sector?.PlayerForce?.Faction == null
                || planet == null
                || destination?.Planet != planet
                || squads.Count == 0)
            {
                return Failure("Select orbiting squads and a valid destination region.");
            }

            Faction playerFaction = sector.PlayerForce.Faction;
            HashSet<Ship> validShips = GetOrbitingPlayerShips(planet, playerFaction).ToHashSet();
            if (squads.Any(squad =>
                    squad.Faction != playerFaction
                    || !squad.IsOperational
                    || squad.CurrentRegion != null
                    || squad.CurrentOrders != null
                    || squad.BoardedLocation == null
                    || !validShips.Contains(squad.BoardedLocation)
                    || !squad.BoardedLocation.LoadedSquads.Contains(squad)))
            {
                return Failure("The force changed and at least one selected squad can no longer land.");
            }

            RegionFaction destinationPresence = GetOrCreatePlayerPresence(
                destination, playerFaction);
            if (destinationPresence == null)
            {
                return Failure("The Chapter has no valid planetary presence on this world.");
            }

            int passengers = squads.Sum(SoldierPresenceService.PresentCount);
            foreach (Squad squad in squads)
            {
                squad.BoardedLocation.RemoveSquad(squad);
                squad.BoardedLocation = null;
                squad.CurrentRegion = destination;
                if (!destinationPresence.LandedSquads.Contains(squad))
                {
                    destinationPresence.LandedSquads.Add(squad);
                }
            }

            return new ForceMovementResult(
                true,
                squads.Count == 1 ? "Squad landed." : $"{squads.Count} squads landed.",
                ForceMovementKind.Landed,
                squads.Count,
                passengers);
        }

        public static ForceMovementResult Embark(
            Sector sector,
            Planet planet,
            Region source,
            Ship destinationShip,
            IReadOnlyList<Squad> selectedSquads)
        {
            List<Squad> squads = Distinct(selectedSquads);
            if (sector?.PlayerForce?.Faction == null
                || planet == null
                || source?.Planet != planet
                || destinationShip == null
                || squads.Count == 0)
            {
                return Failure("Select surface squads and a destination ship.");
            }

            Faction playerFaction = sector.PlayerForce.Faction;
            if (!GetOrbitingPlayerShips(planet, playerFaction).Contains(destinationShip)
                || !source.RegionFactionMap.TryGetValue(
                    playerFaction.Id, out RegionFaction sourcePresence)
                || squads.Any(squad =>
                    squad.Faction != playerFaction
                    || !squad.IsOperational
                    || !ReferenceEquals(squad.CurrentRegion, source)
                    || squad.BoardedLocation != null
                    || !sourcePresence.LandedSquads.Contains(squad)))
            {
                return Failure("The force changed and at least one selected squad can no longer embark.");
            }

            int passengers = squads.Sum(SoldierPresenceService.PresentCount);
            if (!ShipCapacityService.CanBoard(destinationShip, passengers))
            {
                int shortfall = System.Math.Max(
                    0, passengers - ShipCapacityService.AvailableCapacity(destinationShip));
                return Failure($"{destinationShip.Name} is short {shortfall} passenger spaces.");
            }

            List<Order> affectedOrders = squads
                .Select(squad => squad.CurrentOrders)
                .Where(order => order != null)
                .Distinct()
                .ToList();
            int ordersEnded = affectedOrders.Count(order =>
                order.AssignedSquads.All(squads.Contains));
            int specialistsReleased = affectedOrders
                .Where(order => order.AssignedSquads.All(squads.Contains))
                .Sum(order => order.AttachedSoldiers.Count);

            // All validations are complete. Order cleanup cannot fail for these live squad/order
            // pointer pairs, and loading cannot throw after the capacity check above.
            OrderAssignment.UnassignSquads(squads);
            foreach (Squad squad in squads)
            {
                sourcePresence.LandedSquads.Remove(squad);
                destinationShip.LoadSquad(squad);
                squad.CurrentRegion = null;
                squad.BoardedLocation = destinationShip;
            }
            CleanupVacatedPresence(source, sourcePresence);

            return new ForceMovementResult(
                true,
                squads.Count == 1 ? "Squad embarked." : $"{squads.Count} squads embarked.",
                ForceMovementKind.Embarked,
                squads.Count,
                passengers,
                ordersEnded,
                specialistsReleased);
        }

        public static IReadOnlyList<Ship> GetOrbitingPlayerShips(
            Planet planet,
            Faction playerFaction) =>
            planet?.OrbitingTaskForceList
                .Where(fleet => fleet?.Faction == playerFaction
                    && fleet.Planet == planet
                    && fleet.TravelPhase == FleetTravelPhase.InOrbit)
                .SelectMany(fleet => fleet.Ships)
                .Where(ship => ship != null)
                .OrderBy(ship => ship.Name)
                .ThenBy(ship => ship.Id)
                .ToList() ?? [];

        private static List<Squad> Distinct(IReadOnlyList<Squad> squads) =>
            (squads ?? [])
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .ToList();

        private static RegionFaction GetOrCreatePlayerPresence(
            Region region,
            Faction playerFaction)
        {
            if (!region.Planet.PlanetFactionMap.TryGetValue(
                    playerFaction.Id, out PlanetFaction planetPresence))
            {
                planetPresence = new PlanetFaction(playerFaction) { IsPublic = true };
                region.Planet.PlanetFactionMap[playerFaction.Id] = planetPresence;
            }
            if (!region.RegionFactionMap.TryGetValue(
                    playerFaction.Id, out RegionFaction regionPresence))
            {
                regionPresence = new RegionFaction(planetPresence, region) { IsPublic = true };
                region.RegionFactionMap[playerFaction.Id] = regionPresence;
            }
            regionPresence.IsPublic = true;
            return regionPresence;
        }

        private static void CleanupVacatedPresence(
            Region region,
            RegionFaction presence)
        {
            if (presence.LandedSquads.Count > 0) return;
            RegionDefenses.TransferToAlly(presence);
            if (!RegionDefenses.HasAnyWorks(presence))
            {
                region.RegionFactionMap.Remove(presence.PlanetFaction.Faction.Id);
            }
            else
            {
                presence.IsPublic = false;
            }
        }

        private static ForceMovementResult Failure(string message) =>
            new(false, message);
    }
}
