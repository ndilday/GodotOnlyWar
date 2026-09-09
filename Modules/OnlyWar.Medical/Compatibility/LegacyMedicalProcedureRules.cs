using OnlyWar.Domain.Soldiers;
using MedicalRules = OnlyWar.Medical.Treatment.MedicalProcedureRules;

namespace OnlyWar.Helpers;

/// <summary>
/// Compatibility façade for the historical helper API. New code should use
/// <see cref="OnlyWar.Medical.Treatment.MedicalProcedureRules"/> directly.
/// </summary>
public static class MedicalProcedureRules
{
    public static int GetWeeks(MedicalProcedureType type, bool isSevered) =>
        MedicalRules.GetWeeks(type, isSevered);

    public static int GetRequisitionCost(MedicalProcedureType type, bool isSevered) =>
        MedicalRules.GetRequisitionCost(type, isSevered);
}
