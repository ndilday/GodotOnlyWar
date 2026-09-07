using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Contracts.Operations;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Application.Adapters.Operations;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.PlanetaryOperations;
using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Application;

/// <summary>
/// Planetary Operations command handling. Order formation, specialist attachment, movement and
/// casualty detachment resolve against the installed session here, so the screen supplies only
/// entity IDs and its session token.
/// </summary>
public sealed partial class CampaignApplication : IOperationsScreenApplication
{
    private readonly IOperationsPersonnelSurface _personnel = OperationsPersonnelSurface.Instance;
    private readonly MedicalDetachmentService _medicalDetachments =
        new(OperationsPersonnelSurface.Instance);
    private readonly IReadinessDecisions _readiness = MedicalReadinessDecisions.Instance;

    // One redeemable undo at a time, matching the screen's single UNDO affordance. It is discarded
    // whenever the session is replaced so a stale token can never reach a different campaign.
    private OperationsUndo _undo;

    private sealed record OperationsUndo(
        Guid Id, Guid SessionToken, string Description, Func<OrderMutationResult> Action);

    public OperationsWorkspaceView QueryOperations(OperationsWorkspaceQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Planet planet = FindPlanet(query.PlanetId);
        Region region = FindRegion(query.RegionId);
        if (_activeSession == null || planet == null || region?.Planet != planet)
        {
            return new OperationsWorkspaceView(
                SessionToken, false, null, null, -1, query.Verb, null, null, null);
        }

        Sector sector = _activeSession.Sector;
        OperationsScreenProjector projector = Projector();
        Order contextOrder = FindOrder(query.OrderId);

        RegionalOperationsView orders = null;
        MovementOperationsView movement = null;
        DetachOperationsView detach = null;
        switch (query.Verb)
        {
            case PlanetaryOperationsVerb.Order:
                orders = projector.BuildRegional(region, query.MissionKey, query.OrderId,
                    query.Filter, EnumerateSpecialists(sector, region, contextOrder));
                break;
            case PlanetaryOperationsVerb.Land:
            case PlanetaryOperationsVerb.Embark:
                movement = BuildMovement(projector, sector, planet, region, query);
                break;
            case PlanetaryOperationsVerb.Detach:
                detach = BuildDetach(projector, sector, planet, region, query);
                break;
        }

        return new OperationsWorkspaceView(
            SessionToken, true,
            projector.BuildHeader(planet),
            projector.BuildMap(planet, query.Overlay, query.FactionId),
            region.Id, query.Verb, orders, movement, detach);
    }

    public WorldDossierView QueryWorldDossier(int planetId, int regionId) =>
        _activeSession == null
            ? new WorldDossierView([], [], [])
            : Projector().BuildWorld(FindPlanet(planetId), FindRegion(regionId));

    public IReadOnlyList<DossierCardView> QueryRegionCards(int regionId) =>
        _activeSession == null ? [] : Projector().BuildRegionCards(FindRegion(regionId));

    /// <summary>
    /// The region the screen should open on, and the faction whose overlay it should show. Both
    /// are campaign questions (capital region, default-faction precedence), not screen state.
    /// </summary>
    public OperationsEntryView QueryEntry(int planetId, int? regionId)
    {
        Planet planet = FindPlanet(planetId);
        if (planet == null) return new OperationsEntryView(-1, -1, null);
        Region region = planet.Regions.FirstOrDefault(candidate => candidate?.Id == regionId)
            ?? planet.Regions.FirstOrDefault(candidate => candidate?.Id == planet.CapitalRegionId)
            ?? planet.Regions.FirstOrDefault();
        int? factionId = planet.PlanetFactionMap.Values
            .OrderBy(presence => presence.Faction.IsDefaultFaction ? 0
                : presence.Faction.IsPlayerFaction ? 1 : 2)
            .Select(presence => (int?)presence.Faction.Id).FirstOrDefault();
        return new OperationsEntryView(planet.Id, region?.Id ?? -1, factionId);
    }

    /// <summary>
    /// Where a governor's petition should drop the player: the capital, its Show of Force option
    /// and any order already covering it.
    /// </summary>
    public OperationsEntryView QueryGovernorRequestEntry(int planetId)
    {
        OperationsEntryView entry = QueryEntry(planetId, null);
        Region capital = FindRegion(entry.RegionId);
        if (capital == null) return entry;
        AvailableMission showOfForce = Projector().AvailableMissions(capital)
            .FirstOrDefault(option =>
                option.SpecialMission?.MissionType == MissionType.ShowOfForce);
        return entry with
        {
            MissionKey = showOfForce?.IdentityKey,
            OrderId = showOfForce == null
                ? null : FindOrderForMission(capital.Id, showOfForce.IdentityKey)
        };
    }

    /// <summary>The squad a double-click on a force row should open, if it is still present.</summary>
    public int? FindPlanetSquad(int planetId, int squadId) =>
        FindPlanet(planetId)?.Regions.Where(region => region != null)
            .SelectMany(region => region.RegionFactionMap.Values)
            .SelectMany(presence => presence.LandedSquads)
            .Concat(FindPlanet(planetId).OrbitingTaskForceList
                .SelectMany(fleet => fleet.Ships).SelectMany(ship => ship.LoadedSquads))
            .FirstOrDefault(squad => squad.Id == squadId)?.Id;

    private OperationsScreenProjector Projector() => new(
        _activeSession.Sector, _activeSession.CurrentDate, _readiness);

    private MovementOperationsView BuildMovement(
        OperationsScreenProjector projector, Sector sector, Planet planet, Region region,
        OperationsWorkspaceQuery query)
    {
        bool landing = query.Verb == PlanetaryOperationsVerb.Land;
        List<ForceTreeSquad> roster = landing
            ? PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction)
                .SelectMany(ship => ship.LoadedSquads
                    .Where(squad => squad?.IsPresentOperationalForce == true)
                    .Select(squad => new ForceTreeSquad(squad, ship.Name, ship))).ToList()
            : (PlayerPresence(sector, region)?.LandedSquads ?? [])
                .Where(squad => squad?.IsPresentOperationalForce == true)
                .Select(squad => new ForceTreeSquad(squad, region.Name)).ToList();

        Ship destinationShip = landing
            ? null : FindOrbitingShip(sector, planet, query.SelectedShipId ?? -1);
        IReadOnlyList<SpecialistOption> characters = EnumerateMovableCharacters(
            sector, planet, region, landing, destinationShip);

        HashSet<int> validSquadIds = roster.Select(item => item.Squad.Id).ToHashSet();
        HashSet<int> validCharacterIds = characters.Where(option => option.IsAvailable)
            .Select(option => option.Soldier.Id).ToHashSet();
        HashSet<int> selectedSquadIds = (query.MovementSquadIds ?? new HashSet<int>())
            .Where(validSquadIds.Contains).ToHashSet();
        HashSet<int> selectedCharacterIds = (query.MovementCharacterIds ?? new HashSet<int>())
            .Where(validCharacterIds.Contains).ToHashSet();

        IReadOnlyList<ShipChoiceView> ships = landing
            ? []
            : ProjectShips(PlanetForceMovementService.BuildCapacityChoices(
                planet, sector.PlayerForce.Faction,
                new MovementParty(
                    roster.Where(item => selectedSquadIds.Contains(item.Squad.Id))
                        .Select(item => item.Squad).ToList(),
                    characters.Where(option => selectedCharacterIds.Contains(option.Soldier.Id))
                        .Select(option => option.Soldier).ToList()),
                _personnel));
        int? selectedShipId = query.SelectedShipId is int shipId
            && ships.Any(choice => choice.ShipId == shipId && choice.Fits) ? shipId : null;

        IReadOnlyList<HierarchyTreeItem> tree = PlanetaryForceTreeBuilder
            .Build(roster, landing ? query.Grouping : ForceTreeGrouping.Company,
                query.Filter, selectedSquadIds, TreeInputs(sector))
            .Concat(PlanetaryForceTreeBuilder.BuildCharacterGroup(characters, selectedCharacterIds))
            .ToList();

        return new MovementOperationsView(
            projector.BuildRegionCards(region), tree, ships, selectedShipId,
            selectedSquadIds.Count + selectedCharacterIds.Count,
            validSquadIds, validCharacterIds);
    }

    private DetachOperationsView BuildDetach(
        OperationsScreenProjector projector, Sector sector, Planet planet, Region region,
        OperationsWorkspaceQuery query)
    {
        List<PlayerSoldier> casualties = (PlayerPresence(sector, region)?.LandedSquads ?? [])
            .SelectMany(SoldierPresenceService.PresentMembers).OfType<PlayerSoldier>()
            .Where(soldier => soldier.IsWounded && soldier.IndividualPosting == null)
            .DistinctBy(soldier => soldier.Id)
            .OrderBy(soldier => soldier.AssignedSquad?.ParentUnit?.Name)
            .ThenBy(soldier => soldier.Name).ToList();
        HashSet<int> validIds = casualties.Select(soldier => soldier.Id).ToHashSet();
        int selectedCount = (query.CasualtyIds ?? new HashSet<int>()).Count(validIds.Contains);

        IReadOnlyList<ShipChoiceView> ships = PlanetForceMovementService
            .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction)
            .Select(ship => new ShipChoiceView(
                ship.Id, ship.Name, ship.Fleet?.Id ?? -1, FleetName(ship),
                ship.LoadedSoldierCount, selectedCount,
                ship.LoadedSoldierCount + selectedCount, ship.Template.SoldierCapacity,
                Math.Max(0, selectedCount - ship.AvailableCapacity))).ToList();
        int? selectedShipId = query.SelectedShipId is int shipId
            && ships.Any(choice => choice.ShipId == shipId && choice.Fits) ? shipId : null;

        return new DetachOperationsView(
            projector.BuildRegionCards(region),
            casualties.Select(soldier => new CasualtyRowView(
                soldier.Id, soldier.Name, soldier.AssignedSquad?.Name)).ToList(),
            ships, selectedShipId, validIds);
    }

    private static IReadOnlyList<ShipChoiceView> ProjectShips(
        IReadOnlyList<ShipCapacityChoice> choices) =>
        choices.Select(choice => new ShipChoiceView(
            choice.Ship.Id, choice.Ship.Name, choice.Ship.Fleet?.Id ?? -1, FleetName(choice.Ship),
            choice.CurrentPassengers, choice.SelectedPassengers, choice.ResultingPassengers,
            choice.Capacity, choice.Shortfall)).ToList();

    private static string FleetName(Ship ship) =>
        ship.Fleet == null ? "UNASSIGNED SHIPS" : $"TASK FORCE {ship.Fleet.Id}";

    private static ForceTreeInputs TreeInputs(Sector sector) => new(
        sector?.PlayerForce?.RecruitmentProgram,
        sector?.PlayerForce?.Army?.ChapterOperationalDoctrine);

    /// <summary>
    /// Who may be lent to an operation staged out of this region. Availability is Operations
    /// policy; the screen only renders the resulting rows.
    /// </summary>
    private IReadOnlyList<SpecialistOption> EnumerateSpecialists(
        Sector sector, Region region, Order contextOrder)
    {
        if (region == null) return [];
        IEnumerable<PlayerSoldier> roster = sector.PlayerForce?.Army?.PlayerSoldierMap?.Values
            ?? Enumerable.Empty<PlayerSoldier>();
        // SpecialistAvailability is also given the chapter roster. Build it from every valid origin
        // so locally staged characters are included, then apply the same target-or-adjacent region
        // boundary used by the squad roster. Characters outside that operational area should not
        // appear in Order mode at all, even as unavailable rows.
        return region.GetSelfAndAdjacentRegions()
            .Select(candidate => PlayerPresence(sector, candidate))
            .Where(presence => presence != null)
            .SelectMany(presence => SpecialistAvailability.EnumerateRoster(
                presence, region, roster, _readiness, contextOrder, _personnel))
            .Where(option => IsInOrderArea(option?.Soldier, region))
            .GroupBy(option => option.Soldier.Id)
            .Select(group => group.First())
            .OrderBy(option => option.HomeSquad?.Name)
            .ThenBy(option => option.Soldier.Name)
            .ToList();
    }

    private static bool IsInOrderArea(PlayerSoldier soldier, Region target)
    {
        Region location = CampaignLocationService.ForSoldier(soldier)?.Region;
        return target != null && location != null
            && target.GetSelfAndAdjacentRegions().Contains(location);
    }

    private IReadOnlyList<SpecialistOption> EnumerateMovableCharacters(
        Sector sector, Planet planet, Region region, bool landing, Ship destinationShip)
    {
        if (sector?.PlayerForce?.Army?.PlayerSoldierMap == null) return [];
        CampaignLocation destination = landing
            ? CampaignLocation.Landed(region)
            : destinationShip == null ? null : CampaignLocation.Aboard(destinationShip);
        IReadOnlyList<Ship> orbiting = PlanetForceMovementService
            .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction);
        return sector.PlayerForce.Army.PlayerSoldierMap.Values
            .Where(character => character.AssignedSquad?.PermitsIndividualDeployment == true)
            .Where(character =>
            {
                CampaignLocation location = CampaignLocationService.ForSoldier(character);
                return landing
                    ? location?.Ship != null && orbiting.Contains(location.Ship)
                    : location?.Region == region;
            })
            .Select(character =>
            {
                PersonnelAvailabilityDecision decision = destination == null
                    ? new PersonnelAvailabilityDecision(false,
                        (int)PersonnelAvailabilityReasonCode.MissingLocation,
                        "Choose a destination ship.")
                    : _personnel.EvaluateMovement(character, destination);
                return new SpecialistOption(
                    character,
                    character.AssignedSquad,
                    decision.IsAllowed
                        ? CampaignLocationService.Format(
                            CampaignLocationService.ForSoldier(character))
                        : decision.Reason,
                    decision.IsAllowed);
            })
            .OrderBy(option => option.HomeSquad?.Name)
            .ThenBy(option => option.Soldier.Name)
            .ToList();
    }

    /// <summary>Resolves a force-tree row key to the participants the screen just activated.</summary>
    public OperationsSelection ResolveForceSelection(
        OperationsWorkspaceQuery query, string key)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_activeSession == null || string.IsNullOrWhiteSpace(key))
            return new OperationsSelection([], []);
        Sector sector = _activeSession.Sector;
        Planet planet = FindPlanet(query.PlanetId);
        Region region = FindRegion(query.RegionId);
        if (planet == null || region == null) return new OperationsSelection([], []);

        bool landing = query.Verb == PlanetaryOperationsVerb.Land;
        IReadOnlyList<SpecialistOption> characters =
            query.Verb == PlanetaryOperationsVerb.Order
                ? EnumerateSpecialists(sector, region, FindOrder(query.OrderId))
                : EnumerateMovableCharacters(sector, planet, region, landing,
                    landing ? null : FindOrbitingShip(sector, planet, query.SelectedShipId ?? -1));
        List<int> characterIds = PlanetaryForceTreeBuilder
            .ResolveCharacterSelection(characters, key)
            .Select(character => character.Id).ToList();
        if (characterIds.Count > 0) return new OperationsSelection([], characterIds);

        List<ForceTreeSquad> roster;
        if (query.Verb == PlanetaryOperationsVerb.Order)
        {
            AvailableMission mission = FindMission(region, query.MissionKey);
            roster = OperationsScreenProjector.BuildOrderTreeRoster(
                RegionalOrderEligibilityService.Build(
                    sector, region, _readiness, mission, FindOrder(query.OrderId)));
            return new OperationsSelection(
                PlanetaryForceTreeBuilder.ResolveSelection(roster, key)
                    .Where(squad => roster.Any(item =>
                        item.Squad == squad && item.Selectable))
                    .Select(squad => squad.Id).ToList(),
                []);
        }

        roster = landing
            ? PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction)
                .SelectMany(ship => ship.LoadedSquads
                    .Select(squad => new ForceTreeSquad(squad, ship.Name, ship))).ToList()
            : (PlayerPresence(sector, region)?.LandedSquads ?? [])
                .Select(squad => new ForceTreeSquad(squad, region.Name)).ToList();
        return new OperationsSelection(
            PlanetaryForceTreeBuilder.ResolveSelection(roster, key)
                .Select(squad => squad.Id).ToList(),
            []);
    }

    public int? FindOrderForMission(int regionId, string missionKey)
    {
        if (_activeSession == null) return null;
        Region region = FindRegion(regionId);
        AvailableMission mission = FindMission(region, missionKey);
        if (mission == null) return null;
        return OrderMutationService.FindEquivalentOrder(
            _activeSession.Sector, region, mission,
            ResolveTargetFactionId(region, mission))?.Id;
    }

    public OperationsCommandResult SetOrderParticipants(OrderParticipantsCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = _activeSession.Sector;
        Region region = FindRegion(command.RegionId);
        AvailableMission mission = FindMission(region, command.MissionKey);
        if (region == null || mission == null)
            return OperationsCommandResult.Rejected("The target or mission is no longer available.");
        Order context = FindOrder(command.OrderId);

        List<PlayerSoldier> characters = ResolveCharacters(command.CharacterIds);
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
                    ResolveTargetFactionId(region, mission),
                    context?.LevelOfAggression ?? command.Aggression,
                    _readiness, _activeSession.CurrentDate, _personnel);
            return Project(characterResult, undo: null);
        }

        List<Squad> squads = ResolveEligibleSquads(sector, region, mission, context, command.SquadIds);
        if (squads.Count == 0)
            return OperationsCommandResult.Rejected("Select at least one eligible squad.");

        bool created = context == null;
        OrderMutationResult result = OrderMutationService.CreateOrAdd(
            sector, region, mission, squads,
            ResolveTargetFactionId(region, mission),
            context?.LevelOfAggression ?? command.Aggression,
            _readiness, _activeSession.CurrentDate, _personnel);
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
        Sector sector = _activeSession.Sector;
        Order order = FindOrder(command.OrderId);
        Squad squad = order?.AssignedSquads.FirstOrDefault(item => item.Id == command.SquadId);
        OrderMutationResult result = OrderMutationService.RemoveSquad(sector, order, squad);
        return Project(result, result.Succeeded
            ? Register("squad removal", () => OrderMutationService.RestoreSquad(
                sector, order, squad, _readiness, _activeSession.CurrentDate, _personnel))
            : null);
    }

    public OrderCancellationPrompt DescribeOrderCancellation(int orderId)
    {
        Order order = FindOrder(orderId);
        return order == null
            ? new OrderCancellationPrompt(false, null, 0, 0)
            : new OrderCancellationPrompt(
                true,
                MissionAvailability.GetOrderLabel(order.Mission),
                order.AssignedSquads.Count,
                order.AssignedCharacters.Count);
    }

    public OperationsCommandResult CancelOrder(CancelOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = _activeSession.Sector;
        Order order = FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");

        // Capture the participants before the release so the undo restores the whole set as one
        // validated command rather than replaying individual assignments.
        OrderRestoreToken token = OrderMutationService.CaptureCancellationUndo(sector, order);
        OrderMutationResult result = OrderMutationService.Cancel(sector, order);
        return Project(result, result.Succeeded
            ? Register("order cancellation", () => OrderMutationService.Restore(
                sector, token, _readiness, _activeSession.CurrentDate, _personnel))
            : null);
    }

    public OperationsCommandResult SetOrderAggression(SetOrderAggressionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = _activeSession.Sector;
        Order order = FindOrder(command.OrderId);
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
        Sector sector = _activeSession.Sector;
        Order order = FindOrder(command.OrderId);
        if (order == null) return OperationsCommandResult.Rejected("That order is no longer active.");
        PlayerSoldier soldier = FindPlayerSoldier(command.SoldierId);
        bool attached = ReferenceEquals(soldier?.CurrentOrder, order);
        OrderMutationResult result = attached
            ? OrderMutationService.DetachSpecialist(sector, order, soldier)
            : OrderMutationService.AttachSpecialist(
                sector, order, soldier, _readiness, _activeSession.CurrentDate, _personnel);
        return Project(result, result.Succeeded
            ? Register(attached ? "specialist detachment" : "specialist attachment", () => attached
                ? OrderMutationService.AttachSpecialist(
                    sector, order, soldier, _readiness, _activeSession.CurrentDate, _personnel)
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
        Sector sector = _activeSession.Sector;
        Planet planet = FindPlanet(command.PlanetId);
        Region region = FindRegion(command.RegionId);
        List<Squad> squads = OrbitingSquads(sector, planet)
            .Where(squad => command.SquadIds?.Contains(squad.Id) == true).ToList();
        return Project(PlanetForceMovementService.Land(
            sector, planet, region,
            new MovementParty(squads, ResolveCharacters(command.CharacterIds)),
            _activeSession.CurrentDate, _personnel));
    }

    public OperationsCommandResult EmbarkForce(EmbarkForceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = _activeSession.Sector;
        Planet planet = FindPlanet(command.PlanetId);
        Region region = FindRegion(command.RegionId);
        List<Squad> squads = (PlayerPresence(sector, region)?.LandedSquads ?? [])
            .Where(squad => command.SquadIds?.Contains(squad.Id) == true).ToList();
        return Project(PlanetForceMovementService.Embark(
            sector, planet, region, FindOrbitingShip(sector, planet, command.ShipId),
            new MovementParty(squads, ResolveCharacters(command.CharacterIds)),
            _activeSession.CurrentDate, _personnel));
    }

    public OperationsCommandResult DetachCasualties(DetachCasualtiesCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsCurrent(command.SessionToken)) return OperationsCommandResult.StaleSession;
        Sector sector = _activeSession.Sector;
        Planet planet = FindPlanet(command.PlanetId);
        MedicalDetachmentResult result = _medicalDetachments.DetachToOrbit(
            sector, planet, FindRegion(command.RegionId),
            FindOrbitingShip(sector, planet, command.ShipId),
            ResolveCharacters(command.SoldierIds), _activeSession.CurrentDate);
        return new OperationsCommandResult(result.Succeeded, result.Message);
    }

    private bool IsCurrent(Guid token) => _activeSession != null && token == SessionToken;

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

    /// <summary>
    /// The faction a mission actually acts against. Diversions name no target directly, so the
    /// region's first disclosed non-Imperial presence stands in — a targeting rule, not display.
    /// </summary>
    private static int ResolveTargetFactionId(Region region, AvailableMission mission)
    {
        int explicitTarget = mission?.TargetFaction?.PlanetFaction?.Faction?.Id
            ?? mission?.SpecialMission?.RegionFaction?.PlanetFaction?.Faction?.Id ?? -1;
        if (explicitTarget >= 0 || mission?.Kind != MissionAvailabilityKind.Diversion)
            return explicitTarget;
        return region.RegionFactionMap.Values
            .Where(presence => presence.IsPublic
                && !FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
            .Select(presence => presence.PlanetFaction.Faction.Id).FirstOrDefault(-1);
    }

    private AvailableMission FindMission(Region region, string key)
    {
        if (region == null || string.IsNullOrWhiteSpace(key)) return null;
        return region.GetSelfAndAdjacentRegions()
            .SelectMany(origin => MissionAvailability.GetAvailableMissions(origin, region))
            .FirstOrDefault(option => option.IdentityKey == key);
    }

    private Planet FindPlanet(int id) =>
        _activeSession?.Sector.Planets.GetValueOrDefault(id);

    private Region FindRegion(int id) =>
        _activeSession?.Sector.Planets.Values
            .SelectMany(planet => planet.Regions)
            .FirstOrDefault(region => region?.Id == id);

    private Order FindOrder(int? id) => id is int value
        ? _activeSession?.Sector.Orders.Values.FirstOrDefault(order => order.Id == value)
        : null;

    private PlayerSoldier FindPlayerSoldier(int id) =>
        _activeSession?.Sector.PlayerForce?.Army?.PlayerSoldierMap?.GetValueOrDefault(id);

    private List<PlayerSoldier> ResolveCharacters(IReadOnlyList<int> ids) =>
        (ids ?? []).Select(FindPlayerSoldier).Where(soldier => soldier != null)
            .DistinctBy(soldier => soldier.Id).ToList();

    private static RegionFaction PlayerPresence(Sector sector, Region region)
    {
        if (region == null || sector?.PlayerForce?.Faction == null) return null;
        region.RegionFactionMap.TryGetValue(
            sector.PlayerForce.Faction.Id, out RegionFaction presence);
        return presence;
    }

    private static IEnumerable<Squad> OrbitingSquads(Sector sector, Planet planet) =>
        planet == null || sector?.PlayerForce?.Faction == null
            ? []
            : PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction)
                .SelectMany(ship => ship.LoadedSquads);

    private static Ship FindOrbitingShip(Sector sector, Planet planet, int shipId) =>
        planet == null || sector?.PlayerForce?.Faction == null
            ? null
            : PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction)
                .FirstOrDefault(ship => ship.Id == shipId);
}
