using OnlyWar.Models.Soldiers;
using System.Collections.Generic;

namespace OnlyWar.Helpers
{
    public enum ApothecariumSelectionKind
    {
        Vault = 0,
        Unit = 1,
        Squad = 2,
        Soldier = 3
    }

    public enum MedicalSeverity
    {
        None = 0,
        Stable = 1,
        Watch = 2,
        Serious = 3,
        Critical = 4,
        Lost = 5
    }

    public sealed record ApothecariumTreeItem(
        ApothecariumSelectionKind Kind,
        int Id,
        string IconKey,
        string Title,
        string Subtitle,
        string Status,
        MedicalSeverity Severity,
        bool IsSelected,
        IReadOnlyList<ApothecariumTreeItem> Children);

    public sealed record ApothecariumSelection(
        ApothecariumSelectionKind Kind,
        int Id);

    public sealed record GeneSeedVaultSummary(
        int Stockpile,
        int MatureImplanted,
        int ImmatureImplanted,
        int MaturingWithinOneYear,
        int AtRiskImplanted,
        string PurityStatus,
        IReadOnlyList<GeneSeedVaultRow> Rows,
        IReadOnlyList<GeneSeedFormationSummary> FormationSummaries,
        // The chapter's current Requisition balance (PRD 4.23), surfaced on the vault panel.
        int Requisition = 0,
        // The count-weighted aggregate purity (0..1) of the sealed stockpile (PRD 4.8).
        float AggregatePurity = 1.0f,
        MedicalSeverity PuritySeverity = MedicalSeverity.Stable);

    public sealed record GeneSeedVaultRow(
        string Title,
        string Subtitle,
        string Value,
        MedicalSeverity Severity);

    public sealed record GeneSeedFormationSummary(
        string Formation,
        int MatureImplanted,
        int ImmatureImplanted,
        int AtRisk,
        string PurityStatus,
        MedicalSeverity Severity);

    public sealed record MedicalUnitSummary(
        ApothecariumSelectionKind Kind,
        int Id,
        string IconKey,
        string Title,
        string Subtitle,
        int HealthyCount,
        int WoundedCount,
        int OutOfActionCount,
        int ReadyNextCount,
        int MaxRecoveryWeeks,
        IReadOnlyList<MedicalSeriousWoundRow> SeriousWounds);

    public sealed record MedicalSeriousWoundRow(
        int SoldierId,
        string SoldierName,
        string Wound,
        string OutOfAction,
        string Recommendation,
        MedicalSeverity Severity);

    public sealed record MedicalSoldierSummary(
        int SoldierId,
        string IconKey,
        string Name,
        string Assignment,
        bool CanFight,
        int MaxRecoveryWeeks,
        string GeneSeedStatus,
        MedicalSeverity WorstSeverity,
        IReadOnlyList<WoundLocationSummary> Wounds,
        IReadOnlyList<ReplacementOption> ReplacementOptions);

    public sealed record WoundLocationSummary(
        int HitLocationId,
        string LocationName,
        string Status,
        string Recovery,
        bool HoldsProgenoid,
        bool IsCybernetic,
        bool NeedsReplacement,
        MedicalSeverity Severity);

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
