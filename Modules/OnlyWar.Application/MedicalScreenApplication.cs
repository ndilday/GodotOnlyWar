using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;

namespace OnlyWar.Application;

public sealed class MedicalScreenApplication : CampaignScreenApplication,
    IMedicalScreenApplication
{
    private readonly ApothecariumMedicalRecordBuilder _medicalRecords = new();
    private readonly RecoveryOperationsViewModelBuilder _recoveryViews = new();
    private RecoveryPlanService _recoveryPlans =>
        new(Services.Operations.Personnel, Services.Operations.Commitments);
    private readonly MedicalProcedureService _medicalProcedures = new();

    public MedicalScreenApplication(CampaignApplicationContext context) : base(context) { }

    public MedicalScreenView QueryMedical(MedicalScreenQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var session = ActiveSession;
        var force = session?.Sector.PlayerForce;
        Unit chapter = force?.Army?.OrderOfBattle;
        if (chapter == null) return new(SessionToken, [], null, null, null);
        var tree = _medicalRecords.BuildTree(chapter, query.Kind, query.SelectedId, true, force);
        MedicalUnitSummary rollup = null;
        MedicalSoldierSummary soldier = null;
        switch (query.Kind)
        {
            case ApothecariumSelectionKind.Unit:
                var unit = FlattenUnits(chapter).FirstOrDefault(value => value.Id == query.SelectedId);
                if (unit != null) rollup = _medicalRecords.BuildUnitSummary(unit, force);
                break;
            case ApothecariumSelectionKind.Squad:
                var squad = chapter.GetAllSquads().FirstOrDefault(value => value.Id == query.SelectedId);
                if (squad != null) rollup = _medicalRecords.BuildSquadSummary(squad, force);
                break;
            case ApothecariumSelectionKind.Soldier:
                var member = chapter.GetAllMembers().FirstOrDefault(value => value.Id == query.SelectedId);
                if (member != null)
                {
                    soldier = _medicalRecords.BuildSoldierSummary(member, force);
                    soldier = soldier with
                    {
                        ReplacementOptions = soldier.ReplacementOptions
                            .Where(option => !_medicalProcedures.HasProcedureInProgress(force, member.Id, option.HitLocationId))
                            .Select(option =>
                            {
                                var requisites = _medicalProcedures.EvaluateRequisites(force, member, option);
                                return option with { Requisites = requisites, CanAssign = requisites.All(value => value.IsMet) };
                            }).ToArray()
                    };
                }
                break;
        }
        return new(SessionToken, tree,
            rollup == null && soldier == null ? _medicalRecords.BuildVault(force, session.CurrentDate) : null,
            rollup, soldier);
    }

    public RecoveryScreenView QueryRecovery(RecoveryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var session = ActiveSession;
        return new(SessionToken, _recoveryViews.Build(session?.Sector.PlayerForce,
            session?.Sector.Planets.Values, query.SoldierId, query.Sort, query.Ascending,
            ResolveMedicalLocation(query.Destination), query.Movement, query.HitLocationId, query.ProcedureType));
    }

    public RecoveryPlanCommitResult ConfirmRecovery(ConfirmRecoveryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ActiveSession == null || command.SessionToken != SessionToken)
            return new(false, "The campaign changed. Review the current recovery plan.");
        var force = ActiveSession.Sector.PlayerForce;
        var patient = force?.Army?.PlayerSoldierMap.GetValueOrDefault(command.SoldierId);
        if (patient == null) return new(false, "The patient is no longer available.");
        if (IsAwaitingReunion(patient))
            return _recoveryPlans.Rejoin(patient);
        // Resolve the exact displayed treatment again; never silently substitute another option.
        var option = _medicalRecords.BuildSoldierSummary(patient, force).ReplacementOptions
            .FirstOrDefault(value => value.HitLocationId == command.HitLocationId && value.Type == command.ProcedureType);
        if (option == null || _medicalProcedures.HasProcedureInProgress(force, patient.Id, option.HitLocationId))
            return new(false, "The selected treatment is no longer available.");
        return _recoveryPlans.Commit(force, patient, option, ResolveMedicalLocation(command.Destination),
            command.Movement, ActiveSession.CurrentDate);
    }

    private CampaignLocation ResolveMedicalLocation(MedicalLocationId id)
    {
        if (id == null || ActiveSession == null) return null;
        var sector = ActiveSession.Sector;
        return id.Kind switch
        {
            MedicalLocationKind.Ship => CampaignLocation.Aboard(sector.PlayerForce.Fleet.TaskForces
                .SelectMany(fleet => fleet.Ships).FirstOrDefault(ship => ship.Id == id.Id)),
            MedicalLocationKind.Region => CampaignLocation.Landed(sector.Planets.Values
                .SelectMany(planet => planet.Regions).FirstOrDefault(region => region?.Id == id.Id)),
            _ => null
        };
    }

    private static bool IsAwaitingReunion(PlayerSoldier soldier) =>
        soldier?.IndividualPosting?.Purpose == IndividualPostingPurpose.Medical
        && !soldier.IsUndergoingMedicalProcedure
        && soldier.Body?.HitLocations.All(location =>
            location.Wounds.WoundTotal == 0 && !location.IsSevered) == true;

    private static IEnumerable<Unit> FlattenUnits(Unit unit)
    {
        yield return unit;
        foreach (var child in unit.ChildUnits ?? [])
            foreach (var descendant in FlattenUnits(child)) yield return descendant;
    }
}
