using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>
/// Token and lifecycle adapter for the Classis screen. Fleet state and mutation rules stay in the
/// focused FleetScreenContext; this public service exposes only detached views and command results.
/// </summary>
public sealed class FleetScreenApplication : CampaignScreenApplication, IFleetScreenApplication
{
    private const string NoCampaignMessage = "No campaign is active.";
    private const string StaleSessionMessage = "This campaign is no longer active.";

    private FleetScreenContext Screen => Context.FleetScreen;

    public FleetScreenApplication(CampaignApplicationContext context) : base(context) { }

    public FleetRosterView QueryFleetScreen() =>
        new(SessionToken, Screen?.QueryFleetTree() ?? []);

    public bool CanTransferSquadToShip(int squadId, int shipId) =>
        Screen?.CanTransferSquadToShip(squadId, shipId) == true;

    public bool CanTransferUnitToShip(int unitId, int sourceShipId, int destinationShipId) =>
        Screen?.CanTransferUnitToShip(unitId, sourceShipId, destinationShipId) == true;

    public FleetCommandResult TransferSquadToShip(Guid sessionToken, int squadId, int shipId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;
        return Screen.TransferSquadToShip(squadId, shipId);
    }

    public FleetCommandResult TransferUnitToShip(
        Guid sessionToken, int unitId, int sourceShipId, int destinationShipId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;
        return Screen.TransferUnitToShip(unitId, sourceShipId, destinationShipId);
    }

    public FleetActionAvailability QueryFleetActions(int fleetId) =>
        Screen?.QueryFleetActions(fleetId) ?? FleetActionAvailability.None;

    public FleetLocationView QueryFleetLocation(int fleetId) =>
        Screen?.QueryFleetLocation(fleetId) ?? new FleetLocationView(false, null);

    public FleetMoveOptionsView QueryFleetMoveOptions(int fleetId) =>
        Screen?.QueryFleetMoveOptions(fleetId) ?? FleetMoveOptionsView.Unavailable;

    public FleetRouteView QueryFleetRoute(int fleetId, int destinationPlanetId) =>
        Screen?.QueryFleetRoute(fleetId, destinationPlanetId) ?? FleetRouteView.Unavailable;

    public FleetCommandResult PlotCourse(Guid sessionToken, int fleetId, int destinationPlanetId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;
        return Screen.PlotCourse(fleetId, destinationPlanetId);
    }

    public FleetDivideOptionsView QueryFleetDivideOptions(int fleetId) =>
        Screen?.QueryFleetDivideOptions(fleetId) ?? FleetDivideOptionsView.Unavailable;

    public FleetDivideSelectionView EvaluateDivideSelection(
        int fleetId, IReadOnlyList<int> shipIds) =>
        Screen?.EvaluateDivideSelection(fleetId, shipIds)
        ?? new FleetDivideSelectionView(false, string.Empty);

    public FleetCommandResult DivideFleet(
        Guid sessionToken, int fleetId, IReadOnlyList<int> shipIds)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;
        return Screen.DivideFleet(fleetId, shipIds);
    }

    public FleetMergeOptionsView QueryFleetMergeOptions(int fleetId) =>
        Screen?.QueryFleetMergeOptions(fleetId) ?? FleetMergeOptionsView.Unavailable;

    public FleetMergeSelectionView EvaluateMergeSelection(int fleetId, int targetFleetId) =>
        Screen?.EvaluateMergeSelection(fleetId, targetFleetId)
        ?? new FleetMergeSelectionView(false, string.Empty);

    public FleetCommandResult MergeFleet(Guid sessionToken, int fleetId, int targetFleetId)
    {
        if (RejectFleetCommand(sessionToken) is FleetCommandResult rejection) return rejection;
        return Screen.MergeFleet(fleetId, targetFleetId);
    }

    private FleetCommandResult RejectFleetCommand(Guid sessionToken)
    {
        if (Screen == null) return FleetCommandResult.Failed(NoCampaignMessage);
        if (!IsCurrentSession(sessionToken)) return FleetCommandResult.Failed(StaleSessionMessage);
        return null;
    }
}
