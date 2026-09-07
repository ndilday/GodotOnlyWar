using Godot;
using OnlyWar.Application;
using OnlyWar.Models.Equippables;
using System;

public partial class SquadScreenController : MainScreenController
{
    private ILoadoutScreenApplication _application;
    private int? _squadId;
    private SquadScreenView _view;
    private int? _editingSoldierId;

    public event EventHandler CampaignChanged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SquadScreenView>("DialogView");
        _view.LoadoutChanged += OnLoadoutChanged;
        _view.ReturnToDoctrinePressed += OnReturnToDoctrine;
        _view.CharacterLoadoutSelected += OnCharacterLoadoutSelected;
        _view.CharacterLoadoutReset += OnCharacterLoadoutReset;
        _view.CharacterCustomizeRequested += OnCharacterCustomizeRequested;
        _view.EquipmentLoadoutSaveRequested += OnEquipmentLoadoutSaved;
        _view.ClosePressed += (_, _) => RequestClose();
    }

    public void Configure(ILoadoutScreenApplication application)
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
    }

    private void OnSessionChanged(object sender, EventArgs e)
    {
        // The squad the screen was showing belongs to the replaced campaign.
        _squadId = null;
        _editingSoldierId = null;
        Refresh();
    }

    public void SetSquad(int squadId)
    {
        _squadId = squadId;
        Refresh();
    }

    private void OnLoadoutChanged(object sender, EventArgs e)
    {
        if (!TryApply(_application?.SetSquadLoadout(
            _application.SessionToken, _squadId ?? 0, _view.WorkingLoadout)))
        {
            return;
        }

        _view.SetDoctrineState("Custom loadout", true);
    }

    private void OnReturnToDoctrine(object sender, EventArgs e)
    {
        if (!TryApply(_application?.ReturnSquadToDoctrine(
            _application.SessionToken, _squadId ?? 0)))
        {
            return;
        }

        Refresh();
    }

    private void OnCharacterLoadoutSelected(
        object sender, (int SoldierId, WeaponSet WeaponSet) change)
    {
        if (!TryApply(_application?.SetSquadCharacterWeaponSet(
            _application.SessionToken, _squadId ?? 0, change.SoldierId, change.WeaponSet)))
        {
            return;
        }

        Refresh();
    }

    private void OnCharacterLoadoutReset(object sender, int soldierId)
    {
        if (!TryApply(_application?.ResetSquadCharacterLoadout(
            _application.SessionToken, _squadId ?? 0, soldierId)))
        {
            return;
        }

        Refresh();
    }

    private void OnCharacterCustomizeRequested(object sender, int soldierId)
    {
        if (_application == null || !_squadId.HasValue) return;
        EquipmentEditorView editor = _application.QuerySoldierEquipmentEditor(
            _squadId.Value, soldierId);
        if (!editor.IsAvailable) return;

        _editingSoldierId = soldierId;
        _view.OpenEquipmentEditor(
            editor.Title, editor.Subtitle, editor.Catalog, editor.Loadout, editor.Context);
    }

    private void OnEquipmentLoadoutSaved(EquipmentLoadout loadout)
    {
        if (!_editingSoldierId.HasValue || _application == null || !_squadId.HasValue) return;

        LoadoutCommandResult result = _application.SaveSoldierEquipment(
            _application.SessionToken, _squadId.Value, _editingSoldierId.Value, loadout);
        _editingSoldierId = null;
        if (!result.Succeeded)
        {
            GD.PushWarning(result.Message);
            return;
        }

        CampaignChanged?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private bool TryApply(LoadoutCommandResult result)
    {
        if (result == null || !result.Succeeded) return false;
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Refresh()
    {
        if (_view == null || _application == null || !_squadId.HasValue) return;

        SquadLoadoutView squad = _application.QuerySquadLoadout(_squadId.Value);
        if (!squad.Exists) return;

        _view.Display(
            squad.Title,
            squad.Subtitle,
            squad.SourceText,
            squad.Loadout,
            squad.IsCustom,
            squad.CharacterRows,
            squad.CountSections);
    }
}
