using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Personnel;

namespace OnlyWar.Application;

/// <summary>
/// Planetary Operations command service. It owns only command orchestration, validation and the
/// single-use undo token; all screen reads are delegated to <see cref="OperationsScreenQueries"/>.
/// </summary>
public sealed class OperationsScreenApplication : CampaignScreenApplication,
    IOperationsScreenApplication
{
    private readonly OperationsScreenQueries _queries;
    private OperationsCommandContext Command => Context.OperationsCommand;

    // One redeemable undo at a time, matching the screen's single UNDO affordance. It is discarded
    // whenever the session is replaced so a stale token can never reach a different campaign.
    private OperationsUndo _undo;

    private sealed record OperationsUndo(
        Guid Id, Guid SessionToken, string Description, Func<OrderMutationResult> Action);

    public OperationsScreenApplication(CampaignApplicationContext context) : base(context)
    {
        _queries = new OperationsScreenQueries(context);
        Context.SessionChanged += OnSessionChanged;
    }

    public IOperationsScreenQueries Queries => _queries;

    private void OnSessionChanged(object sender, EventArgs args) => _undo = null;

    // IOperationsScreenQueries
    public OperationsWorkspaceView QueryOperations(OperationsWorkspaceQuery query) =>
        _queries.QueryOperations(query);

    public WorldDossierView QueryWorldDossier(int planetId, int regionId) =>
        _queries.QueryWorldDossier(planetId, regionId);

    public IReadOnlyList<DossierCardView> QueryRegionCards(int regionId) =>
        _queries.QueryRegionCards(regionId);

    public OperationsEntryView QueryEntry(int planetId, int? regionId) =>
        _queries.QueryEntry(planetId, regionId);

    public OperationsEntryView QueryGovernorRequestEntry(int planetId) =>
        _queries.QueryGovernorRequestEntry(planetId);

    public int? FindPlanetSquad(int planetId, int squadId) =>
        _queries.FindPlanetSquad(planetId, squadId);

    public OperationsSelection ResolveForceSelection(
        OperationsWorkspaceQuery query, string key) =>
        _queries.ResolveForceSelection(query, key);

    public int? FindOrderForMission(int regionId, string missionKey) =>
        _queries.FindOrderForMission(regionId, missionKey);

    public OrderCancellationPrompt DescribeOrderCancellation(int orderId) =>
        _queries.DescribeOrderCancellation(orderId);

    // Commands -------------------------------------------------------------------------------

    public OperationsCommandResult SetOrderParticipants(OrderParticipantsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Region region = commandContext.FindRegion(command.RegionId);
        AvailableMission mission = commandContext.FindMission(region, command.MissionKey);
        if (region == null || mission == null)
            return OperationsCommandResult.Rejected("The target or mission is no longer available.");
        Order context = commandContext.FindOrder(command.OrderId);

        List<PlayerSoldier> characters = commandContext.ResolveCharacters(command.CharacterIds);
        if (characters.Count > 0)
        {
            // Selecting characters already committed to the order being edited releases them;
            // this is the same toggle the squad rows use, kept out of the screen.
            bool removing = context != null
                && characters.All(character => ReferenceEquals(character.CurrentOrder, context));
            OrderMutationResult characterResult = removing
                ? commandContext.DetachSpecialists(context, characters)
                : commandContext.CreateOrAdd(
                    region, mission, [], characters,
                    OperationsCommandContext.ResolveTargetFactionId(region, mission),
                    context?.LevelOfAggression ?? command.Aggression);
            return Project(characterResult, undo: null);
        }

        List<Squad> squads = commandContext.ResolveEligibleSquads(
            region, mission, context, command.SquadIds);
        if (squads.Count == 0)
            return OperationsCommandResult.Rejected("Select at least one eligible squad.");

        bool created = context == null;
        OrderMutationResult result = commandContext.CreateOrAdd(
            region, mission, squads, [],
            OperationsCommandContext.ResolveTargetFactionId(region, mission),
            context?.LevelOfAggression ?? command.Aggression);
        if (!result.Succeeded) return Project(result, undo: null);

        Order issued = result.Order;
        return Project(result, created
            ? Register("order creation", () => commandContext.Cancel(issued))
            : Register("squad addition", () => commandContext.RemoveMany(issued, squads)));
    }

    public OperationsCommandResult RemoveOrderSquad(RemoveOrderSquadCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Order order = commandContext.FindOrder(command.OrderId);
        Squad squad = order?.AssignedSquads.FirstOrDefault(item => item.Id == command.SquadId);
        OrderMutationResult result = commandContext.RemoveSquad(order, squad);
        return Project(result, result.Succeeded
            ? Register("squad removal", () => commandContext.RestoreSquad(order, squad))
            : null);
    }

    public OperationsCommandResult CancelOrder(CancelOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Order order = commandContext.FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");

        // Capture the participants before the release so the undo restores the whole set as one
        // validated command rather than replaying individual assignments.
        OrderRestoreToken token = commandContext.CaptureCancellationUndo(order);
        OrderMutationResult result = commandContext.Cancel(order);
        return Project(result, result.Succeeded
            ? Register("order cancellation", () => commandContext.Restore(token))
            : null);
    }

    public OperationsCommandResult SetOrderAggression(SetOrderAggressionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Order order = commandContext.FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");
        Aggression previous = order.LevelOfAggression;
        OrderMutationResult result = commandContext.SetAggression(order, command.Aggression);
        return Project(result, result.Succeeded && previous != command.Aggression
            ? Register("aggression change",
                () => commandContext.SetAggression(order, previous))
            : null);
    }

    public OperationsCommandResult ToggleOrderSpecialist(ToggleOrderSpecialistCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Order order = commandContext.FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");
        PlayerSoldier soldier = commandContext.FindPlayerSoldier(command.SoldierId);
        bool attached = ReferenceEquals(soldier?.CurrentOrder, order);
        OrderMutationResult result = attached
            ? commandContext.DetachSpecialist(order, soldier)
            : commandContext.AttachSpecialist(order, soldier);
        return Project(result, result.Succeeded
            ? Register(attached ? "specialist detachment" : "specialist attachment", () => attached
                ? commandContext.AttachSpecialist(order, soldier)
                : commandContext.DetachSpecialist(order, soldier))
            : null);
    }

    public OperationsCommandResult UndoLastOperation(UndoOperationsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        OperationsUndo undo = _undo;
        _undo = null;
        if (!IsCurrent(command.SessionToken)
            || undo == null
            || undo.Id != command.UndoToken
            || undo.SessionToken != SessionToken)
        {
            return OperationsCommandResult.StaleSession;
        }
        OrderMutationResult result = undo.Action();
        return new OperationsCommandResult(
            result.Succeeded,
            result.Succeeded ? $"Undid {undo.Description}." : result.Message,
            result.Order?.Id);
    }

    public OperationsCommandResult LandForce(LandForceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Planet planet = commandContext.FindPlanet(command.PlanetId);
        Region region = commandContext.FindRegion(command.RegionId);
        List<Squad> squads = commandContext
            .OrbitingSquads(planet)
            .Where(squad => command.SquadIds?.Contains(squad.Id) == true).ToList();
        return Project(commandContext.Land(
            planet, region,
            new MovementParty(squads, commandContext.ResolveCharacters(command.CharacterIds))));
    }

    public OperationsCommandResult EmbarkForce(EmbarkForceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Planet planet = commandContext.FindPlanet(command.PlanetId);
        Region region = commandContext.FindRegion(command.RegionId);
        List<Squad> squads = (commandContext.PlayerPresence(region)
                ?.LandedSquads ?? [])
            .Where(squad => command.SquadIds?.Contains(squad.Id) == true).ToList();
        return Project(commandContext.Embark(
            planet, region,
            commandContext.FindOrbitingShip(planet, command.ShipId),
            new MovementParty(squads, commandContext.ResolveCharacters(command.CharacterIds))));
    }

    public OperationsCommandResult DetachCasualties(DetachCasualtiesCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        OperationsCommandContext commandContext = Command;
        Planet planet = commandContext.FindPlanet(command.PlanetId);
        MedicalDetachmentResult result = commandContext.DetachCasualties(
            planet,
            commandContext.FindRegion(command.RegionId),
            commandContext.FindOrbitingShip(planet, command.ShipId),
            commandContext.ResolveCharacters(command.SoldierIds));
        return new OperationsCommandResult(result.Succeeded, result.Message);
    }

    private bool IsCurrent(Guid token) => IsCurrentSession(token);

    // The screen keeps an order selected only while the order still holds a force, matching the
    // pre-migration controller: a rejected command leaves the previous selection untouched.
    private OperationsCommandResult Project(OrderMutationResult result, OperationsUndo undo) =>
        new(result.Succeeded, result.Message,
            result.Succeeded && result.Order is { } order && !order.Force.IsEmpty
                ? order.Id : null,
            undo?.Id, undo?.Description);

    private static OperationsCommandResult Project(ForceMovementResult result) =>
        new(result.Succeeded, result.Message);

    private OperationsUndo Register(string description, Func<OrderMutationResult> action)
    {
        _undo = new OperationsUndo(Guid.NewGuid(), SessionToken, description, action);
        return _undo;
    }

}
