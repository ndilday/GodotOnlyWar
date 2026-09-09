using OnlyWar.Medical.Readiness;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign
{
    public static class SoldierPresenceService
    {
        public static IReadOnlyList<ISoldier> NominalMembers(Squad squad) =>
            squad?.Members?.ToList() ?? [];

        public static IReadOnlyList<ISoldier> PresentMembers(Squad squad) =>
            squad?.Members?.Where(member =>
                member is not PlayerSoldier player || player.IndividualPosting == null).ToList() ?? [];

        public static IReadOnlyList<ISoldier> DeployableMembers(
            Squad squad,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null) =>
            PresentMembers(squad)
                .Where(member => DutyReadinessService.Evaluate(
                    member, doctrine: doctrine, recruitmentProgram: program).IsDutyReady)
                .ToList();

        public static IReadOnlyList<PlayerSoldier> OrderParticipants(Order order)
        {
            if (order == null) return [];
            return order.Force.AllPlayerSoldiers.Distinct().ToList();
        }

        public static int NominalCount(Squad squad) =>
            squad?.Members?.Count ?? 0;

        public static int PresentCount(Squad squad) =>
            PresentMembers(squad).Count;

        public static int DeployableCount(
            Squad squad,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null) =>
            DeployableMembers(squad, program, doctrine).Count;

    }
}
