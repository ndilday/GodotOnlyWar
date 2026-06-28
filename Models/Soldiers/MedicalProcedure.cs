namespace OnlyWar.Models.Soldiers
{
    public enum MedicalProcedureType
    {
        // Faster, cheaper: an augmetic replacement (sets HitLocation.IsCybernetic on completion).
        Cybernetic,
        // Rarer, slower, more Requisition-intensive: a vat-grown organic replacement
        // (restores the location without marking it cybernetic).
        VatGrown
    }

    // A medical procedure in progress in the Apothecarium (PRD 4.8 / 5.3). Tracked on the
    // chapter's Army; its Requisition cost is paid up front at assignment, so a mid-procedure
    // save needs no reconciliation. Staff are gated by co-location at the start of the
    // procedure and are not committed for its duration, so no staff are recorded here.
    public sealed class MedicalProcedure
    {
        public int SoldierId { get; set; }
        public int HitLocationTemplateId { get; set; }
        public MedicalProcedureType ProcedureType { get; set; }
        public int WeeksRemaining { get; set; }
        public int RequisitionCost { get; set; }

        public MedicalProcedure() { }

        public MedicalProcedure(int soldierId, int hitLocationTemplateId,
                                MedicalProcedureType procedureType, int weeksRemaining,
                                int requisitionCost)
        {
            SoldierId = soldierId;
            HitLocationTemplateId = hitLocationTemplateId;
            ProcedureType = procedureType;
            WeeksRemaining = weeksRemaining;
            RequisitionCost = requisitionCost;
        }
    }
}
