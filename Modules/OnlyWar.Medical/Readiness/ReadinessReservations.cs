using System.Linq;
using OnlyWar.Domain.Recruitment;
namespace OnlyWar.Medical.Readiness;

public static class ReadinessReservations
{
    public static bool IsReserved(RecruitmentProgram program, int soldierId) =>
        program?.Procedures.Any(procedure =>
            procedure.AssignedApothecarySoldierId == soldierId
            || (procedure.Type == RecruitmentProcedureType.BlackCarapace
                && procedure.SubjectId == soldierId)) == true;
}
