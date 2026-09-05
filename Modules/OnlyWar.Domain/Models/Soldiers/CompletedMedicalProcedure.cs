namespace OnlyWar.Models.Soldiers;

/// <summary>
/// Engine compatibility projection of a Medical contract completion onto the live player soldier.
/// The transition itself is applied by OnlyWar.Medical.
/// </summary>
public sealed record CompletedMedicalProcedure(
    PlayerSoldier Soldier,
    int PrimaryHitLocationTemplateId,
    string PrimaryHitLocationName,
    MedicalProcedureType ProcedureType,
    bool WasAlreadyCybernetic,
    int ProcedureDurationWeeks,
    int RequisitionCost);

