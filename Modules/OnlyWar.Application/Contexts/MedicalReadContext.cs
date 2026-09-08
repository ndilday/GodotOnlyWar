using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Application;

/// <summary>
/// The live facts needed by Apothecarium and Recovery Operations.
///
/// Medical application code can resolve patients, formations, worlds and care locations through
/// this feature context without acquiring a general-purpose Sector or the active GameSession.
/// Public medical results remain detached application contracts.
/// </summary>
internal sealed record MedicalReadResult(
    IReadOnlyList<ApothecariumTreeItem> Tree,
    GeneSeedVaultSummary Vault,
    MedicalUnitSummary Rollup,
    MedicalSoldierSummary Soldier);

internal sealed class MedicalReadContext
{
    private readonly Sector _sector;
    private readonly ApothecariumMedicalRecordBuilder _medicalRecords = new();
    private readonly RecoveryOperationsViewModelBuilder _recoveryViews = new();
    private readonly MedicalProcedureService _medicalProcedures = new();

    private Date CurrentDate { get; }
    private Unit Chapter => _sector.PlayerForce?.Army?.OrderOfBattle;
    private IEnumerable<Planet> Planets => _sector.Planets.Values;

    internal MedicalReadContext(Sector sector, Date currentDate)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
    }

    internal MedicalReadResult BuildMedical(MedicalScreenQuery query)
    {
        PlayerForce force = _sector.PlayerForce;
        Unit chapter = Chapter;
        if (chapter == null)
        {
            return new MedicalReadResult([], null, null, null);
        }

        IReadOnlyList<ApothecariumTreeItem> tree = _medicalRecords.BuildTree(
            chapter, query.Kind, query.SelectedId, true, force);
        MedicalUnitSummary rollup = null;
        MedicalSoldierSummary soldier = null;
        switch (query.Kind)
        {
            case ApothecariumSelectionKind.Unit:
                Unit unit = FlattenUnits(chapter)
                    .FirstOrDefault(value => value.Id == query.SelectedId);
                if (unit != null) rollup = _medicalRecords.BuildUnitSummary(unit, force);
                break;
            case ApothecariumSelectionKind.Squad:
                Squad squad = chapter.GetAllSquads()
                    .FirstOrDefault(value => value.Id == query.SelectedId);
                if (squad != null) rollup = _medicalRecords.BuildSquadSummary(squad, force);
                break;
            case ApothecariumSelectionKind.Soldier:
                ISoldier member = chapter.GetAllMembers()
                    .FirstOrDefault(value => value.Id == query.SelectedId);
                if (member != null)
                {
                    soldier = BuildSoldierSummary(member);
                    soldier = soldier with
                    {
                        ReplacementOptions = soldier.ReplacementOptions
                            .Where(option => !_medicalProcedures.HasProcedureInProgress(
                                force, member.Id, option.HitLocationId))
                            .Select(option =>
                            {
                                IReadOnlyList<ProcedureRequisite> requisites =
                                    _medicalProcedures.EvaluateRequisites(force, member, option);
                                return option with
                                {
                                    Requisites = requisites,
                                    CanAssign = requisites.All(value => value.IsMet)
                                };
                            }).ToArray()
                    };
                }
                break;
        }

        return new MedicalReadResult(
            tree,
            rollup == null && soldier == null
                ? _medicalRecords.BuildVault(force, CurrentDate)
                : null,
            rollup,
            soldier);
    }

    internal RecoveryOperationsViewModel BuildRecovery(RecoveryQuery query) =>
        _recoveryViews.Build(
            _sector.PlayerForce,
            Planets,
            query.SoldierId,
            query.Sort,
            query.Ascending,
            ResolveLocation(query.Destination),
            query.Movement,
            query.HitLocationId,
            query.ProcedureType);

    private MedicalSoldierSummary BuildSoldierSummary(ISoldier soldier) =>
        _medicalRecords.BuildSoldierSummary(soldier, _sector.PlayerForce);

    private Region FindRegion(int id) => _sector.Planets.Values
        .SelectMany(planet => planet.Regions)
        .FirstOrDefault(region => region?.Id == id);

    private Ship FindShip(int id) => _sector.PlayerForce?.Fleet?.TaskForces
        .SelectMany(taskForce => taskForce.Ships)
        .FirstOrDefault(ship => ship?.Id == id);

    private CampaignLocation ResolveLocation(MedicalLocationId id)
    {
        if (id == null) return null;
        return id.Kind switch
        {
            MedicalLocationKind.Ship => CampaignLocation.Aboard(FindShip(id.Id)),
            MedicalLocationKind.Region => CampaignLocation.Landed(FindRegion(id.Id)),
            _ => null
        };
    }

    private static IEnumerable<Unit> FlattenUnits(Unit unit)
    {
        yield return unit;
        foreach (Unit child in unit.ChildUnits ?? [])
        {
            foreach (Unit descendant in FlattenUnits(child)) yield return descendant;
        }
    }
}
