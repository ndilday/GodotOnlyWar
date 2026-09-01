using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.UI;
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
                .Where(member => SquadStrengthSnapshotBuilder.IsCombatEffectiveMember(
                    member, CurrentProgram))
                .ToList();

        public static IReadOnlyList<PlayerSoldier> OrderParticipants(Order order)
        {
            if (order == null) return [];
            return order.Force.AllPlayerSoldiers.Distinct().ToList();
        }

        public static int NominalCount(Squad squad) =>
            SquadStrengthSnapshotBuilder.Build(squad, CurrentProgram).Rostered;

        public static int PresentCount(Squad squad) =>
            SquadStrengthSnapshotBuilder.Build(squad, CurrentProgram).Present;

        public static int DeployableCount(Squad squad) =>
            SquadStrengthSnapshotBuilder.Build(squad, CurrentProgram).Effective;

        private static OnlyWar.Models.Recruitment.RecruitmentProgram CurrentProgram =>
            GameDataSingleton.Instance?.Sector?.PlayerForce?.RecruitmentProgram;
    }
}
