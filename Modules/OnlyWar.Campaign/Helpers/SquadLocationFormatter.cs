using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;

namespace OnlyWar.Campaign
{
    public static class SquadLocationFormatter
    {
        public static string Format(Squad squad)
        {
            if (squad == null)
            {
                return "Unknown";
            }

            CampaignLocation location = CampaignLocationService.ForSquad(squad);
            Region region = location?.Region;
            if (region != null)
            {
                return $"{region.Name}, {region.Planet.Name}";
            }

            Ship ship = location?.Ship;
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
