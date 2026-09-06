using OnlyWar.Models.Recruitment;
using System.Linq;

namespace OnlyWar.Helpers.Recruitment;

/// <summary>Small recruitment reservation query needed by order commitment policy.</summary>
public static class RecruitmentProcedureRules
{
    public static bool IsSoldierInBlackCarapaceProcedure(
        RecruitmentProgram program,
        int soldierId) =>
        program?.Procedures.Any(procedure =>
            procedure.Type == RecruitmentProcedureType.BlackCarapace
            && procedure.SubjectId == soldierId) == true;
}
