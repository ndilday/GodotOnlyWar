namespace OnlyWar.Models.Soldiers;

public enum MedicalProcedureType
{
    Cybernetic,
    VatGrown
}

/// <summary>
/// Persisted medical work in progress.  This is a neutral campaign value: policy belongs to
/// Medical and association with a live soldier belongs to the application/campaign adapter.
/// </summary>
public sealed class MedicalProcedure
{
    public int SoldierId { get; set; }
    public int HitLocationTemplateId { get; set; }
    public MedicalProcedureType ProcedureType { get; set; }
    public int WeeksRemaining { get; set; }
    public int RequisitionCost { get; set; }

    public MedicalProcedure() { }

    public MedicalProcedure(
        int soldierId,
        int hitLocationTemplateId,
        MedicalProcedureType procedureType,
        int weeksRemaining,
        int requisitionCost)
    {
        SoldierId = soldierId;
        HitLocationTemplateId = hitLocationTemplateId;
        ProcedureType = procedureType;
        WeeksRemaining = weeksRemaining;
        RequisitionCost = requisitionCost;
    }
}

