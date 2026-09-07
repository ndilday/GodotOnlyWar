using Godot;
using OnlyWar.Application;
using OnlyWar.Helpers.UI;
using System;
using System.Collections.Generic;

public partial class FleetScreenController : MainScreenController
{
    private IFleetScreenApplication _application;
    private FleetScreenView _view;

    public event EventHandler CampaignChanged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<FleetScreenView>("FleetScreenView");
        _view.CanTransferSquadToShip = CanTransferSquadToShip;
        _view.CanTransferUnitToShip = CanTransferUnitToShip;
        _view.SquadDroppedOnShip += OnSquadDroppedOnShip;
        _view.UnitDroppedOnShip += OnUnitDroppedOnShip;
        PopulateFleetData();
    }

    public void Configure(IFleetScreenApplication application)
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
        PopulateFleetData();
    }

    private void OnSessionChanged(object sender, EventArgs e)
    {
        PopulateFleetData();
    }

    public void PopulateFleetData(int? focusSquadId = null)
    {
        if (_view == null || _application == null) return;

        IReadOnlyList<TreeNode> fleetNodes = _application.QueryFleetScreen().Fleets;
        _view.PopulateFleetTree(fleetNodes);
        if (focusSquadId.HasValue)
        {
            _view.FocusSquad(focusSquadId.Value);
        }
    }

    private bool CanTransferSquadToShip(int squadId, int shipId) =>
        _application?.CanTransferSquadToShip(squadId, shipId) == true;

    private bool CanTransferUnitToShip(int unitId, int sourceShipId, int destinationShipId) =>
        _application?.CanTransferUnitToShip(unitId, sourceShipId, destinationShipId) == true;

    private void OnSquadDroppedOnShip(object sender, ValueTuple<int, int> args)
    {
        if (_application == null) return;
        FleetCommandResult result = _application.TransferSquadToShip(
            _application.SessionToken, args.Item1, args.Item2);
        if (!result.Succeeded) return;

        CampaignChanged?.Invoke(this, EventArgs.Empty);
        PopulateFleetData();
    }

    private void OnUnitDroppedOnShip(object sender, ValueTuple<int, int, int> args)
    {
        if (_application == null) return;
        FleetCommandResult result = _application.TransferUnitToShip(
            _application.SessionToken, args.Item1, args.Item2, args.Item3);
        if (!result.Succeeded) return;

        CampaignChanged?.Invoke(this, EventArgs.Empty);
        PopulateFleetData();
    }
}
