using Godot;
using System;
using System.Collections.Generic;

public partial class OrderDialogView : Panel
{
    private RichTextLabel _headerLabel;
    private OptionButton _regionOption;
    private OptionButton _missionOption;
    private RichTextLabel _missionDescription;
    private OptionButton _aggressionOption;
    private RichTextLabel _aggressionDescription;
    private Button _cancelButton;
    private Button _confirmButton;

    public event EventHandler<int> MissionOptionSelected;
    public event EventHandler<int> RegionOptionSelected;
    public event EventHandler<int> AggressionOptionSelected;
    public event EventHandler<Tuple<int, int, int>> OrdersConfirmed;
    public event EventHandler Canceled;

    public override void _Ready()
    {
        _headerLabel = GetNode<RichTextLabel>("Panel/HeaderLabel");
        _regionOption = GetNode<OptionButton>("VBoxContainer/RegionHBox/RegionOption");
        _regionOption.ItemSelected += OnRegionOptionItemSelected;
        _missionOption = GetNode<OptionButton>("VBoxContainer/MissionHBox/MissionOption");
        _missionOption.ItemSelected += OnMissionOptionItemSelected;
        _missionDescription = GetNode<RichTextLabel>("VBoxContainer/MissionDescriptionHBox/MissionDescription");
        _aggressionOption = GetNode<OptionButton>("VBoxContainer/AggressionHBox/AggressionOption");
        _aggressionOption.ItemSelected += OnAggressionOptionItemSelected;
        _aggressionDescription = GetNode<RichTextLabel>("VBoxContainer/AggressionDescriptionHBox/AggressionDescription");
        _cancelButton = GetNode<Button>("CancelButton");
        _cancelButton.Pressed += OnCancelButtonPressed;
        _confirmButton = GetNode<Button>("ConfirmButton");
        _confirmButton.Pressed += OnConfirmButtonPressed;
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
        _confirmButton.Disabled = _missionOption.Selected == -1 || _aggressionOption.Selected == -1;
        _missionOption.Disabled = false;
    }

    private void OnMissionOptionItemSelected(long index)
    {
        MissionOptionSelected?.Invoke(this, _missionOption.GetItemId((int)index));
        _confirmButton.Disabled = _regionOption.Selected == -1 || _aggressionOption.Selected == -1;
    }

    private void OnAggressionOptionItemSelected(long index)
    {
        AggressionOptionSelected?.Invoke(this, _aggressionOption.GetItemId((int)index));
        _confirmButton.Disabled = _regionOption.Selected == -1 || _missionOption.Selected == -1;
    }

    private void OnCancelButtonPressed()
    {
        Canceled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirmButtonPressed()
    {
        Tuple<int, int, int> tuple = new Tuple<int, int, int>
        (
            _regionOption.GetItemId(_regionOption.Selected),
            _missionOption.GetItemId(_missionOption.Selected),
            _aggressionOption.GetItemId(_aggressionOption.Selected)
        );
        OrdersConfirmed?.Invoke(this, tuple);
    }
}
