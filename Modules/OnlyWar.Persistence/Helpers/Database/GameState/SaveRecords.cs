using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Supply;

namespace OnlyWar.Persistence.Database.GameState;

/// <summary>
/// Raw save relationships. Persistence reads these rows without applying Campaign or Operations
/// mutation policy; the reconstruction coordinator binds them after all domain objects exist.
/// </summary>
public sealed record OrderCharacterRecord(int OrderId, int SoldierId);

/// <summary>Raw persisted request state. Campaign reconstructs the policy object from this record.</summary>
public sealed record PresenceRequestRecord(
    int Id,
    int TargetPlanetId,
    int RequesterId,
    int? ThreatFactionId,
    int RequestDateWeeks,
    int? ResolvedDateWeeks,
    int DeadlineWeeks,
    ForceCommitmentPackage Commitment,
    int OfferedRequisition,
    PledgeScheduleKind OfferedScheduleKind,
    int OfferedCadenceWeeks,
    int OfferedDeliveryDelayWeeks,
    RequestSeverity Severity,
    RequestHazard Hazard,
    long ProgressBattleValueTime,
    bool HasPlayerResponded,
    RequestStatus Status);

/// <summary>Raw physical posting row returned by the save reader.</summary>
public sealed record IndividualPostingRecord(
    int SoldierId,
    IndividualPostingPurpose Purpose,
    int? LoadedShipId,
    int? LandedRegionId,
    int StartedDate);
