using OnlyWar.Medical.Abstractions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

namespace OnlyWar.Operations.Abstractions;

/// <summary>
/// Queries over personnel availability. Requests contain only immutable IDs and already-projected
/// facts; no Campaign aggregate crosses this read boundary.
/// </summary>
public interface IPersonnelAvailabilityQueries
{
    PersonnelAvailabilityDecision EvaluateMovement(PersonnelMovementRequest request);

    PersonnelAvailabilityDecision EvaluateOrderAssignment(PersonnelOrderAssignmentRequest request);

    int PresentCount(PersonnelFormationSnapshot formation);
}

/// <summary>
/// Commands that mutate physical posting state. Aggregate handles are intentionally confined to
/// this separate port: the operation has to update the authoritative roster and ship manifest, so
/// a purely detached request would only hide the mutation rather than remove the coupling.
/// </summary>
public interface IPhysicalPostingCommands
{
    bool CanCreate(
        PlayerSoldier soldier,
        IndividualPostingPurpose purpose,
        CampaignLocation location,
        out string reason);

    IndividualPosting RestorePhysical(
        PlayerSoldier soldier,
        IndividualPostingPurpose purpose,
        CampaignLocation location,
        Date startedDate);

    void BeginMedicalDetachment(
        PlayerSoldier soldier,
        CampaignLocation location,
        Date date);

    void NormalizeReunion(PlayerSoldier soldier);
}

/// <summary>Compatibility composition surface combining the two explicit personnel capabilities.</summary>
public interface IOperationsPersonnelSurface :
    IPersonnelAvailabilityQueries,
    IPhysicalPostingCommands
{
}

/// <summary>Stable physical location facts used by availability policy.</summary>
public sealed record PersonnelLocationSnapshot(
    int? ShipId = null,
    int? RegionId = null,
    bool IsInWarp = false,
    IReadOnlyList<int> AdjacentRegionIds = null)
{
    public bool IsShip => ShipId.HasValue && !RegionId.HasValue;
    public bool IsRegion => RegionId.HasValue && !ShipId.HasValue;
    public bool IsValid => IsShip ^ IsRegion;
    public IReadOnlyList<int> SafeAdjacentRegionIds =>
        AdjacentRegionIds ?? Array.Empty<int>();

    public bool IsSamePlace(PersonnelLocationSnapshot other) =>
        other != null
        && ((IsShip && other.IsShip && ShipId == other.ShipId)
            || (IsRegion && other.IsRegion && RegionId == other.RegionId));
}

/// <summary>Immutable character facts needed by personnel availability policy.</summary>
public sealed record PersonnelCharacterSnapshot(
    int CharacterId,
    string Name,
    bool HasAdministrativeHome,
    int? CurrentOrderId,
    PersonnelLocationSnapshot CurrentLocation,
    DutyReadinessFacts DutyFacts,
    DutyReadinessPolicyOptions DutyOptions);

/// <summary>Request to validate a character's independent movement.</summary>
public sealed record PersonnelMovementRequest(
    PersonnelCharacterSnapshot Character,
    PersonnelLocationSnapshot Destination);

/// <summary>Request to validate attaching a character to an order from a staging area.</summary>
public sealed record PersonnelOrderAssignmentRequest(
    PersonnelCharacterSnapshot Character,
    int? RequestedOrderId,
    PersonnelLocationSnapshot ExplicitOrigin,
    IReadOnlyList<PersonnelLocationSnapshot> StagingLocations,
    PersonnelLocationSnapshot TargetLocation)
{
    public IReadOnlyList<PersonnelLocationSnapshot> SafeStagingLocations =>
        StagingLocations ?? Array.Empty<PersonnelLocationSnapshot>();
}

/// <summary>Immutable presence facts used for ship-capacity calculations.</summary>
public sealed record PersonnelFormationSnapshot(int FormationId, int PresentCount);

/// <summary>Stable reason codes projected by personnel capabilities.</summary>
public enum PersonnelAvailabilityReasonCode
{
    None = 0,
    MissingCharacter,
    NoAdministrativeFormation,
    AssignedElsewhere,
    InWarp,
    MissingLocation,
    NotCombatEffective,
    ReservedForProcedure,
    UntreatedSeverance,
    InsufficientFunctioningArms,
    ChapterInjuryThreshold,
    NotAtOrigin,
    AlreadyAtDestination,
    ContinuousTaskCommitment
}

/// <summary>Stable, serializable-in-spirit projection of a personnel availability decision.</summary>
public sealed record PersonnelAvailabilityDecision(
    bool IsAllowed,
    int ReasonCode = 0,
    string Reason = null)
{
    public static PersonnelAvailabilityDecision Allowed { get; } = new(true);
}
