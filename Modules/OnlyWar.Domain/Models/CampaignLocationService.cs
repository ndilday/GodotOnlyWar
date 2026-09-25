using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;

namespace OnlyWar.Domain
{
    public static class CampaignLocationService
    {
        public static CampaignLocation ForSquad(Squad squad)
        {
            if (squad?.PermitsIndividualDeployment == true && squad.DutyStation != null)
            {
                return squad.DutyStation;
            }
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
            location?.Ship?.Name
                ?? location?.Region?.Name
                ?? "No operational location";
    }
}
