using System;
using System.Collections.Generic;
using OnlyWar.Models.Orders;

namespace OnlyWar.Application;

/// <summary>
/// The outcome of a Planetary Operations command. It carries only identifiers, display text and an
/// application-issued undo token; the screen never receives the mutated campaign objects.
/// </summary>
public sealed record OperationsCommandResult(
    bool Succeeded,
    string Message,
    int? OrderId = null,
    Guid? UndoToken = null,
    string UndoDescription = null)
{
    public static OperationsCommandResult Rejected(string message) => new(false, message);

    /// <summary>The campaign changed underneath a queued command; the screen must requery.</summary>
    public static OperationsCommandResult StaleSession { get; } =
        new(false, "The campaign changed. Reopen the operation and try again.");
}

/// <summary>
/// Adds or removes the named participants for the mission the screen is editing. The application
/// decides whether that means creating an order, reinforcing one, or releasing characters, so the
/// screen holds no order-formation rules.
/// </summary>
public sealed record OrderParticipantsCommand(
    Guid SessionToken,
    int RegionId,
    string MissionKey,
    IReadOnlyList<int> SquadIds,
    IReadOnlyList<int> CharacterIds,
    int? OrderId = null,
    Aggression Aggression = Aggression.Normal);

public sealed record RemoveOrderSquadCommand(Guid SessionToken, int OrderId, int SquadId);
public sealed record CancelOrderCommand(Guid SessionToken, int OrderId);
public sealed record SetOrderAggressionCommand(Guid SessionToken, int OrderId, Aggression Aggression);
public sealed record ToggleOrderSpecialistCommand(Guid SessionToken, int OrderId, int SoldierId);

/// <summary>
/// Redeems the token returned by the last successful command. The token is bound to the session
/// that issued it, so an undo queued before a load or new game is rejected rather than applied to
/// an unrelated campaign that happens to reuse the same entity IDs.
/// </summary>
public sealed record UndoOperationsCommand(Guid SessionToken, Guid UndoToken);

public sealed record LandForceCommand(
    Guid SessionToken,
    int PlanetId,
    int RegionId,
    IReadOnlyList<int> SquadIds,
    IReadOnlyList<int> CharacterIds);

public sealed record EmbarkForceCommand(
    Guid SessionToken,
    int PlanetId,
    int RegionId,
    int ShipId,
    IReadOnlyList<int> SquadIds,
    IReadOnlyList<int> CharacterIds);

public sealed record DetachCasualtiesCommand(
    Guid SessionToken,
    int PlanetId,
    int RegionId,
    int ShipId,
    IReadOnlyList<int> SoldierIds);

/// <summary>The participants a force-tree row key stands for, resolved against the live force.</summary>
public sealed record OperationsSelection(
    IReadOnlyList<int> SquadIds,
    IReadOnlyList<int> CharacterIds)
{
    public bool IsEmpty => SquadIds.Count == 0 && CharacterIds.Count == 0;
}

/// <summary>What cancelling an order would release, for the screen's confirmation prompt.</summary>
public sealed record OrderCancellationPrompt(
    bool Exists,
    string OrderLabel,
    int SquadCount,
    int SpecialistCount);

/// <summary>
/// The read boundary for Planetary Operations. Query implementations resolve the currently
/// installed campaign and return detached projections; they do not expose campaign entities.
/// </summary>
public interface IOperationsScreenQueries
{
    Guid SessionToken { get; }

    OperationsWorkspaceView QueryOperations(OperationsWorkspaceQuery query);
    WorldDossierView QueryWorldDossier(int planetId, int regionId);
    IReadOnlyList<DossierCardView> QueryRegionCards(int regionId);
    OperationsEntryView QueryEntry(int planetId, int? regionId);
    OperationsEntryView QueryGovernorRequestEntry(int planetId);
    int? FindPlanetSquad(int planetId, int squadId);
    OperationsSelection ResolveForceSelection(OperationsWorkspaceQuery query, string key);

    /// <summary>
    /// The active order that already represents this mission in this region, if any. Matching a
    /// mission to an order involves the same target-faction rule the issue command uses, so the
    /// screen asks rather than recomputing it.
    /// </summary>
    int? FindOrderForMission(int regionId, string missionKey);
    OrderCancellationPrompt DescribeOrderCancellation(int orderId);
}

/// <summary>
/// The Planetary Operations application boundary. Every command resolves the live campaign at
/// execution and validates the caller's session token first. Read behavior is supplied by the
/// narrower <see cref="IOperationsScreenQueries"/> boundary.
/// </summary>
public interface IOperationsScreenApplication : IOperationsScreenQueries
{
    event EventHandler SessionChanged;

    OperationsCommandResult SetOrderParticipants(OrderParticipantsCommand command);
    OperationsCommandResult RemoveOrderSquad(RemoveOrderSquadCommand command);
    OperationsCommandResult CancelOrder(CancelOrderCommand command);
    OperationsCommandResult SetOrderAggression(SetOrderAggressionCommand command);
    OperationsCommandResult ToggleOrderSpecialist(ToggleOrderSpecialistCommand command);
    OperationsCommandResult UndoLastOperation(UndoOperationsCommand command);

    OperationsCommandResult LandForce(LandForceCommand command);
    OperationsCommandResult EmbarkForce(EmbarkForceCommand command);
    OperationsCommandResult DetachCasualties(DetachCasualtiesCommand command);
}
