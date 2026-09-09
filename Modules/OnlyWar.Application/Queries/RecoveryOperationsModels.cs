using OnlyWar.Application;
using System.Collections.Generic;

namespace OnlyWar.Application
{
    public enum RecoverySortMode { Severity, RecoveryTime, Squad, Location }

    public enum RecoveryActionState { Met, Pending, Blocked }
    public sealed record RecoverySortRequest(RecoverySortMode Mode, bool Ascending);

    public sealed record RecoveryQueueRow(
        int SoldierId,
        string Name,
        string IconKey,
        string Home,
        string Location,
        string WorstWound,
        int RecoveryWeeks,
        MedicalWoundLevel WoundLevel,
        string PostingStatus,
        IReadOnlyList<string> CareGaps);

    public sealed record RecoverySquadStatus(
        string Squad,
        string Company,
        string Strength,
        string Location,
        string Order);

    public sealed record RecoveryAction(
        string Key,
        string Title,
        string Detail,
        RecoveryActionState State);

    public sealed record RecoveryOperationsViewModel(
        IReadOnlyList<RecoveryQueueRow> Queue,
        MedicalSoldierSummary Patient,
        RecoverySquadStatus SquadStatus,
        MedicalTreatmentOptionView SelectedTreatment,
        IReadOnlyList<CareDestinationView> Destinations,
        CareDestinationView SelectedDestination,
        RecoveryMovementChoice Movement,
        IReadOnlyList<RecoveryAction> Actions,
        int TotalRequisition,
        bool CanConfirm,
        string PlanStatus);
}
