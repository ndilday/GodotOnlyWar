using Godot;
using OnlyWar.Helpers.Fleets;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FleetMoveDialogController : DialogController
{
    private FleetMoveDialogView _view;
    private TaskForce _taskForce;
    private Planet _selectedDestination;
    private FleetRoute _selectedRoute;

    public event EventHandler CoursePlotted;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetMoveDialogView>("FleetMoveDialogView");
        _view.DestinationSelected += OnDestinationSelected;
        _view.PlotCoursePressed += OnPlotCoursePressed;
    }

    public void SetTaskForce(TaskForce taskForce)
    {
        _taskForce = taskForce;
        _selectedDestination = null;
        _selectedRoute = null;

        _view.SetHeader($"Task Force {taskForce.Id} — Plot Course from {taskForce.Planet?.Name ?? "Unknown"}");

        Sector sector = GameDataSingleton.Instance.Sector;
        List<KeyValuePair<int, string>> destinations = sector.Planets.Values
            .Where(planet => planet != taskForce.Planet)
            .OrderBy(planet => FleetRouteCalculator.CalculateDistance(taskForce.Planet, planet))
            .Select(planet => new KeyValuePair<int, string>(planet.Id, planet.Name))
            .ToList();

        _view.PopulateDestinations(destinations);
    }

    private void OnDestinationSelected(object sender, int planetId)
    {
        Sector sector = GameDataSingleton.Instance.Sector;
        _selectedDestination = sector.Planets[planetId];

        ushort maxDiameter = GameDataSingleton.Instance.GameRulesData.MaxSubsectorCellDiameter;
        FleetRouteScope scope = FleetRouteCalculator.DetermineScope(
            _taskForce.Planet, _selectedDestination, maxDiameter);

        FleetRouteCalculator calculator = new FleetRouteCalculator();
        _selectedRoute = calculator.CalculateBestRoute(
            _taskForce.Planet, _selectedDestination, sector.WarpLanes, scope);

        _view.SetRouteDetail(BuildRouteDescription(_selectedDestination, _selectedRoute), true);
    }

    private static string BuildRouteDescription(Planet destination, FleetRoute route)
    {
        string routeType = route.RouteType == FleetRouteType.WarpLane ? "Warp Lane" : "Direct";
        string scope = route.Scope switch
        {
            FleetRouteScope.SameSubsector => "Same subsector",
            FleetRouteScope.AdjacentSubsector => "Adjacent subsector",
            _ => "Distant subsector"
        };
        string arrival = route.EstimatedMinTurns == route.EstimatedMaxTurns
            ? $"{route.EstimatedMinTurns} weeks"
            : $"{route.EstimatedMinTurns}–{route.EstimatedMaxTurns} weeks";

        return $"Destination: {destination.Name}\n"
            + $"Route: {routeType}\n"
            + $"Distance: {scope} ({route.TotalDistance:0.0} ly)\n"
            + $"Estimated transit: {arrival}";
    }

    private void OnPlotCoursePressed(object sender, EventArgs e)
    {
        if (_taskForce == null || _selectedDestination == null || _selectedRoute == null) return;

        _taskForce.OrderMoveTo(_selectedDestination, _selectedRoute);
        CoursePlotted?.Invoke(this, EventArgs.Empty);
    }
}
