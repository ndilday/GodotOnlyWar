using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers
{
    public static class SquadLocationFormatter
    {
        public static string Format(Squad squad)
        {
            if (squad == null)
            {
                return "Unknown";
            }

            Region region = squad.CurrentRegion;
            if (region != null)
            {
                return $"{region.Name}, {region.Planet.Name}";
            }

            Ship ship = squad.BoardedLocation;
            if (ship == null)
            {
                return "Unknown";
            }

            TaskForce fleet = ship.Fleet;
            if (fleet?.TravelPhase != FleetTravelPhase.InOrbit || fleet.Planet == null)
            {
                return $"{ship.Name}, in transit";
            }

            return $"{ship.Name}, orbiting {fleet.Planet.Name}";
        }
    }
}
