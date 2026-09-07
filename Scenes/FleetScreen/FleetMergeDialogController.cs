using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FleetMergeDialogController : DialogController
{
    private FleetMergeDialogView _view;
    private IFleetScreenApplication _application;
    private int _fleetId;
    private int? _selectedTargetId;

    public event EventHandler FleetsMerged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetMergeDialogView>("FleetMergeDialogView");
        _view.TargetSelected += OnTargetSelected;
        _view.MergePressed += OnMergePressed;
    }

    public void Configure(IFleetScreenApplication application)
    {
        _application = application;
    }

    public void SetTaskForce(int fleetId)
    {
        _fleetId = fleetId;
        _selectedTargetId = null;
        FleetMergeOptionsView options = _application.QueryFleetMergeOptions(fleetId);
        _view.SetHeader(options.Header);
        _view.PopulateTargets(options.Targets
            .Select(target => new KeyValuePair<int, string>(target.FleetId, target.Label))
            .ToList());
    }

    private void OnTargetSelected(object sender, int fleetId)
    {
        FleetMergeSelectionView selection = _application.EvaluateMergeSelection(_fleetId, fleetId);
        _selectedTargetId = selection.CanMerge ? fleetId : null;
        _view.SetDetail(selection.Detail, selection.CanMerge);
    }

    private void OnMergePressed(object sender, EventArgs e)
    {
        if (!_selectedTargetId.HasValue) return;

        FleetCommandResult result = _application.MergeFleet(
            _application.SessionToken, _fleetId, _selectedTargetId.Value);
        if (!result.Succeeded) return;

        FleetsMerged?.Invoke(this, EventArgs.Empty);
    }
}
