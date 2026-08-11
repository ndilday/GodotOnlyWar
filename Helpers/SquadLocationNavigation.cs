using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers;

public enum SquadLocationNavigationKind
{
    Unavailable,
    Ship,
    Region
}

public sealed record SquadLocationNavigationTarget(
    SquadLocationNavigationKind Kind,
    Squad Squad,
    Ship Ship = null,
    Region Region = null);

public static class SquadLocationNavigation
{
    public static SquadLocationNavigationTarget Resolve(Squad squad)
    {
        if (squad?.CurrentRegion != null)
        {
            return new SquadLocationNavigationTarget(
                SquadLocationNavigationKind.Region,
                squad,
                Region: squad.CurrentRegion);
        }

        Ship ship = squad?.BoardedLocation;
        if (ship?.Fleet != null && ship.Fleet.TravelPhase != FleetTravelPhase.InWarp)
        {
            return new SquadLocationNavigationTarget(
                SquadLocationNavigationKind.Ship,
                squad,
                Ship: ship);
        }

        return null;
    }
}
