using System;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Soldiers.Ratings;
using OnlyWar.Models.Squads;

namespace OnlyWar.Helpers.Recruitment
{
    /// <summary>
    /// Projects the members of the 10th Company administrative HQ into the staffing
    /// values used by the recruitment simulation. The Chapter screen remains the sole
    /// reassignment UI: moving an eligible brother into or out of that squad changes the
    /// program the next time it is previewed or processed.
    /// </summary>
    internal sealed class RecruitmentStaffService
    {
        internal Squad GetAdministrativeSquad(PlayerForce force)
        {
            return force?.Army?.OrderOfBattle?.GetAllSquads()
                .SingleOrDefault(squad => squad.IsAdministrative);
        }

        internal void Synchronize(PlayerForce force, GameRulesData rules)
        {
            RecruitmentProgram program = force?.RecruitmentProgram;
            if (program == null)
            {
                return;
            }

            program.StaffAssignments.Clear();
            Squad administrative = GetAdministrativeSquad(force);
            if (administrative == null)
            {
                return;
            }

            foreach (PlayerSoldier soldier in administrative.Members.OfType<PlayerSoldier>())
            {
                RecruitmentStaffRole? role = ResolveRole(soldier, rules);
                if (!role.HasValue)
                {
                    // The Captain is the Master of Recruitment, but is not one of the
                    // throughput-producing staff posts charged by the weekly program.
                    continue;
                }

                SoldierEvaluation evaluation = soldier.SoldierEvaluationHistory.LastOrDefault();
                program.StaffAssignments.Add(new RecruitmentStaffAssignment(
                    soldier.Id,
                    role.Value,
                    evaluation?[RatingKeys.Leadership] ?? 0,
                    evaluation?[RatingKeys.Medical] ?? 0,
                    evaluation?[RatingKeys.Piety] ?? 0));
            }
        }

        private static RecruitmentStaffRole? ResolveRole(
            PlayerSoldier soldier,
            GameRulesData rules)
        {
            if (soldier?.Template == null || rules?.ChapterTemplates == null)
            {
                return null;
            }

            if (soldier.Template == rules.ChapterTemplates.ScoutSergeant)
            {
                return RecruitmentStaffRole.ScoutSergeant;
            }
            if (soldier.Template == rules.ChapterTemplates.Apothecary)
            {
                return RecruitmentStaffRole.Apothecary;
            }
            if (soldier.Template == rules.ChapterTemplates.Chaplain
                || soldier.Template == rules.ChapterTemplates.Judiciar)
            {
                return RecruitmentStaffRole.Chaplain;
            }

            return null;
        }
    }
}
