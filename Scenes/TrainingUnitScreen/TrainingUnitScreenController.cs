using Godot;
using OnlyWar.Application;
using System;

public partial class TrainingUnitScreenController : MainScreenController
{
    private ITrainingScreenApplication _application;
    private TrainingUnitScreenView _view;
    private int? _selectedSquadId;
    private RecruitmentDoctrineDraft _draft;

    public event EventHandler<int> SoldierLinkClicked;
    public event EventHandler CampaignChanged;
    public event EventHandler ManageAdministrativeStaffRequested;
    public event EventHandler<int> NeophytePlacementRequested;
    public event EventHandler<int> Phase13PromotionRequested;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<TrainingUnitScreenView>("TrainingUnitScreenView");
        _view.LinkClicked += OnLinkClicked;
        _view.SquadButtonPressed += OnSquadButtonPressed;
        _view.TrainingOptionSelected += OnTrainingOptionSelected;
        _view.DoctrineChanged += OnDoctrineChanged;
        _view.DoctrineConfirmed += OnDoctrineConfirmed;
        _view.ManageAdministrativeStaffRequested += (sender, e) =>
            ManageAdministrativeStaffRequested?.Invoke(this, e);
        _view.NeophytePlacementRequested += (sender, aspirantId) =>
            NeophytePlacementRequested?.Invoke(this, aspirantId);
        _view.Phase13PromotionRequested += (sender, soldierId) =>
            Phase13PromotionRequested?.Invoke(this, soldierId);
        RefreshFromExternalChange();
    }

    public void Configure(ITrainingScreenApplication application)
    {
        if (_application != null)
        {
            _application.SessionChanged -= OnSessionChanged;
        }

        _application = application;
        if (_application != null)
        {
            _application.SessionChanged += OnSessionChanged;
        }
        RefreshFromExternalChange();
    }

    private void OnSessionChanged(object sender, EventArgs e)
    {
        // The staged doctrine and the selected squad belong to the replaced campaign.
        _draft = null;
        _selectedSquadId = null;
        RefreshFromExternalChange();
    }

    public void RefreshFromExternalChange()
    {
        if (_view == null || _application == null) return;

        RecruitmentScreenSnapshot snapshot =
            _application.QueryRecruitmentScreen(_draft, _selectedSquadId);
        if (!snapshot.IsUnlocked)
        {
            _draft = null;
            _view.RenderLockedState(snapshot.LockedMessage);
            _view.PopulateScoutSquads(snapshot.ScoutSquads, _selectedSquadId);
            return;
        }

        _draft = snapshot.Doctrine;
        if (_selectedSquadId.HasValue
            && !ContainsSquad(snapshot.ScoutSquads, _selectedSquadId.Value))
        {
            _selectedSquadId = null;
        }
        _view.Render(snapshot, _selectedSquadId);
    }

    public void OpenMandatorySetup()
    {
        RefreshFromExternalChange();
        if (_application?.IsRecruitmentSetupComplete == false
            && _application.QueryDoctrineDraft() != null)
        {
            _view.ShowRecruitmentView();
            _view.SetSetupMode(true);
        }
    }

    public override void RequestClose()
    {
        if (_application?.IsRecruitmentSetupComplete == false
            && _application.QueryDoctrineDraft() != null)
        {
            _view.ShowSetupValidation(
                "The Master of Recruitment must establish the program before continuing.");
            return;
        }
        base.RequestClose();
    }

    private void OnDoctrineChanged(object sender, RecruitmentDoctrineDraft doctrine)
    {
        _draft = doctrine;
        RefreshPreviewOnly();
    }

    private void RefreshPreviewOnly()
    {
        if (_application == null || _draft == null) return;

        RecruitmentForecastView forecast = _application.PreviewForecast(_draft);
        if (forecast == null) return;

        _view.UpdateForecast(_draft, forecast);
    }

    private void OnDoctrineConfirmed(object sender, EventArgs e)
    {
        if (_application == null || _draft == null) return;

        TrainingCommandResult result = _application.ConfirmDoctrine(
            _application.SessionToken, _draft);
        if (!result.Succeeded)
        {
            _view.ShowSetupValidation(result.Message);
            return;
        }

        CampaignChanged?.Invoke(this, EventArgs.Empty);
        _view.ShowSetupValidation(string.Empty);
        RefreshFromExternalChange();
    }

    private void PopulateScoutSquadList()
    {
        _view.PopulateScoutSquads(
            _application?.QueryScoutSquads(_selectedSquadId) ?? [], _selectedSquadId);
    }

    private void OnSquadButtonPressed(object sender, int squadId)
    {
        _selectedSquadId = squadId;
        PopulateScoutSquadList();
    }

    private void OnTrainingOptionSelected(object sender, string optionKey)
    {
        if (_application == null || !_selectedSquadId.HasValue) return;

        if (_application.SetScoutTrainingOption(
            _application.SessionToken, _selectedSquadId.Value, optionKey).Succeeded)
        {
            CampaignChanged?.Invoke(this, EventArgs.Empty);
        }
        PopulateScoutSquadList();
    }

    private void OnLinkClicked(object sender, Variant meta)
    {
        SoldierLinkClicked?.Invoke(this, meta.AsInt32());
    }

    private static bool ContainsSquad(
        System.Collections.Generic.IReadOnlyList<ScoutSquadRow> squads, int squadId)
    {
        foreach (ScoutSquadRow squad in squads)
        {
            if (squad.Id == squadId) return true;
        }
        return false;
    }
}
