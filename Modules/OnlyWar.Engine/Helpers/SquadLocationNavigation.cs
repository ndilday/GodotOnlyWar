using OnlyWar.Models;
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
        CampaignLocation location = CampaignLocationService.ForSquad(squad);
        if (location?.Region != null)
        {
            return new SquadLocationNavigationTarget(
                SquadLocationNavigationKind.Region,
                squad,
                Region: location.Region);
        }

        Ship stationedShip = location?.Ship;
        if (stationedShip?.Fleet != null
            && stationedShip.Fleet.TravelPhase != FleetTravelPhase.InWarp)
        {
            return new SquadLocationNavigationTarget(
                SquadLocationNavigationKind.Ship,
                squad,
                Ship: stationedShip);
        }

        // A squad with no effective location is unlocated (for example a historical empty Scout).
        return null;
    }
}
