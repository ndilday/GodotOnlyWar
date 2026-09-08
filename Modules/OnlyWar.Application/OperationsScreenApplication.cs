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
using OnlyWar.Operations.Contracts;
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
    private readonly IOperationsPersonnelSurface _personnelSurface;
    private readonly IPersonnelAvailabilityQueries _personnelQueries;
    private readonly IReadinessDecisions _readiness;
    private readonly MedicalDetachmentService _medicalDetachments;

    // One redeemable undo at a time, matching the screen's single UNDO affordance. It is discarded
    // whenever the session is replaced so a stale token can never reach a different campaign.
    private OperationsUndo _undo;

    private sealed record OperationsUndo(
        Guid Id, Guid SessionToken, string Description, Func<OrderMutationResult> Action);

    public OperationsScreenApplication(CampaignApplicationContext context) : base(context)
    {
        _queries = new OperationsScreenQueries(context);
        _personnelSurface = Services.Operations.Personnel;
        _personnelQueries = Services.Operations.Availability;
        _readiness = Services.Readiness.Decisions;
        _medicalDetachments = new MedicalDetachmentService(_personnelSurface);
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
        Sector sector = ActiveSession.Sector;
        Region region = _queries.FindRegion(command.RegionId);
        AvailableMission mission = _queries.FindMission(region, command.MissionKey);
        if (region == null || mission == null)
            return OperationsCommandResult.Rejected("The target or mission is no longer available.");
        Order context = _queries.FindOrder(command.OrderId);

        List<PlayerSoldier> characters = _queries.ResolveCharacters(command.CharacterIds);
        if (characters.Count > 0)
        {
            // Selecting characters already committed to the order being edited releases them;
            // this is the same toggle the squad rows use, kept out of the screen.
            bool removing = context != null
                && characters.All(character => ReferenceEquals(character.CurrentOrder, context));
            OrderMutationResult characterResult = removing
                ? DetachSpecialists(sector, context, characters)
                : OrderMutationService.CreateOrAdd(
                    sector, region, mission, [], characters,
                    OperationsScreenQueries.ResolveTargetFactionId(region, mission),
                    context?.LevelOfAggression ?? command.Aggression,
                    _readiness, ActiveSession.CurrentDate, _personnelQueries);
            return Project(characterResult, undo: null);
        }

        List<Squad> squads = ResolveEligibleSquads(
            sector, region, mission, context, command.SquadIds);
        if (squads.Count == 0)
            return OperationsCommandResult.Rejected("Select at least one eligible squad.");

        bool created = context == null;
        OrderMutationResult result = OrderMutationService.CreateOrAdd(
            sector, region, mission, squads,
            OperationsScreenQueries.ResolveTargetFactionId(region, mission),
            context?.LevelOfAggression ?? command.Aggression,
            _readiness, ActiveSession.CurrentDate, _personnelQueries);
        if (!result.Succeeded) return Project(result, undo: null);

        Order issued = result.Order;
        return Project(result, created
            ? Register("order creation", () => OrderMutationService.Cancel(sector, issued))
            : Register("squad addition", () => RemoveMany(sector, issued, squads)));
    }

    public OperationsCommandResult RemoveOrderSquad(RemoveOrderSquadCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = ActiveSession.Sector;
        Order order = _queries.FindOrder(command.OrderId);
        Squad squad = order?.AssignedSquads.FirstOrDefault(item => item.Id == command.SquadId);
        OrderMutationResult result = OrderMutationService.RemoveSquad(sector, order, squad);
        return Project(result, result.Succeeded
            ? Register("squad removal", () => OrderMutationService.RestoreSquad(
                sector, order, squad, _readiness, ActiveSession.CurrentDate, _personnelQueries))
            : null);
    }

    public OperationsCommandResult CancelOrder(CancelOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = ActiveSession.Sector;
        Order order = _queries.FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");

        // Capture the participants before the release so the undo restores the whole set as one
        // validated command rather than replaying individual assignments.
        OrderRestoreToken token = OrderMutationService.CaptureCancellationUndo(sector, order);
        OrderMutationResult result = OrderMutationService.Cancel(sector, order);
        return Project(result, result.Succeeded
            ? Register("order cancellation", () => OrderMutationService.Restore(
                sector, token, _readiness, ActiveSession.CurrentDate, _personnelQueries))
            : null);
    }

    public OperationsCommandResult SetOrderAggression(SetOrderAggressionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = ActiveSession.Sector;
        Order order = _queries.FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");
        Aggression previous = order.LevelOfAggression;
        OrderMutationResult result = OrderMutationService.SetAggression(
            sector, order, command.Aggression);
        return Project(result, result.Succeeded && previous != command.Aggression
            ? Register("aggression change",
                () => OrderMutationService.SetAggression(sector, order, previous))
            : null);
    }

    public OperationsCommandResult ToggleOrderSpecialist(ToggleOrderSpecialistCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = ActiveSession.Sector;
        Order order = _queries.FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");
        PlayerSoldier soldier = _queries.FindPlayerSoldier(command.SoldierId);
        bool attached = ReferenceEquals(soldier?.CurrentOrder, order);
        OrderMutationResult result = attached
            ? OrderMutationService.DetachSpecialist(sector, order, soldier)
            : OrderMutationService.AttachSpecialist(
                sector, order, soldier, _readiness, ActiveSession.CurrentDate, _personnelQueries);
        return Project(result, result.Succeeded
            ? Register(attached ? "specialist detachment" : "specialist attachment", () => attached
                ? OrderMutationService.AttachSpecialist(
                    sector, order, soldier, _readiness, ActiveSession.CurrentDate, _personnelQueries)
                : OrderMutationService.DetachSpecialist(sector, order, soldier))
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
        Sector sector = ActiveSession.Sector;
        Planet planet = _queries.FindPlanet(command.PlanetId);
        Region region = _queries.FindRegion(command.RegionId);
        List<Squad> squads = OperationsScreenQueries
            .OrbitingSquads(sector, planet)
            .Where(squad => command.SquadIds?.Contains(squad.Id) == true).ToList();
        return Project(PlanetForceMovementService.Land(
            sector, planet, region,
            new MovementParty(squads, _queries.ResolveCharacters(command.CharacterIds)),
            ActiveSession.CurrentDate, _personnelSurface));
    }

    public OperationsCommandResult EmbarkForce(EmbarkForceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = ActiveSession.Sector;
        Planet planet = _queries.FindPlanet(command.PlanetId);
        Region region = _queries.FindRegion(command.RegionId);
        List<Squad> squads = (OperationsScreenQueries.PlayerPresence(sector, region)
                ?.LandedSquads ?? [])
            .Where(squad => command.SquadIds?.Contains(squad.Id) == true).ToList();
        return Project(PlanetForceMovementService.Embark(
            sector, planet, region,
            OperationsScreenQueries.FindOrbitingShip(
                sector, planet, command.ShipId),
            new MovementParty(squads, _queries.ResolveCharacters(command.CharacterIds)),
            ActiveSession.CurrentDate, _personnelSurface));
    }

    public OperationsCommandResult DetachCasualties(DetachCasualtiesCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = ActiveSession.Sector;
        Planet planet = _queries.FindPlanet(command.PlanetId);
        MedicalDetachmentResult result = _medicalDetachments.DetachToOrbit(
            sector, planet, _queries.FindRegion(command.RegionId),
            OperationsScreenQueries.FindOrbitingShip(
                sector, planet, command.ShipId),
            _queries.ResolveCharacters(command.SoldierIds), ActiveSession.CurrentDate);
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

    private OrderMutationResult DetachSpecialists(
        Sector sector, Order order, IReadOnlyList<PlayerSoldier> characters)
    {
        int removed = characters.Count(character =>
            OrderMutationService.DetachSpecialist(sector, order, character).Succeeded);
        return new OrderMutationResult(
            removed == characters.Count,
            removed == characters.Count
                ? "Characters removed." : "Some characters could not be removed.",
            OrderMutationKind.SpecialistDetached, order, ReleasedSpecialists: removed);
    }

    private OrderMutationResult RemoveMany(Sector sector, Order order, IReadOnlyList<Squad> squads)
    {
        OrderMutationResult last = new(true, "Change undone.", Order: order);
        foreach (Squad squad in squads
            .Where(squad => ReferenceEquals(squad.CurrentOrders, order)).ToList())
        {
            last = OrderMutationService.RemoveSquad(sector, order, squad);
        }
        return last;
    }

    private List<Squad> ResolveEligibleSquads(
        Sector sector, Region region, AvailableMission mission, Order context,
        IReadOnlyList<int> squadIds)
    {
        if (squadIds == null || squadIds.Count == 0) return [];
        RegionalEligibilityResult eligibility = RegionalOrderEligibilityService.Build(
            sector, region, _readiness, mission, context);
        return eligibility.Groups.SelectMany(group => group.Candidates)
            .Concat(eligibility.Excluded)
            .Where(candidate => candidate.Exclusion == SquadEligibilityExclusion.None
                && !candidate.IsAssignedToContext
                && SpecialistAvailability.IsMissionSquadFormation(candidate.Squad)
                && squadIds.Contains(candidate.Squad.Id))
            .DistinctBy(candidate => candidate.Squad.Id)
            .Select(candidate => candidate.Squad)
            .ToList();
    }
}
