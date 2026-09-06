using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Contracts.Operations;

/// <summary>
/// The personnel capability consumed by Operations. It deliberately returns detached availability
/// facts instead of the Campaign implementation's evaluation type, and exposes only the physical
/// posting mutations required by movement, order commitment, and mission exfiltration.
/// </summary>
public interface IOperationsPersonnelSurface
{
    PersonnelAvailabilityDecision EvaluateMovement(
        PlayerSoldier character,
        CampaignLocation destination,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    PersonnelAvailabilityDecision EvaluateOrderAssignment(
        PlayerSoldier character,
        Order order,
        Region origin = null,
        IReadOnlyList<Squad> stagingSquads = null,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null);

    int PresentCount(
        Squad squad,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null);

    bool CanCreate(
        PlayerSoldier soldier,
        IndividualPostingKind kind,
        CampaignLocation location,
        Order order,
        out string reason,
        ChapterOperationalDoctrine doctrine = null);

    IndividualPosting Restore(
        PlayerSoldier soldier,
        IndividualPostingKind kind,
        CampaignLocation location,
        Date startedDate,
        Order order = null,
        ChapterOperationalDoctrine doctrine = null);

    IndividualPosting RestorePhysical(
        PlayerSoldier soldier,
        IndividualPostingPurpose purpose,
        CampaignLocation location,
        Date startedDate);

    void BeginMedicalDetachment(
        PlayerSoldier soldier,
        CampaignLocation location,
        Date date);

    void ReleaseFromOrder(PlayerSoldier soldier);

    void NormalizeReunion(PlayerSoldier soldier);
}

/// <summary>
/// Transitional composition hook for legacy command callers that predate explicit personnel
/// inputs. The application/host registers its adapter at startup; Operations never names that
/// adapter's implementation assembly.
/// </summary>
public static class OperationsPersonnelDefaults
{
    private static IOperationsPersonnelSurface _current;

    public static IOperationsPersonnelSurface Current => _current
        ?? throw new System.InvalidOperationException(
            "No Operations personnel capability has been composed for this command.");

    public static void Configure(IOperationsPersonnelSurface personnel) =>
        _current = personnel ?? throw new System.ArgumentNullException(nameof(personnel));
}

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
