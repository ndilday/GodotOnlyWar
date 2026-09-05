using System.Linq;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Fleets;
namespace OnlyWar.Models;
public static class PhysicalPresence
{
    public static int PresentCount(Squad squad) => squad?.Members.Count(
        member => member is not PlayerSoldier player || player.IndividualPosting == null) ?? 0;
    public static int LoadedSoldierCount(Ship ship) => ship == null ? 0 :
        ship.LoadedSquads.Sum(PresentCount) + ship.AdministrativeStations.Sum(PresentCount)
        + ship.IndividuallyBoardedSoldiers.Count;
    public static CampaignLocation ForSquad(Squad squad) =>
        squad?.PermitsIndividualDeployment == true && squad.DutyStation != null ? squad.DutyStation :
        squad?.BoardedLocation != null ? CampaignLocation.Aboard(squad.BoardedLocation) :
        CampaignLocation.Landed(squad?.CurrentRegion);
    public static CampaignLocation ForSoldier(PlayerSoldier soldier) =>
        soldier?.IndividualPosting?.Location ?? ForSquad(soldier?.AssignedSquad);
}