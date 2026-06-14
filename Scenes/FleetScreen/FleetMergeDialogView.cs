using Godot;
using System;
using System.Collections.Generic;

public partial class FleetMergeDialogView : DialogView
{
    private ItemList _targetList;
    private Label _detailLabel;
    private Button _mergeButton;

    public event EventHandler<int> TargetSelected;
    public event EventHandler MergePressed;

    public override void _Ready()
    {
        base._Ready();
        _targetList = GetNode<ItemList>("Panel/TargetList");
        _detailLabel = GetNode<Label>("Panel/DetailLabel");
        _mergeButton = GetNode<Button>("Panel/MergeButton");

        _targetList.ItemSelected += OnTargetItemSelected;
        _mergeButton.Pressed += () => MergePressed?.Invoke(this, EventArgs.Empty);
        _mergeButton.Disabled = true;
    }

    public void SetHeader(string text)
    {
        GetNode<Label>("Panel/Header/Label").Text = text;
    }

    public void PopulateTargets(IReadOnlyList<KeyValuePair<int, string>> targets)
    {
        _targetList.Clear();
        foreach (KeyValuePair<int, string> target in targets)
        {
            int index = _targetList.AddItem(target.Value);
            _targetList.SetItemMetadata(index, target.Key);
        }
        _detailLabel.Text = targets.Count == 0
            ? "No other task forces are in orbit here to merge with."
            : "Select a task force to merge into this one.";
        _mergeButton.Disabled = true;
    }

    public void SetDetail(string text, bool canMerge)
    {
        _detailLabel.Text = text;
        _mergeButton.Disabled = !canMerge;
    }

    private void OnTargetItemSelected(long index)
    {
        int fleetId = (int)_targetList.GetItemMetadata((int)index);
        TargetSelected?.Invoke(this, fleetId);
    }
}
