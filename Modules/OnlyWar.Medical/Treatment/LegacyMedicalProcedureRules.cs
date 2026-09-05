using OnlyWar.Models.Soldiers;
using MedicalRules = OnlyWar.Medical.Treatment.MedicalProcedureRules;

namespace OnlyWar.Helpers
{
    // Compatibility façade. Procedure policy is implemented in OnlyWar.Medical; this namespace
    // remains only while application and host callers migrate.
    public static class MedicalProcedureRules
    {
        public static int GetWeeks(MedicalProcedureType type, bool isSevered)
        {
            return MedicalRules.GetWeeks(type, isSevered);
        }

        public static int GetRequisitionCost(MedicalProcedureType type, bool isSevered)
        {
            return MedicalRules.GetRequisitionCost(type, isSevered);
        }
    }
}
