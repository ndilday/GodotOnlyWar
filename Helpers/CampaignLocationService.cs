using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers
{
    public static class CampaignLocationService
    {
        public static CampaignLocation ForSquad(Squad squad)
        {
            if (squad?.BoardedLocation != null)
            {
                return CampaignLocation.Aboard(squad.BoardedLocation);
            }
            return CampaignLocation.Landed(squad?.CurrentRegion);
        }

        public static CampaignLocation ForSoldier(PlayerSoldier soldier) =>
            soldier?.IndividualPosting?.Location ?? ForSquad(soldier?.AssignedSquad);

        public static bool AreCoLocated(PlayerSoldier soldier, Squad squad) =>
            ForSoldier(soldier)?.IsSamePlace(ForSquad(squad)) == true;

        public static bool AreCoLocated(PlayerSoldier first, PlayerSoldier second) =>
            ForSoldier(first)?.IsSamePlace(ForSoldier(second)) == true;

        public static string Format(CampaignLocation location) =>
            location?.Ship != null
                ? $"Aboard {location.Ship.Name}"
                : location?.Region != null
                    ? $"{location.Region.Planet?.Name} / {location.Region.Name}"
                    : "No operational location";
    }
}
