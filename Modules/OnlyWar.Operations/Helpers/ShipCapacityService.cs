using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using OnlyWar.Contracts.Operations;
using System.Linq;

namespace OnlyWar.Helpers
{
    public static class ShipCapacityService
    {
        public static int AdministrativeStationedSoldierCount(Ship ship) => ship == null
            ? 0
                : ship.AdministrativeStations
                .Sum(squad => OperationsPersonnelDefaults.Current.PresentCount(squad));

        public static int LoadedSoldierCount(Ship ship) => ship == null
            ? 0
            : ship.LoadedSquads.Sum(squad => OperationsPersonnelDefaults.Current.PresentCount(squad))
                + AdministrativeStationedSoldierCount(ship)
                + ship.IndividuallyBoardedSoldiers.Count;

        public static int AvailableCapacity(Ship ship) =>
            ship?.Template == null ? 0 : ship.Template.SoldierCapacity - LoadedSoldierCount(ship);

        public static bool CanBoard(Ship ship, int passengers) =>
            ship != null && passengers >= 0 && AvailableCapacity(ship) >= passengers;

        public static bool CanLoadSquad(Ship ship, Squad squad) =>
            CanBoard(ship, OperationsPersonnelDefaults.Current.PresentCount(squad));
    }
}
