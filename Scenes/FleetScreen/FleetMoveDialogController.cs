using Godot;
using OnlyWar.Application;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FleetMoveDialogController : DialogController
{
    private FleetMoveDialogView _view;
    private IFleetScreenApplication _application;
    private int _fleetId;
    private int? _selectedDestinationId;

    public event EventHandler CoursePlotted;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetMoveDialogView>("FleetMoveDialogView");
        _view.DestinationSelected += OnDestinationSelected;
        _view.PlotCoursePressed += OnPlotCoursePressed;
    }

    public void Configure(IFleetScreenApplication application)
    {
        _application = application;
    }

    public void SetTaskForce(int fleetId)
    {
        _fleetId = fleetId;
        _selectedDestinationId = null;

        FleetMoveOptionsView options = _application.QueryFleetMoveOptions(fleetId);
        _view.SetHeader(options.Header);
        _view.PopulateDestinations(options.Destinations
            .Select(destination => new KeyValuePair<int, string>(
                destination.PlanetId, destination.Name))
            .ToList());
    }

    private void OnDestinationSelected(object sender, int planetId)
    {
        FleetRouteView route = _application.QueryFleetRoute(_fleetId, planetId);
        _selectedDestinationId = route.IsAvailable ? planetId : null;
        _view.SetRouteDetail(route.Description, route.IsAvailable);
    }

    private void OnPlotCoursePressed(object sender, EventArgs e)
    {
        if (!_selectedDestinationId.HasValue) return;

        FleetCommandResult result = _application.PlotCourse(
            _application.SessionToken, _fleetId, _selectedDestinationId.Value);
        if (!result.Succeeded) return;

        CoursePlotted?.Invoke(this, EventArgs.Empty);
    }
}
