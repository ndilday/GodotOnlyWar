using Godot;
using OnlyWar.Application;
using System;

public partial class ApothecariumScreenController : MainScreenController
{
    private IMedicalScreenApplication _application;
    private Guid _sessionToken;
    private MedicalTreatmentOptionView _displayedTreatment;

    public void Configure(IMedicalScreenApplication application)
    {
        if (_application != null) _application.SessionChanged -= OnSessionChanged;
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _application.SessionChanged += OnSessionChanged;
        _sessionToken = application.SessionToken;
    }

    private void OnSessionChanged(object sender, EventArgs args)
    {
        _sessionToken = _application.SessionToken;
        _selectedKind = ApothecariumSelectionKind.Vault;
        _selectedId = null;
        _showRecoveryOperations = false;
        _recoveryDestination = null;
        _recoveryMovement = RecoveryMovementChoice.None;
        _recoveryHitLocationId = null;
        _recoveryProcedureType = null;
        _displayedTreatment = null;
        if (_apothecariumView != null) Render();
    }
    private ApothecariumScreenView _apothecariumView;
    private ApothecariumSelectionKind _selectedKind = ApothecariumSelectionKind.Vault;
    private int? _selectedId;
    private bool _showRecoveryOperations;
    private RecoverySortMode _recoverySort = RecoverySortMode.Severity;
    private bool _recoveryAscending;
    private MedicalLocationId _recoveryDestination;
    private RecoveryMovementChoice _recoveryMovement;
    private int? _recoveryHitLocationId;
    private MedicalProcedureChoice? _recoveryProcedureType;

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
        if (_application != null) _application.SessionChanged -= OnSessionChanged;
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

    private void OnReplacementOptionPressed(object sender, MedicalTreatmentOptionView option)
    {
        if (option == null || _selectedId == null) return;
        _selectedKind = ApothecariumSelectionKind.Soldier;
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

    private void OnRecoveryDestinationSelected(object sender, MedicalLocationId location)
    {
        _recoveryDestination = location;
        RenderRecoveryOperations();
    }

    private void OnRecoveryMovementSelected(object sender, RecoveryMovementChoice movement)
    {
        _recoveryMovement = movement;
        RenderRecoveryOperations();
    }

    private void OnRecoveryTreatmentSelected(object sender, MedicalTreatmentOptionView option)
    {
        _recoveryHitLocationId = option?.HitLocationId;
        _recoveryProcedureType = option?.Type;
        _recoveryDestination = null;
        RenderRecoveryOperations();
    }

    private void OnRecoveryConfirmPressed(object sender, EventArgs e)
    {
        if (_selectedId == null) return;
        RecoveryPlanCommitResult result = _application.ConfirmRecovery(new(
            _sessionToken, _selectedId.Value, _recoveryDestination, _recoveryMovement,
            _displayedTreatment?.HitLocationId, _displayedTreatment?.Type));
        if (result.Succeeded)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
            _recoveryDestination = null;
            _recoveryMovement = RecoveryMovementChoice.None;
        }
        RenderRecoveryOperations(result.Succeeded ? null : result.Message);
    }

    private void RenderRecoveryOperations(string failure = null)
    {
        RecoveryScreenView projection = _application.QueryRecovery(new(
            _selectedId, _recoverySort, _recoveryAscending, _recoveryDestination,
            _recoveryMovement, _recoveryHitLocationId, _recoveryProcedureType));
        _sessionToken = projection.SessionToken;
        RecoveryOperationsViewModel model = projection.Model;
        _displayedTreatment = model.SelectedTreatment;
        _selectedId = model.Patient?.SoldierId;
        _apothecariumView.ShowRecoveryOperations(failure == null ? model : model with { PlanStatus = failure });
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
        if (_application == null || _apothecariumView == null) return;
        if (_showRecoveryOperations)
        {
            RenderRecoveryOperations();
            return;
        }
        _apothecariumView.HideRecoveryOperations();
        MedicalScreenView projection = _application.QueryMedical(new(_selectedKind, _selectedId));
        _sessionToken = projection.SessionToken;
        _apothecariumView.SetTree(projection.Tree);
        ShowDetail(projection);
    }

    private void RenderSelectedDetail()
    {
        ShowDetail(_application.QueryMedical(new(_selectedKind, _selectedId)));
    }

    private void ShowDetail(MedicalScreenView projection)
    {
        _sessionToken = projection.SessionToken;
        _apothecariumView.SetVaultSelected(_selectedKind == ApothecariumSelectionKind.Vault);
        if (projection.Soldier != null) _apothecariumView.ShowSoldier(projection.Soldier);
        else if (projection.Rollup != null) _apothecariumView.ShowRollup(projection.Rollup);
        else if (projection.Vault != null) _apothecariumView.ShowVault(projection.Vault);
    }
}
