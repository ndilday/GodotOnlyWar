using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers
{
    // Centralized procedure week-counts and Requisition costs (PRD 4.8 / 5.3 second pass:
    // "costs live as centralized rules constants, not UI literals"). Cybernetic replacement
    // is the faster, cheaper option; vat-grown is the rarer, slower, more Requisition-intensive
    // one. A severed location is more involved than a merely crippled one.
    public static class MedicalProcedureRules
    {
        public static int GetWeeks(MedicalProcedureType type, bool isSevered)
        {
            return type == MedicalProcedureType.Cybernetic
                ? (isSevered ? 6 : 4)
                : (isSevered ? 14 : 10);
        }

        public static int GetRequisitionCost(MedicalProcedureType type, bool isSevered)
        {
            return type == MedicalProcedureType.Cybernetic
                ? (isSevered ? 40 : 25)
                : (isSevered ? 95 : 70);
        }
    }
}
