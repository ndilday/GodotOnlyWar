using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Helpers.Fleets
{
    /// <summary>
    /// Whether a squad or a whole formation may be moved between ships, and the move itself.
    /// Two ships share a transfer location when they belong to the same task force, or to two
    /// task forces sitting in orbit at the same world; an administrative or absent squad has no
    /// berth to move from.
    /// </summary>
    public static class FleetTransferService
    {
        public static bool CanTransferSquadToShip(Squad squad, Ship destinationShip)
        {
            if (squad?.IsPresentOperationalForce != true
                || destinationShip == null
                || squad.BoardedLocation == null)
            {
                return false;
            }

            Ship sourceShip = squad.BoardedLocation;
            if (sourceShip == destinationShip)
            {
                return false;
            }

            if (destinationShip.AvailableCapacity < squad.Members.Count)
            {
                return false;
            }

            return ShipsShareTransferLocation(sourceShip, destinationShip);
        }

        public static void TransferSquadToShip(Squad squad, Ship destinationShip)
        {
            if (!CanTransferSquadToShip(squad, destinationShip))
            {
                return;
            }

            Ship sourceShip = squad.BoardedLocation;
            sourceShip.RemoveSquad(squad);
            destinationShip.LoadSquad(squad);
            squad.BoardedLocation = destinationShip;
            squad.CurrentRegion = null;
        }

        public static bool CanTransferUnitToShip(Unit unit, Ship sourceShip, Ship destinationShip)
        {
            if (unit == null || sourceShip == null || destinationShip == null
                || sourceShip == destinationShip
                || !ShipsShareTransferLocation(sourceShip, destinationShip))
            {
                return false;
            }

            List<Squad> squads = sourceShip.LoadedSquads
                .Where(squad => squad.ParentUnit == unit)
                .ToList();
            return squads.Count > 0
                && squads.All(squad => CanTransferSquadToShip(squad, destinationShip))
                && destinationShip.AvailableCapacity >= squads.Sum(squad => squad.Members.Count);
        }

        public static void TransferUnitToShip(Unit unit, Ship sourceShip, Ship destinationShip)
        {
            if (!CanTransferUnitToShip(unit, sourceShip, destinationShip))
            {
                return;
            }

            foreach (Squad squad in sourceShip.LoadedSquads
                .Where(squad => squad.ParentUnit == unit)
                .ToList())
            {
                TransferSquadToShip(squad, destinationShip);
            }
        }

        public static bool ShipsShareTransferLocation(Ship sourceShip, Ship destinationShip)
        {
            TaskForce sourceFleet = sourceShip?.Fleet;
            TaskForce destinationFleet = destinationShip?.Fleet;
            if (sourceFleet == null || destinationFleet == null)
            {
                return false;
            }

            if (sourceFleet == destinationFleet)
            {
                return true;
            }

            return sourceFleet.TravelPhase == FleetTravelPhase.InOrbit
                && destinationFleet.TravelPhase == FleetTravelPhase.InOrbit
                && sourceFleet.Planet != null
                && sourceFleet.Planet == destinationFleet.Planet;
        }
    }
}
