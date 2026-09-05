using System.Linq;
using OnlyWar.Models.Recruitment;
namespace OnlyWar.Helpers.Readiness;

internal static class ReadinessReservations
{
    internal static bool IsReserved(RecruitmentProgram program, int soldierId) =>
        program?.Procedures.Any(procedure =>
            procedure.AssignedApothecarySoldierId == soldierId
            || (procedure.Type == RecruitmentProcedureType.BlackCarapace
                && procedure.SubjectId == soldierId)) == true;
}
