using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using OnlyWar.Operations.Contracts;
using OnlyWar.Operations.Personnel;
using System.Linq;

namespace OnlyWar.Helpers
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

        public static bool CanLoadSquad(
            Ship ship,
            Squad squad,
            IPersonnelAvailabilityQueries personnel) =>
            CanBoard(ship, Require(personnel).PresentCount(
                PersonnelAvailabilityProjection.ForFormation(squad)), personnel);

        private static IPersonnelAvailabilityQueries Require(
            IPersonnelAvailabilityQueries personnel) =>
            personnel ?? throw new System.ArgumentNullException(nameof(personnel));
    }
}
