using System;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Recruitment;
using OnlyWar.Helpers.Readiness;
namespace OnlyWar.Helpers.Battles { public static class BattleSquadReadinessExtensions {
        public static void RefreshDutyReadyParticipants(this BattleSquad squad, ChapterOperationalDoctrine doctrine = null, RecruitmentProgram program = null)
        {
            if (!squad.IsPlayerSquad) return;
            ChapterOperationalDoctrine resolvedDoctrine =
                doctrine;
            // A factory-built wrapper with an explicit participant set may be used by a detached
            // inspection/test model that has no live Chapter policy. In that case the factory has
            // already selected the participants; do not let the generic leader/minimum snapshot
            // reinterpret that set as a policy failure.
            if (resolvedDoctrine == null) return;
            if (squad.CampaignCharacter != null)
            {
                squad.RefreshEngagementParticipants(
                    DutyReadinessService.Evaluate(squad.CampaignCharacter, doctrine: resolvedDoctrine, recruitmentProgram: program).IsDutyReady
                        ? new[] { squad.CampaignCharacter }
                        : Array.Empty<ISoldier>());
                return;
            }

            SquadReadinessSnapshot readiness = SquadReadinessService.Evaluate(squad.Squad, program: program, doctrine: resolvedDoctrine);
            squad.RefreshEngagementParticipants(
                readiness.StructuralBlockers.Count == 0
                    ? DutyReadinessService.GetDutyReadyMembers(squad.Squad, doctrine: resolvedDoctrine, recruitmentProgram: program)
                        .Where(member => member is not PlayerSoldier player
                            || player.IndividualPosting == null)
                    : Array.Empty<ISoldier>());
        }

} }