using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
namespace OnlyWar.Helpers
{
    public sealed record ReplacementOption(
        int HitLocationId,
        MedicalProcedureType Type,
        string LocationName,
        string Title,
        string Description,
        int Weeks,
        int RequisitionCost,
        bool IsAvailable,
        // Per-requisite breakdown and overall assignability are filled in by the controller
        // via MedicalProcedureService once the soldier/force context is known; the builder
        // leaves them at their defaults.
        IReadOnlyList<ProcedureRequisite> Requisites = null,
        bool CanAssign = false);

    // A single met/unmet prerequisite for a procedure (PRD 4.8 presentation-of-requisites:
    // green when met, red when unmet).
    public sealed record ProcedureRequisite(string Label, bool IsMet);
}
