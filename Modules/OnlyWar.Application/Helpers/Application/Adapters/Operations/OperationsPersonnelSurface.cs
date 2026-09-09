using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Personnel;
using OnlyWar.Operations.Orders;
using OnlyWar.Medical.Readiness;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Collections.Generic;

namespace OnlyWar.Application.Adapters.Operations;

/// <summary>
/// Application composition for the Operations personnel port. Operations receives this capability;
/// only this adapter knows which Campaign services currently implement it.
/// </summary>
public sealed class OperationsPersonnelSurface : IOperationsPersonnelSurface
{
    private readonly IndividualPostingService _postings;
    private readonly IReadinessDecisions _readiness;

    public OperationsPersonnelSurface(
        IReadinessDecisions readiness,
        IOrderCommitmentSurface commitments)
    {
        _readiness = readiness ?? throw new System.ArgumentNullException(nameof(readiness));
        _postings = new IndividualPostingService(
            commitments ?? throw new System.ArgumentNullException(nameof(commitments)));
    }

    public PersonnelAvailabilityDecision EvaluateMovement(
        PersonnelMovementRequest request) =>
        PersonnelAvailabilityPolicy.EvaluateMovement(
            request,
            EvaluateDuty(request?.Character));

    public PersonnelAvailabilityDecision EvaluateOrderAssignment(
        PersonnelOrderAssignmentRequest request) =>
        PersonnelAvailabilityPolicy.EvaluateOrderAssignment(
            request,
            EvaluateDuty(request?.Character));

    public int PresentCount(PersonnelFormationSnapshot formation) =>
        PersonnelAvailabilityPolicy.PresentCount(formation);

    public bool CanCreate(
        PlayerSoldier soldier,
        IndividualPostingPurpose purpose,
        CampaignLocation location,
        out string reason) =>
        _postings.CanCreate(soldier, purpose, location, out reason);

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

    public void NormalizeReunion(PlayerSoldier soldier) =>
        _postings.NormalizeReunion(soldier);

    private DutyReadinessEvaluation EvaluateDuty(PersonnelCharacterSnapshot character) =>
        character == null
            ? null
            : _readiness.EvaluateSoldier(character.DutyFacts, character.DutyOptions);
}
