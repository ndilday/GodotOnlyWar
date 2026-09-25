using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign
{
    public static class SoldierPresenceService
    {
        public static IReadOnlyList<ISoldier> PresentMembers(Squad squad) =>
            squad?.Members?.Where(member =>
                member is not PlayerSoldier player || player.IndividualPosting == null).ToList() ?? [];

        public static int NominalCount(Squad squad) =>
            squad?.Members?.Count ?? 0;

        public static int PresentCount(Squad squad) =>
            PresentMembers(squad).Count;
    }
}
