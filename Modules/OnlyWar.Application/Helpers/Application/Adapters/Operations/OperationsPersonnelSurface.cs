using OnlyWar.Contracts.Operations;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Application.Adapters.Operations;

/// <summary>
/// Engine composition for the Operations personnel port. Operations receives this capability;
/// only this adapter knows which Campaign services currently implement it.
/// </summary>
public sealed class OperationsPersonnelSurface : IOperationsPersonnelSurface
{
    public static OperationsPersonnelSurface Instance { get; } = new();

    static OperationsPersonnelSurface()
    {
        OperationsPersonnelDefaults.Configure(Instance);
    }

    private readonly CharacterAvailabilityService _availability = new();
    private readonly IndividualPostingService _postings =
        new(OrderCommitmentSurface.Instance);

    public PersonnelAvailabilityDecision EvaluateMovement(
        PlayerSoldier character,
        CampaignLocation destination,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        Project(_availability.EvaluateMovement(character, destination, doctrine, program));

    public PersonnelAvailabilityDecision EvaluateOrderAssignment(
        PlayerSoldier character,
        Order order,
        Region origin = null,
        IReadOnlyList<Squad> stagingSquads = null,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        Project(_availability.EvaluateOrderAssignment(
            character, order, origin, stagingSquads, doctrine, program));

    public int PresentCount(
        Squad squad,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null) =>
        SquadStrengthSnapshotBuilder.Build(squad, program, doctrine).Present;

    public bool CanCreate(
        PlayerSoldier soldier,
        IndividualPostingKind kind,
        CampaignLocation location,
        Order order,
        out string reason,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        _postings.CanCreate(soldier, kind, location, order, out reason, doctrine, program);

    public IndividualPosting Restore(
        PlayerSoldier soldier,
        IndividualPostingKind kind,
        CampaignLocation location,
        Date startedDate,
        Order order = null,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        _postings.Restore(soldier, kind, location, startedDate, order, doctrine, program);

    public IndividualPosting RestorePhysical(
        PlayerSoldier soldier,
        IndividualPostingPurpose purpose,
        CampaignLocation location,
        Date startedDate) =>
        _postings.RestorePhysical(soldier, purpose, location, startedDate);

    public void BeginMedicalDetachment(
        PlayerSoldier soldier,
        CampaignLocation location,
        Date date) =>
        _postings.BeginMedicalDetachment(soldier, location, date);

    public void ReleaseFromOrder(PlayerSoldier soldier) =>
        _postings.ReleaseFromOrder(soldier);

    public void NormalizeReunion(PlayerSoldier soldier) =>
        _postings.NormalizeReunion(soldier);

    private static PersonnelAvailabilityDecision Project(
        CharacterAvailabilityEvaluation evaluation) =>
        evaluation == null
            ? new PersonnelAvailabilityDecision(false, -1, "No availability decision was returned.")
            : new PersonnelAvailabilityDecision(
                evaluation.IsAllowed,
                (int)evaluation.ReasonCode,
                evaluation.Reason);
}
