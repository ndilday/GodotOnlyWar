using Godot;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ApothecariumScreenController : MainScreenController
{
    private readonly ApothecariumMedicalRecordBuilder _recordBuilder = new();
    private readonly MedicalProcedureService _procedureService = new();
    private readonly RecoveryOperationsViewModelBuilder _recoveryBuilder = new();
    private readonly RecoveryPlanService _recoveryPlans = new();
    private ApothecariumScreenView _apothecariumView;
    private ApothecariumSelectionKind _selectedKind = ApothecariumSelectionKind.Vault;
    private int? _selectedId;
    private bool _showRecoveryOperations;
    private RecoverySortMode _recoverySort = RecoverySortMode.Severity;
    private bool _recoveryAscending;
    private CampaignLocation _recoveryDestination;
    private RecoveryMovementChoice _recoveryMovement;
    private int? _recoveryHitLocationId;
    private MedicalProcedureType? _recoveryProcedureType;

    public event EventHandler CampaignChanged;

    public override void _Ready()
    {
        base._Ready();
        _apothecariumView = GetNode<ApothecariumScreenView>("ApothecariumScreenView");
        _apothecariumView.VaultButtonPressed += OnVaultButtonPressed;
        _apothecariumView.TreeSelectionChanged += OnTreeSelectionChanged;
        _apothecariumView.ReplacementOptionPressed += OnReplacementOptionPressed;
        _apothecariumView.RecoveryOperationsPressed += OnRecoveryOperationsPressed;
        _apothecariumView.RecoveryBackPressed += OnRecoveryBackPressed;
        _apothecariumView.RecoveryPatientSelected += OnRecoveryPatientSelected;
        _apothecariumView.RecoverySortChanged += OnRecoverySortChanged;
        _apothecariumView.RecoveryDestinationSelected += OnRecoveryDestinationSelected;
        _apothecariumView.RecoveryMovementSelected += OnRecoveryMovementSelected;
        _apothecariumView.RecoveryTreatmentSelected += OnRecoveryTreatmentSelected;
        _apothecariumView.RecoveryConfirmPressed += OnRecoveryConfirmPressed;
        Render();
    }

    public override void _ExitTree()
    {
        if (_apothecariumView != null)
        {
            _apothecariumView.VaultButtonPressed -= OnVaultButtonPressed;
            _apothecariumView.TreeSelectionChanged -= OnTreeSelectionChanged;
            _apothecariumView.ReplacementOptionPressed -= OnReplacementOptionPressed;
            _apothecariumView.RecoveryOperationsPressed -= OnRecoveryOperationsPressed;
            _apothecariumView.RecoveryBackPressed -= OnRecoveryBackPressed;
            _apothecariumView.RecoveryPatientSelected -= OnRecoveryPatientSelected;
            _apothecariumView.RecoverySortChanged -= OnRecoverySortChanged;
            _apothecariumView.RecoveryDestinationSelected -= OnRecoveryDestinationSelected;
            _apothecariumView.RecoveryMovementSelected -= OnRecoveryMovementSelected;
            _apothecariumView.RecoveryTreatmentSelected -= OnRecoveryTreatmentSelected;
            _apothecariumView.RecoveryConfirmPressed -= OnRecoveryConfirmPressed;
        }
    }

    private void OnVaultButtonPressed(object sender, EventArgs e)
    {
        _selectedKind = ApothecariumSelectionKind.Vault;
        _selectedId = null;
        Render();
    }

    private void OnTreeSelectionChanged(object sender, ApothecariumSelection selection)
    {
        _selectedKind = selection.Kind;
        _selectedId = selection.Id;
        // Do not rebuild (and therefore Clear) the Tree while it is dispatching its
        // selection signal. The clicked row already has the correct visual selection;
        // only the detail panel needs to change here.
        RenderSelectedDetail();
    }

    private void OnReplacementOptionPressed(object sender, ReplacementOption option)
    {
        PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
        Unit chapter = force?.Army?.OrderOfBattle;
        if (force == null || chapter == null)
        {
            return;
        }
        ISoldier soldier = chapter.GetAllMembers().FirstOrDefault(s => s.Id == _selectedId);
        if (soldier == null)
        {
            return;
        }
        _selectedKind = ApothecariumSelectionKind.Soldier;
        _selectedId = soldier.Id;
        _recoveryHitLocationId = option.HitLocationId;
        _recoveryProcedureType = option.Type;
        _showRecoveryOperations = true;
        RenderRecoveryOperations();
    }

    private void OnRecoveryOperationsPressed(object sender, EventArgs e)
    {
        _showRecoveryOperations = true;
        RenderRecoveryOperations();
    }

    private void OnRecoveryBackPressed(object sender, EventArgs e)
    {
        _showRecoveryOperations = false;
        _apothecariumView.HideRecoveryOperations();
        Render();
    }

    private void OnRecoveryPatientSelected(object sender, int soldierId)
    {
        _selectedKind = ApothecariumSelectionKind.Soldier;
        _selectedId = soldierId;
        _recoveryDestination = null;
        _recoveryMovement = RecoveryMovementChoice.None;
        _recoveryHitLocationId = null;
        _recoveryProcedureType = null;
        RenderRecoveryOperations();
    }

    private void OnRecoverySortChanged(object sender, RecoverySortRequest request)
    {
        _recoverySort = request.Mode;
        _recoveryAscending = request.Ascending;
        RenderRecoveryOperations();
    }

    private void OnRecoveryDestinationSelected(object sender, CampaignLocation location)
    {
        _recoveryDestination = location;
        RenderRecoveryOperations();
    }

    private void OnRecoveryMovementSelected(object sender, RecoveryMovementChoice movement)
    {
        _recoveryMovement = movement;
        RenderRecoveryOperations();
    }

    private void OnRecoveryTreatmentSelected(object sender, ReplacementOption option)
    {
        _recoveryHitLocationId = option?.HitLocationId;
        _recoveryProcedureType = option?.Type;
        _recoveryDestination = null;
        RenderRecoveryOperations();
    }

    private void OnRecoveryConfirmPressed(object sender, EventArgs e)
    {
        PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
        PlayerSoldier patient = force?.Army?.PlayerSoldierMap?.GetValueOrDefault(_selectedId ?? -1);
        if (patient?.IndividualPosting?.Kind == IndividualPostingKind.AwaitingReunion)
        {
            RecoveryPlanCommitResult reunion = _recoveryPlans.Rejoin(patient);
            if (reunion.Succeeded) CampaignChanged?.Invoke(this, EventArgs.Empty);
            RenderRecoveryOperations();
            return;
        }
        ReplacementOption option = patient == null
            ? null
            : _recordBuilder.BuildSoldierSummary(patient, force).ReplacementOptions.FirstOrDefault(candidate =>
                candidate.HitLocationId == _recoveryHitLocationId
                && candidate.Type == _recoveryProcedureType)
                ?? _recordBuilder.BuildSoldierSummary(patient, force).ReplacementOptions.FirstOrDefault();
        RecoveryPlanCommitResult result = _recoveryPlans.Commit(
            force,
            patient,
            option,
            _recoveryDestination,
            _recoveryMovement,
            GameDataSingleton.Instance?.Date);
        if (result.Succeeded)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
            _recoveryDestination = null;
            _recoveryMovement = RecoveryMovementChoice.None;
        }
        RenderRecoveryOperations();
    }

    private void RenderRecoveryOperations()
    {
        var data = GameDataSingleton.Instance;
        PlayerForce force = data?.Sector?.PlayerForce;
        if (force == null) return;
        RecoveryOperationsViewModel model = _recoveryBuilder.Build(
            force,
            data.Sector.Planets.Values,
            _selectedId,
            _recoverySort,
            _recoveryAscending,
            _recoveryDestination,
            _recoveryMovement,
            _recoveryHitLocationId,
            _recoveryProcedureType);
        if (_selectedId == null && model.Patient != null) _selectedId = model.Patient.SoldierId;
        _apothecariumView.ShowRecoveryOperations(model);
    }

    /// <summary>
    /// Rebuilds the roster and detail panel from the current campaign state while preserving
    /// the user's selection. The screen instance is reused between openings, so callers use
    /// this after another campaign screen may have moved a squad or changed its medical state.
    /// </summary>
    public void RefreshFromExternalChange()
    {
        Render();
    }

    /// <summary>
    /// Selects a medical record requested by another workspace while keeping the Apothecarium's
    /// normal tree/detail presentation authoritative.
    /// </summary>
    public void FocusSoldier(int soldierId)
    {
        _selectedKind = ApothecariumSelectionKind.Soldier;
        _selectedId = soldierId;
        Render();
        _apothecariumView?.FocusSoldier(soldierId);
    }

    private void Render()
    {
        PlayerForce force = GameDataSingleton.Instance?.Sector?.PlayerForce;
        Unit chapter = force?.Army?.OrderOfBattle;
        if (force == null || chapter == null)
        {
            return;
        }

        if (_showRecoveryOperations)
        {
            RenderRecoveryOperations();
            return;
        }
        _apothecariumView.HideRecoveryOperations();
        _apothecariumView.SetVaultSelected(_selectedKind == ApothecariumSelectionKind.Vault);
        _apothecariumView.SetTree(_recordBuilder.BuildTree(
            chapter, _selectedKind, _selectedId, woundedOnly: true, force: force));
        RenderSelectedDetail(chapter, force);
    }

    private void RenderSelectedDetail(Unit chapter = null, PlayerForce force = null)
    {
        force ??= GameDataSingleton.Instance?.Sector?.PlayerForce;
        chapter ??= force?.Army?.OrderOfBattle;
        if (force == null || chapter == null)
        {
            return;
        }

        _apothecariumView.SetVaultSelected(_selectedKind == ApothecariumSelectionKind.Vault);
        switch (_selectedKind)
        {
            case ApothecariumSelectionKind.Unit:
                RenderUnit(chapter, force);
                break;
            case ApothecariumSelectionKind.Squad:
                RenderSquad(chapter, force);
                break;
            case ApothecariumSelectionKind.Soldier:
                RenderSoldier(chapter);
                break;
            default:
                RenderVault(force);
                break;
        }
    }

    private void RenderVault(PlayerForce force)
    {
        _apothecariumView.ShowVault(_recordBuilder.BuildVault(force, GameDataSingleton.Instance.Date));
    }

    private void RenderUnit(Unit chapter, PlayerForce force)
    {
        Unit unit = chapter.Id == _selectedId ? chapter : chapter.ChildUnits.SelectMany(FlattenUnits).FirstOrDefault(u => u.Id == _selectedId);
        if (unit == null)
        {
            RenderVault(GameDataSingleton.Instance.Sector.PlayerForce);
            return;
        }

        _apothecariumView.ShowRollup(_recordBuilder.BuildUnitSummary(unit, force));
    }

    private void RenderSquad(Unit chapter, PlayerForce force)
    {
        Squad squad = chapter.GetAllSquads().FirstOrDefault(s => s.Id == _selectedId);
        if (squad == null)
        {
            RenderVault(GameDataSingleton.Instance.Sector.PlayerForce);
            return;
        }

        _apothecariumView.ShowRollup(_recordBuilder.BuildSquadSummary(squad, force));
    }

    private void RenderSoldier(Unit chapter)
    {
        ISoldier soldier = chapter.GetAllMembers().FirstOrDefault(s => s.Id == _selectedId);
        if (soldier == null)
        {
            RenderVault(GameDataSingleton.Instance.Sector.PlayerForce);
            return;
        }

        MedicalSoldierSummary summary = _recordBuilder.BuildSoldierSummary(
            soldier, GameDataSingleton.Instance.Sector.PlayerForce);
        summary = EnrichWithRequisites(GameDataSingleton.Instance.Sector.PlayerForce, soldier, summary);
        _apothecariumView.ShowSoldier(summary);
    }

    // Fills each replacement option's requisite breakdown (rendered green/red by the view)
    // and drops any location already under an active procedure.
    private MedicalSoldierSummary EnrichWithRequisites(PlayerForce force, ISoldier soldier, MedicalSoldierSummary summary)
    {
        if (summary.ReplacementOptions.Count == 0)
        {
            return summary;
        }
        List<ReplacementOption> enriched = [];
        foreach (ReplacementOption option in summary.ReplacementOptions)
        {
            if (_procedureService.HasProcedureInProgress(force, soldier.Id, option.HitLocationId))
            {
                continue;
            }
            IReadOnlyList<ProcedureRequisite> requisites = _procedureService.EvaluateRequisites(force, soldier, option);
            enriched.Add(option with { Requisites = requisites, CanAssign = requisites.All(r => r.IsMet) });
        }
        return summary with { ReplacementOptions = enriched };
    }

    private static System.Collections.Generic.IEnumerable<Unit> FlattenUnits(Unit unit)
    {
        yield return unit;
        foreach (Unit child in unit.ChildUnits ?? [])
        {
            foreach (Unit descendant in FlattenUnits(child))
            {
                yield return descendant;
            }
        }
    }
}
