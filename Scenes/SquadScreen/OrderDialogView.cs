using Godot;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

// Result of the order dialog's confirm action. TargetFactionId is the id of the enemy
// RegionFaction's PlanetFaction.Faction chosen for Advance/Diversion orders, or -1 when the
// target-faction selector is not applicable to the selected mission (e.g. Recon, Defend, own-region
// missions, or special missions that already carry a baked-in target).
public readonly struct OrderDialogResult
{
    public int RegionId { get; }
    public int MissionCode { get; }
    public int Aggression { get; }
    public int TargetFactionId { get; }

    public OrderDialogResult(int regionId, int missionCode, int aggression, int targetFactionId)
    {
        RegionId = regionId;
        MissionCode = missionCode;
        Aggression = aggression;
        TargetFactionId = targetFactionId;
    }
}

public partial class OrderDialogView : Panel
{
    private RichTextLabel _headerLabel;
    private OptionButton _regionOption;
    private OptionButton _missionOption;
    private RichTextLabel _missionDescription;
    private HBoxContainer _targetFactionHBox;
    private OptionButton _targetFactionOption;
    private OptionButton _aggressionOption;
    private RichTextLabel _aggressionDescription;
    private Button _cancelButton;
    private Button _confirmButton;

    public event EventHandler<int> MissionOptionSelected;
    public event EventHandler<int> RegionOptionSelected;
    public event EventHandler<int> AggressionOptionSelected;
    public event EventHandler<int> TargetFactionOptionSelected;
    public event EventHandler<OrderDialogResult> OrdersConfirmed;
    public event EventHandler Canceled;

    public override void _Ready()
    {
        _headerLabel = GetNode<RichTextLabel>("Panel/HeaderLabel");
        _regionOption = GetNode<OptionButton>("VBoxContainer/RegionHBox/RegionOption");
        _regionOption.ItemSelected += OnRegionOptionItemSelected;
        _missionOption = GetNode<OptionButton>("VBoxContainer/MissionHBox/MissionOption");
        _missionOption.ItemSelected += OnMissionOptionItemSelected;
        _missionDescription = GetNode<RichTextLabel>("VBoxContainer/MissionDescriptionHBox/MissionDescription");
        _targetFactionHBox = GetNode<HBoxContainer>("VBoxContainer/TargetFactionHBox");
        _targetFactionOption = GetNode<OptionButton>("VBoxContainer/TargetFactionHBox/TargetFactionOption");
        _targetFactionOption.ItemSelected += OnTargetFactionOptionItemSelected;
        _targetFactionHBox.Visible = false;
        _aggressionOption = GetNode<OptionButton>("VBoxContainer/AggressionHBox/AggressionOption");
        _aggressionOption.ItemSelected += OnAggressionOptionItemSelected;
        _aggressionDescription = GetNode<RichTextLabel>("VBoxContainer/AggressionDescriptionHBox/AggressionDescription");
        _cancelButton = GetNode<Button>("CancelButton");
        _cancelButton.Pressed += OnCancelButtonPressed;
        _confirmButton = GetNode<Button>("ConfirmButton");
        _confirmButton.Pressed += OnConfirmButtonPressed;
        ApplyThemeStyling();
    }

    public void SetHeader(string header)
    {
        _headerLabel.Text = header;
    }

    public void UnsetAggressionOption()
    {
        _aggressionOption.Select(-1);
    }

    public void DisableMissionOption()
    {
        _missionOption.Select(-1);
        _missionOption.Disabled = true;
        HideTargetFactionOption();
    }

    public void SetAggressionDescription(string text)
    {
        _aggressionDescription.Text = text;
    }

    public void SetMissionDescription(string text)
    {
        _missionDescription.Text = text;
    }

    public void ClearRegionOptions()
    {
        _regionOption.Clear();
    }

    public void ClearMissionOptions()
    {
        _missionOption.Clear();
    }

    public void ClearTargetFactionOptions()
    {
        _targetFactionOption.Clear();
    }

    // Hides the target-faction selector for missions that don't target a specific enemy
    // faction (Recon, own-region missions, and special missions that already carry their
    // own baked-in target).
    public void HideTargetFactionOption()
    {
        ClearTargetFactionOptions();
        _targetFactionHBox.Visible = false;
        _targetFactionOption.Disabled = true;
        UpdateConfirmButtonState();
    }

    // Populates the target-faction selector for the enemy-directed synthesized missions
    // (Advance/Diversion). Exactly one option auto-selects and locks the dropdown (the common
    // single-enemy case stays one-click); two or more options require an explicit pick.
    public void PopulateTargetFactionOptions(IReadOnlyList<Tuple<string, int>> options)
    {
        ClearTargetFactionOptions();
        foreach (var option in options)
        {
            _targetFactionOption.AddItem(option.Item1, option.Item2);
        }
        _targetFactionHBox.Visible = true;
        if (options.Count == 1)
        {
            _targetFactionOption.Select(0);
            _targetFactionOption.Disabled = true;
        }
        else
        {
            _targetFactionOption.Select(-1);
            _targetFactionOption.Disabled = false;
        }
        UpdateConfirmButtonState();
    }

    public int GetSelectedTargetFactionId()
    {
        return _targetFactionOption.Selected < 0 ? -1 : _targetFactionOption.GetSelectedId();
    }

    public void PopulateRegionOptions(IReadOnlyList<Tuple<string, int>> options)
    {
        ClearRegionOptions();
        foreach (var option in options)
        {
            _regionOption.AddItem(option.Item1, option.Item2);
        }
    }

    public void PopulateMissionOptions(IReadOnlyList<Tuple<string, int>> options)
    {
        ClearMissionOptions();
        foreach (var option in options)
        {
            _missionOption.AddItem(option.Item1, option.Item2);
        }
        _missionOption.Select(-1);
    }

    public int GetSelectedMissionId()
    {
        return _missionOption.GetSelectedId();
    }

    public void SelectRegion(int regionId)
    {
        _regionOption.Select(_regionOption.GetItemIndex(regionId));
    }

    public void SelectMission(int missionId)
    {
        _missionOption.Select(_missionOption.GetItemIndex(missionId));
    }

    private void OnRegionOptionItemSelected(long index)
    {
        RegionOptionSelected?.Invoke(this, _regionOption.GetItemId((int)index));
        _missionOption.Disabled = false;
        UpdateConfirmButtonState();
    }

    private void OnMissionOptionItemSelected(long index)
    {
        MissionOptionSelected?.Invoke(this, _missionOption.GetItemId((int)index));
        UpdateConfirmButtonState();
    }

    private void OnAggressionOptionItemSelected(long index)
    {
        AggressionOptionSelected?.Invoke(this, _aggressionOption.GetItemId((int)index));
        UpdateConfirmButtonState();
    }

    private void OnTargetFactionOptionItemSelected(long index)
    {
        TargetFactionOptionSelected?.Invoke(this, _targetFactionOption.GetItemId((int)index));
        UpdateConfirmButtonState();
    }

    private void OnCancelButtonPressed()
    {
        Canceled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirmButtonPressed()
    {
        if (!IsReadyToConfirm())
        {
            _confirmButton.Disabled = true;
            return;
        }

        OrderDialogResult result = new OrderDialogResult
        (
            _regionOption.GetItemId(_regionOption.Selected),
            _missionOption.GetItemId(_missionOption.Selected),
            _aggressionOption.GetItemId(_aggressionOption.Selected),
            _targetFactionHBox.Visible ? GetSelectedTargetFactionId() : -1
        );
        OrdersConfirmed?.Invoke(this, result);
    }

    // A target-faction pick is only required when the selector is visible (Advance/Diversion)
    // and holds more than one option; the single-enemy case auto-selects and is always "ready".
    private bool IsReadyToConfirm()
    {
        if (_regionOption.Selected < 0 || _missionOption.Selected < 0 || _aggressionOption.Selected < 0)
        {
            return false;
        }
        if (_targetFactionHBox.Visible && _targetFactionOption.Selected < 0)
        {
            return false;
        }
        return true;
    }

    private void UpdateConfirmButtonState()
    {
        _confirmButton.Disabled = !IsReadyToConfirm();
    }

    private void ApplyThemeStyling()
    {
        OnlyWarStyle.ApplyContentPanel(this);
        OnlyWarStyle.ApplyInsetPanel(GetNode<Panel>("Panel"));

        _missionDescription.AddThemeColorOverride("default_color", OnlyWarStyle.MutedText);
        _aggressionDescription.AddThemeColorOverride("default_color", OnlyWarStyle.MutedText);

        GetNode<VBoxContainer>("VBoxContainer").AddThemeConstantOverride("separation", 8);
        _cancelButton.CustomMinimumSize = new Vector2(100, 36);
        _confirmButton.CustomMinimumSize = new Vector2(100, 36);
    }
}
