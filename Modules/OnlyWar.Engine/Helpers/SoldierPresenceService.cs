using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public static class SoldierPresenceService
    {
        public static IReadOnlyList<ISoldier> NominalMembers(Squad squad) =>
            squad?.Members?.ToList() ?? [];

        public static IReadOnlyList<ISoldier> PresentMembers(Squad squad) =>
            squad?.Members?.Where(member =>
                member is not PlayerSoldier player || player.IndividualPosting == null).ToList() ?? [];

        public static IReadOnlyList<ISoldier> DeployableMembers(Squad squad) =>
            PresentMembers(squad)
                .Where(member => DutyReadinessService.Evaluate(member, doctrine: CurrentCampaignReadinessContext.ResolveDoctrine(squad), recruitmentProgram: CurrentCampaignReadinessContext.ResolveProgram(squad)).IsDutyReady)
                .ToList();

        public static IReadOnlyList<PlayerSoldier> OrderParticipants(Order order)
        {
            if (order == null) return [];
            return order.Force.AllPlayerSoldiers.Distinct().ToList();
        }

        public static int NominalCount(Squad squad) =>
            SquadStrengthSnapshotBuilder.Build(squad, program: CurrentCampaignReadinessContext.ResolveProgram(squad), doctrine: CurrentCampaignReadinessContext.ResolveDoctrine(squad)).Rostered;

        public static int PresentCount(Squad squad) =>
            SquadStrengthSnapshotBuilder.Build(squad, program: CurrentCampaignReadinessContext.ResolveProgram(squad), doctrine: CurrentCampaignReadinessContext.ResolveDoctrine(squad)).Present;

        public static int DeployableCount(Squad squad) =>
            SquadStrengthSnapshotBuilder.Build(squad, program: CurrentCampaignReadinessContext.ResolveProgram(squad), doctrine: CurrentCampaignReadinessContext.ResolveDoctrine(squad)).DutyReady;

    }
}
