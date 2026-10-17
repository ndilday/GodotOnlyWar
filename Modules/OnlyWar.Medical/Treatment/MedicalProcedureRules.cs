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

    /// <summary>
    /// Which price row a replacement of <paramref name="location"/> uses. Every organ replacement
    /// uses the "not severed" row, even for a severed Vitals
    /// (OnlyWar_TDD.md §6.6.1); a limb uses the severed row.
    /// </summary>
    public static bool UsesSeveredPricing(HitLocation location) =>
        location != null && !location.RequiresOrganReplacement && location.IsSevered;
}
