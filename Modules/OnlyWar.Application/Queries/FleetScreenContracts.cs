using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>The player's task forces as a detached transfer tree.</summary>
public sealed record FleetRosterView(Guid SessionToken, IReadOnlyList<TreeNode> Fleets);

/// <summary>The outcome of one fleet command, with the wording the screen shows on failure.</summary>
public sealed record FleetCommandResult(bool Succeeded, string Message = null)
{
    public static FleetCommandResult Ok() => new(true);
    public static FleetCommandResult Failed(string message) => new(false, message);
}

/// <summary>
/// Which re-tasking actions a task force currently offers. A fleet already in transit cannot
/// change course or be reorganized until it arrives, and only player fleets are actionable.
/// </summary>
public sealed record FleetActionAvailability(
    bool IsActionable,
    bool CanPlotCourse,
    bool CanDivide,
    bool CanMerge)
{
    public static readonly FleetActionAvailability None = new(false, false, false, false);
}

/// <summary>Where a task force sits, for the screen that has to open its world.</summary>
public sealed record FleetLocationView(bool IsActionable, int? PlanetId);

public sealed record FleetDestinationOption(int PlanetId, string Name);

public sealed record FleetMoveOptionsView(
    bool IsAvailable,
    string Header,
    IReadOnlyList<FleetDestinationOption> Destinations)
{
    public static readonly FleetMoveOptionsView Unavailable = new(false, string.Empty, []);
}

/// <summary>The chosen route, already described; the screen prints it and enables the button.</summary>
public sealed record FleetRouteView(bool IsAvailable, string Description)
{
    public static readonly FleetRouteView Unavailable = new(false, string.Empty);
}

public sealed record FleetShipOption(int ShipId, string Label);

public sealed record FleetDivideOptionsView(
    bool IsAvailable,
    string Header,
    IReadOnlyList<FleetShipOption> Ships)
{
    public static readonly FleetDivideOptionsView Unavailable = new(false, string.Empty, []);
}

/// <summary>
/// Whether the ships picked so far form a legal division, and the sentence explaining it. The
/// "at least one ship must remain" rule is decided here, not by the dialog.
/// </summary>
public sealed record FleetDivideSelectionView(bool CanDivide, string Detail);

public sealed record FleetMergeTargetOption(int FleetId, string Label);

public sealed record FleetMergeOptionsView(
    bool IsAvailable,
    string Header,
    IReadOnlyList<FleetMergeTargetOption> Targets)
{
    public static readonly FleetMergeOptionsView Unavailable = new(false, string.Empty, []);
}

public sealed record FleetMergeSelectionView(bool CanMerge, string Detail);

/// <summary>
/// The Classis screen and the three fleet dialogs. Every ship, squad and task force is named by
/// id; transfer legality, route planning and the divide/merge rules live behind this boundary.
/// </summary>
public interface IFleetScreenApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    FleetRosterView QueryFleetScreen();

    bool CanTransferSquadToShip(int squadId, int shipId);

    bool CanTransferUnitToShip(int unitId, int sourceShipId, int destinationShipId);

    FleetCommandResult TransferSquadToShip(Guid sessionToken, int squadId, int shipId);

    FleetCommandResult TransferUnitToShip(
        Guid sessionToken, int unitId, int sourceShipId, int destinationShipId);

    /// <summary>Which context-menu entries a task force offers, and whether it is actionable.</summary>
    FleetActionAvailability QueryFleetActions(int fleetId);

    FleetLocationView QueryFleetLocation(int fleetId);

    FleetMoveOptionsView QueryFleetMoveOptions(int fleetId);

    FleetRouteView QueryFleetRoute(int fleetId, int destinationPlanetId);

    FleetCommandResult PlotCourse(Guid sessionToken, int fleetId, int destinationPlanetId);

    FleetDivideOptionsView QueryFleetDivideOptions(int fleetId);

    FleetDivideSelectionView EvaluateDivideSelection(int fleetId, IReadOnlyList<int> shipIds);

    FleetCommandResult DivideFleet(Guid sessionToken, int fleetId, IReadOnlyList<int> shipIds);

    FleetMergeOptionsView QueryFleetMergeOptions(int fleetId);

    FleetMergeSelectionView EvaluateMergeSelection(int fleetId, int targetFleetId);

    /// <summary>Folds the target task force into the one the player acted on.</summary>
    FleetCommandResult MergeFleet(Guid sessionToken, int fleetId, int targetFleetId);
}
