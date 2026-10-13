using OnlyWar.Medical.Readiness;
using System.Collections.Generic;

namespace OnlyWar.Application
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
        IReadOnlyList<ApothecariumTreeItem> Children,
        SquadRowViewModel SquadRow = null);

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
        MedicalSeverity PuritySeverity = MedicalSeverity.Stable,
        // The chapter's cybernetic repair queue, in the order the Techmarines work it.
        IReadOnlyList<CyberneticRepairQueueRow> RepairQueue = null)
    {
        public IReadOnlyList<CyberneticRepairQueueRow> SafeRepairQueue => RepairQueue ?? [];
    }

    /// <summary>
    /// One damaged augmetic on the selected brother. <see cref="HoursRemaining"/> is working hours
    /// at the reference skill; a skilled Techmarine takes fewer.
    /// </summary>
    public sealed record CyberneticRepairOptionView(
        int HitLocationId,
        string LocationName,
        string Damage,
        float HoursRemaining,
        bool IsQueued,
        int RequisitionCost,
        IReadOnlyList<MedicalTreatmentRequisiteView> Requisites,
        bool CanQueue);

    /// <summary>One queued repair, in queue order (Position 1 is worked first).</summary>
    public sealed record CyberneticRepairQueueRow(
        int Position,
        int SoldierId,
        string SoldierName,
        string LocationName,
        float HoursRemaining);

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
        // Operational readiness: the physical result plus the current Chapter injury doctrine.
        // The legacy property name remains for saved/UI compatibility.
        bool IsCombatEffective,
        int MaxRecoveryWeeks,
        string GeneSeedStatus,
        MedicalSeverity WorstSeverity,
        IReadOnlyList<WoundLocationSummary> Wounds,
        IReadOnlyList<MedicalTreatmentOptionView> ReplacementOptions,
        DutyReadinessReasonCode DutyReasonCode = DutyReadinessReasonCode.Ready,
        // Damaged augmetics and their Techmarine repair, queued or not
        // (OnlyWar_TDD.md §6.6.1).
        IReadOnlyList<CyberneticRepairOptionView> CyberneticRepairs = null)
    {
        public IReadOnlyList<CyberneticRepairOptionView> SafeCyberneticRepairs =>
            CyberneticRepairs ?? [];
        public bool IsDutyReady => IsCombatEffective;
        public bool IsWithheldByDoctrine =>
            DutyReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold;
    }

    public sealed record WoundLocationSummary(
        int HitLocationId,
        string LocationName,
        string Status,
        string Recovery,
        bool HoldsProgenoid,
        bool IsCybernetic,
        bool NeedsReplacement,
        MedicalSeverity Severity,
        MedicalWoundLevel PrincipalWoundLevel = MedicalWoundLevel.None,
        bool IsSevered = false,
        bool IsCrippled = false);

}
