using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;

namespace OnlyWar.Application;

/// <summary>
/// The write-side recovery capability. It re-resolves the patient and treatment inside the
/// installed medical context, then delegates the atomic mutation to RecoveryPlanService.
/// </summary>
internal sealed class MedicalCommandContext
{
    private readonly Sector _sector;
    private readonly Date _currentDate;
    private readonly RecoveryPlanService _recoveryPlans;
    private readonly ApothecariumMedicalRecordBuilder _medicalRecords = new();
    private readonly MedicalProcedureService _medicalProcedures = new();

    internal MedicalCommandContext(
        Sector sector,
        Date currentDate,
        RecoveryPlanService recoveryPlans)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _currentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
        _recoveryPlans = recoveryPlans ?? throw new ArgumentNullException(nameof(recoveryPlans));
    }

    internal RecoveryPlanCommitResult ConfirmRecovery(ConfirmRecoveryCommand command)
    {
        PlayerForce force = _sector.PlayerForce;
        PlayerSoldier patient = force?.Army?.PlayerSoldierMap
            .GetValueOrDefault(command.SoldierId);
        if (patient == null) return new(false, "The patient is no longer available.");
        if (IsAwaitingReunion(patient)) return _recoveryPlans.Rejoin(patient);

        // Resolve the exact displayed treatment again; never silently substitute another option.
        ReplacementOption option = _medicalRecords.BuildTreatmentOptions(patient, force)
            .FirstOrDefault(value => value.HitLocationId == command.HitLocationId
                && value.Type == MedicalProcedureChoiceMapping.ToDomain(command.ProcedureType));
        if (option == null || _medicalProcedures.HasProcedureInProgress(
                force, patient.Id, option.HitLocationId))
        {
            return new(false, "The selected treatment is no longer available.");
        }

        return _recoveryPlans.Commit(
            force,
            patient,
            option,
            ResolveLocation(command.Destination),
            command.Movement,
            _currentDate);
    }

    private CampaignLocation ResolveLocation(MedicalLocationId id)
    {
        if (id == null) return null;
        return id.Kind switch
        {
            MedicalLocationKind.Ship => CampaignLocation.Aboard(
                _sector.PlayerForce?.Fleet?.TaskForces
                    .SelectMany(taskForce => taskForce.Ships)
                    .FirstOrDefault(ship => ship?.Id == id.Id)),
            MedicalLocationKind.Region => CampaignLocation.Landed(
                _sector.Planets.Values
                    .SelectMany(planet => planet.Regions)
                    .FirstOrDefault(region => region?.Id == id.Id)),
            _ => null
        };
    }

    private static bool IsAwaitingReunion(PlayerSoldier soldier) =>
        soldier?.IndividualPosting?.Purpose == IndividualPostingPurpose.Medical
        && !soldier.IsUndergoingMedicalProcedure
        && soldier.Body?.HitLocations.All(location =>
            location.Wounds.WoundTotal == 0 && !location.IsSevered) == true;
}
