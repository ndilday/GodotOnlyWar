using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>One task force in orbit, as the inspector's list shows it.</summary>
public sealed record OrbitingFleetRow(int FleetId, string Label, bool IsPlayerFleet);

/// <summary>
/// Everything the system inspector prints for one selection. Which fleets are in orbit, which
/// one is chosen when the player has not chosen, whether its actions are legal and whether the
/// governor has an answerable request are all decided here.
/// </summary>
public sealed record SystemInspectorView(
    bool HasSystem,
    string SystemName,
    string ControlText,
    string OrbitDetailText,
    string RequestDetailText,
    bool HasAnswerableRequest,
    IReadOnlyList<OrbitingFleetRow> OrbitingFleets,
    int? SelectedFleetId,
    string SelectedFleetDetail,
    FleetActionAvailability SelectedFleetActions,
    WorldDossierView Dossier)
{
    public static readonly SystemInspectorView Empty = new(
        false, null, null, null, null, false, [], null, null,
        FleetActionAvailability.None, null);
}

public interface ISystemInspectorApplication
{
    Guid SessionToken { get; }
    event EventHandler SessionChanged;

    /// <summary>
    /// The inspector for one world. <paramref name="selectedFleetId"/> is the player's explicit
    /// pick; when it is absent or no longer in orbit the first actionable chapter fleet is used.
    /// </summary>
    SystemInspectorView QuerySystemInspector(
        int? planetId, int? selectedFleetId, bool includeDossier);

    /// <summary>Where a task force is shown: its orbit, else its origin or its destination.</summary>
    int? QueryFleetContextPlanet(int fleetId);
}
