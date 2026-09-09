using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Medical.Treatment;

/// <summary>Authoritative procedure duration and requisition policy.</summary>
public static class MedicalProcedureRules
{
    public static int GetWeeks(MedicalProcedureType type, bool isSevered) =>
        type == MedicalProcedureType.Cybernetic ? 4 : isSevered ? 6 : 10;

    public static int GetRequisitionCost(MedicalProcedureType type, bool isSevered) =>
        type == MedicalProcedureType.Cybernetic
            ? isSevered ? 40 : 25
            : isSevered ? 95 : 70;
}

