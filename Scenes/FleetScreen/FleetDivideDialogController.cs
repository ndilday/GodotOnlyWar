using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FleetDivideDialogController : DialogController
{
    private FleetDivideDialogView _view;
    private IFleetScreenApplication _application;
    private int _fleetId;

    public event EventHandler FleetDivided;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetDivideDialogView>("FleetDivideDialogView");
        _view.SelectionChanged += OnSelectionChanged;
        _view.DividePressed += OnDividePressed;
    }

    public void Configure(IFleetScreenApplication application)
    {
        _application = application;
    }

    public void SetTaskForce(int fleetId)
    {
        _fleetId = fleetId;
        FleetDivideOptionsView options = _application.QueryFleetDivideOptions(fleetId);
        _view.SetHeader(options.Header);
        _view.PopulateShips(options.Ships
            .Select(ship => new KeyValuePair<int, string>(ship.ShipId, ship.Label))
            .ToList());
    }

    private void OnSelectionChanged(object sender, EventArgs e)
    {
        FleetDivideSelectionView selection = _application.EvaluateDivideSelection(
            _fleetId, _view.GetSelectedShipIds());
        _view.SetDetail(selection.Detail, selection.CanDivide);
    }

    private void OnDividePressed(object sender, EventArgs e)
    {
        FleetCommandResult result = _application.DivideFleet(
            _application.SessionToken, _fleetId, _view.GetSelectedShipIds());
        if (!result.Succeeded) return;

        FleetDivided?.Invoke(this, EventArgs.Empty);
    }
}
