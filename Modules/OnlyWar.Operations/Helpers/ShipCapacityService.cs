using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Squads;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Personnel;
using System.Linq;

namespace OnlyWar.Operations.Fleets
{
    public static class ShipCapacityService
    {
        public static int AdministrativeStationedSoldierCount(
            Ship ship,
            IPersonnelAvailabilityQueries personnel) => ship == null
            ? 0
                : ship.AdministrativeStations
                .Sum(squad => Require(personnel).PresentCount(
                    PersonnelAvailabilityProjection.ForFormation(squad)));

        public static int LoadedSoldierCount(
            Ship ship,
            IPersonnelAvailabilityQueries personnel) => ship == null
            ? 0
            : ship.LoadedSquads.Sum(squad => Require(personnel).PresentCount(
                    PersonnelAvailabilityProjection.ForFormation(squad)))
                + AdministrativeStationedSoldierCount(ship, personnel)
                + ship.IndividuallyBoardedSoldiers.Count;

        public static int AvailableCapacity(
            Ship ship,
            IPersonnelAvailabilityQueries personnel) =>
            ship?.Template == null ? 0 : ship.Template.SoldierCapacity - LoadedSoldierCount(ship, personnel);

        public static bool CanBoard(
            Ship ship,
            int passengers,
            IPersonnelAvailabilityQueries personnel) =>
            ship != null && passengers >= 0 && AvailableCapacity(ship, personnel) >= passengers;

        private static IPersonnelAvailabilityQueries Require(
            IPersonnelAvailabilityQueries personnel) =>
            personnel ?? throw new System.ArgumentNullException(nameof(personnel));
    }
}
