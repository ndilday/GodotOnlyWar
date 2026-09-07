using System;
using System.Collections.Generic;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Application;

public enum MedicalLocationKind { Ship, Region }
public sealed record MedicalLocationId(MedicalLocationKind Kind, int Id);
public sealed record CareDestinationView(
    MedicalLocationId Location, string Name, string SiteType, CareDestinationState State,
    int RequiredBerths, int AvailableBerths, string ApothecaryName, string TechmarineName,
    IReadOnlyList<CareDestinationReason> Reasons);

public sealed record MedicalScreenQuery(
    ApothecariumSelectionKind Kind = ApothecariumSelectionKind.Vault, int? SelectedId = null);
public sealed record MedicalScreenView(
    Guid SessionToken, IReadOnlyList<ApothecariumTreeItem> Tree,
    GeneSeedVaultSummary Vault, MedicalUnitSummary Rollup, MedicalSoldierSummary Soldier);
public sealed record RecoveryQuery(
    int? SoldierId, RecoverySortMode Sort = RecoverySortMode.Severity, bool Ascending = false,
    MedicalLocationId Destination = null, RecoveryMovementChoice Movement = RecoveryMovementChoice.None,
    int? HitLocationId = null, MedicalProcedureType? ProcedureType = null);
public sealed record RecoveryScreenView(Guid SessionToken, RecoveryOperationsViewModel Model);
public sealed record ConfirmRecoveryCommand(
    Guid SessionToken, int SoldierId, MedicalLocationId Destination,
    RecoveryMovementChoice Movement, int? HitLocationId, MedicalProcedureType? ProcedureType);

public interface IMedicalScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;
    MedicalScreenView QueryMedical(MedicalScreenQuery query);
    RecoveryScreenView QueryRecovery(RecoveryQuery query);
    RecoveryPlanCommitResult ConfirmRecovery(ConfirmRecoveryCommand command);
}
