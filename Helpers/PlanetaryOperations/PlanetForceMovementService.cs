using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
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
            => BuildCapacityChoices(
                planet,
                playerFaction,
                new MovementParty(selectedSquads ?? [], []));

        public static IReadOnlyList<ShipCapacityChoice> BuildCapacityChoices(
            Planet planet,
            Faction playerFaction,
            MovementParty party)
        {
            int passengers = Distinct(party).Squads
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .Sum(SoldierPresenceService.PresentCount)
                + Distinct(party).Characters.Count;
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
            return Land(sector, planet, destination,
                new MovementParty(selectedSquads ?? [], []));
        }

        public static ForceMovementResult Land(
            Sector sector,
            Planet planet,
            Region destination,
            MovementParty party)
        {
            MovementParty distinctParty = Distinct(party);
            List<Squad> squads = distinctParty.Squads.ToList();
            List<PlayerSoldier> characters = distinctParty.Characters.ToList();
            if (sector?.PlayerForce?.Faction == null
                || planet == null
                || destination?.Planet != planet
                || squads.Count == 0 && characters.Count == 0)
            {
                return Failure("Select orbiting squads or characters and a valid destination region.");
            }

            Faction playerFaction = sector.PlayerForce.Faction;
            HashSet<Ship> validShips = GetOrbitingPlayerShips(planet, playerFaction).ToHashSet();
            if (squads.Any(squad =>
                    squad.Faction != playerFaction
                    || !squad.CanMoveAsFormation
                    || squad.CurrentRegion != null
                    || squad.CurrentOrders != null
                    || squad.BoardedLocation == null
                    || !validShips.Contains(squad.BoardedLocation)
                    || !squad.BoardedLocation.LoadedSquads.Contains(squad))
                || characters.Any(character =>
                    squads.Any(squad => squad.Members.Contains(character))
                    || character.AssignedSquad?.Faction != playerFaction
                    || !validShips.Contains(CampaignLocationService.ForSoldier(character)?.Ship)
                    || !new CharacterAvailabilityService().EvaluateMovement(
                        character, CampaignLocation.Landed(destination)).IsAllowed))
            {
                return Failure("The force changed and at least one selected participant can no longer land.");
            }

            RegionFaction destinationPresence = GetOrCreatePlayerPresence(
                destination, playerFaction);
            if (destinationPresence == null)
            {
                return Failure("The Chapter has no valid planetary presence on this world.");
            }

            int passengers = squads.Sum(SoldierPresenceService.PresentCount)
                + characters.Count;
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

            IndividualPostingService postings = new();
            foreach (PlayerSoldier character in characters)
            {
                postings.RestorePhysical(
                    character,
                    IndividualPostingPurpose.Independent,
                    CampaignLocation.Landed(destination),
                    GameDataSingleton.Instance?.Date ?? new Date(1));
            }

            return new ForceMovementResult(
                true,
                Describe("landed", squads.Count, characters.Count),
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
            return Embark(sector, planet, source, destinationShip,
                new MovementParty(selectedSquads ?? [], []));
        }

        public static ForceMovementResult Embark(
            Sector sector,
            Planet planet,
            Region source,
            Ship destinationShip,
            MovementParty party)
        {
            MovementParty distinctParty = Distinct(party);
            List<Squad> squads = distinctParty.Squads.ToList();
            List<PlayerSoldier> characters = distinctParty.Characters.ToList();
            if (sector?.PlayerForce?.Faction == null
                || planet == null
                || source?.Planet != planet
                || destinationShip == null
                || squads.Count == 0 && characters.Count == 0)
            {
                return Failure("Select surface squads or characters and a destination ship.");
            }

            Faction playerFaction = sector.PlayerForce.Faction;
            if (!GetOrbitingPlayerShips(planet, playerFaction).Contains(destinationShip)
                || !source.RegionFactionMap.TryGetValue(
                    playerFaction.Id, out RegionFaction sourcePresence)
                || squads.Any(squad =>
                    squad.Faction != playerFaction
                    || !squad.CanMoveAsFormation
                    || !ReferenceEquals(squad.CurrentRegion, source)
                    || squad.BoardedLocation != null
                    || !sourcePresence.LandedSquads.Contains(squad))
                || characters.Any(character =>
                    squads.Any(squad => squad.Members.Contains(character))
                    || character.AssignedSquad?.Faction != playerFaction
                    || !new CharacterAvailabilityService().EvaluateMovement(
                        character, CampaignLocation.Aboard(destinationShip)).IsAllowed
                    || CampaignLocationService.ForSoldier(character)?.Region != source))
            {
                return Failure("The force changed and at least one selected participant can no longer embark.");
            }

            int passengers = squads.Sum(SoldierPresenceService.PresentCount) + characters.Count;
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
                .Sum(order => order.AssignedCharacters.Count);

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
            IndividualPostingService postings = new();
            foreach (PlayerSoldier character in characters)
            {
                postings.RestorePhysical(
                    character,
                    IndividualPostingPurpose.Independent,
                    CampaignLocation.Aboard(destinationShip),
                    GameDataSingleton.Instance?.Date ?? new Date(1));
            }
            CleanupVacatedPresence(source, sourcePresence);

            return new ForceMovementResult(
                true,
                Describe("embarked", squads.Count, characters.Count),
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

        private static MovementParty Distinct(MovementParty party) =>
            new(
                (party?.Squads ?? [])
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .ToList(),
                (party?.Characters ?? [])
                .Where(character => character != null)
                .DistinctBy(character => character.Id)
                .ToList());

        private static string Describe(string verb, int squadCount, int characterCount)
        {
            List<string> parts = [];
            if (squadCount > 0) parts.Add($"{squadCount} squad{(squadCount == 1 ? "" : "s")}");
            if (characterCount > 0) parts.Add($"{characterCount} character{(characterCount == 1 ? "" : "s")}");
            return string.Join(" and ", parts) + $" {verb}.";
        }

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
