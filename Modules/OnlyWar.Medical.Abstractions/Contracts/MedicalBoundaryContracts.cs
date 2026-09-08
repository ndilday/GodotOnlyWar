using System;
using System.Collections.Generic;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Medical.Abstractions;

/// <summary>
/// The facts needed to answer the individual duty question.  It deliberately contains no
/// soldier, squad, campaign, posting, or recruitment implementation type.
/// </summary>
public readonly record struct DutyReadinessFacts(
    string Name,
    bool IsCombatEffective,
    bool HasUntreatedSeveredLimb,
    bool IsProcedureReserved,
    int FunctioningHands,
    WoundLevel WorstWoundLevel);

/// <summary>Chapter policy supplied to the medical owner at the point of evaluation.</summary>
public readonly record struct DutyReadinessPolicyOptions(
    WoundLevel? InjuryThreshold,
    bool RequireDutyReadySquadLeader = false,
    int MinimumDutyReadySquadStrength = 0);

/// <summary>One member's already-resolved facts for a squad readiness query.</summary>
public readonly record struct SquadMemberReadinessFacts(
    int Id,
    bool IsPresent,
    bool IsCombatEffective,
    bool IsDutyReady,
    bool IsProcedureReserved,
    bool IsIndividuallyPosted,
    bool IsLeader,
    bool IsLeaderDutyReady,
    DutyReadinessReasonCode DutyReasonCode);

/// <summary>
/// A bounded squad snapshot.  Callers resolve location, commitment, and personnel facts; the
/// Medical assembly applies only readiness policy and returns a detached decision.
/// </summary>
public sealed record SquadReadinessFacts(
    int Establishment,
    bool IsAdministrative,
    bool RequiresLeader,
    IReadOnlyList<SquadMemberReadinessFacts> Members,
    SquadCommitmentKind Commitment,
    SquadDeploymentAction Action = SquadDeploymentAction.None,
    IReadOnlyList<SquadReadinessBlocker> Restrictions = null,
    bool HasCurrentOrders = false,
    bool IsInTransit = false,
    bool IsEmbarked = false,
    bool IsLanded = true,
    bool IsOrbiting = false,
    bool IsInWarp = false)
{
    public IReadOnlyList<SquadMemberReadinessFacts> SafeMembers =>
        Members ?? Array.Empty<SquadMemberReadinessFacts>();

    public IReadOnlyList<SquadReadinessBlocker> SafeRestrictions =>
        Restrictions ?? Array.Empty<SquadReadinessBlocker>();
}

/// <summary>Small, explicit input for surgery/facility policy.</summary>
public readonly record struct MedicalFacilityFacts(
    bool IsShipFacility,
    bool HasSoldierCapacity,
    string WorldType,
    bool IsImperialControlled,
    long PublicImperialPopulation);

/// <summary>
/// Health transitions returned by the Medical owner.  The campaign adapter associates the fact
/// with its live soldier and is responsible for posting/reunion/event bookkeeping.
/// </summary>
public sealed record MedicalProcedureCompletion(
    int SoldierId,
    int PrimaryHitLocationTemplateId,
    string PrimaryHitLocationName,
    MedicalProcedureType ProcedureType,
    bool WasAlreadyCybernetic,
    int ProcedureDurationWeeks,
    int RequisitionCost);

/// <summary>One field-care mutation, detached from order and roster implementations.</summary>
public sealed record FieldCareTreatmentResult(
    int SoldierId,
    string SoldierName,
    string LocationName,
    WoundLevel FromBand,
    int WoundsMoved,
    float Cost,
    int Day = 0);

/// <summary>Explicit provider input for one field-care pass.</summary>
public readonly record struct FieldCareProviderFacts(
    int SoldierId,
    string Name,
    float DailyCapacity);

/// <summary>
/// Explicit patient input for one field-care pass. The Medical owner may mutate only the supplied
/// body; roster, order, location and experience bookkeeping stay with the caller.
/// </summary>
public sealed record FieldCarePatientFacts(
    int SoldierId,
    string Name,
    int Rank,
    int Subrank,
    Body Body);
