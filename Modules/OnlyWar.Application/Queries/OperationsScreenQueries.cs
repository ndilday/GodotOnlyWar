using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
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
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Operations.Personnel;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application;

/// <summary>
/// Planetary Operations read service. It resolves the active session, applies the Operations
/// presentation policy, and returns detached screen projections. Mutating commands belong to
/// <see cref="OperationsScreenApplication"/>.
/// </summary>
public sealed class OperationsScreenQueries : CampaignScreenApplication, IOperationsScreenQueries
{
    private OperationsReadContext Read => Context.OperationsRead;
    private IPersonnelAvailabilityQueries Personnel => Read?.Personnel;
    private IReadinessDecisions Readiness => Read?.Readiness;

    public OperationsScreenQueries(CampaignApplicationContext context) : base(context) { }

    public OperationsWorkspaceView QueryOperations(OperationsWorkspaceQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        OperationsReadContext read = Read;
        Planet planet = read?.FindPlanet(query.PlanetId);
        Region region = read?.FindRegion(query.RegionId);
        if (read == null || planet == null || region?.Planet != planet)
        {
            return new OperationsWorkspaceView(
                SessionToken, false, null, null, -1, query.Verb, null, null, null);
        }

        OperationsScreenProjector projector = Projector(read);
        Order contextOrder = read.FindOrder(query.OrderId);

        RegionalOperationsView orders = null;
        MovementOperationsView movement = null;
        DetachOperationsView detach = null;
        switch (query.Verb)
        {
            case PlanetaryOperationsVerb.Order:
                orders = projector.BuildRegional(region, query.MissionKey, query.OrderId,
                    query.Filter, EnumerateSpecialists(read, region, contextOrder));
                break;
            case PlanetaryOperationsVerb.Land:
            case PlanetaryOperationsVerb.Embark:
                movement = BuildMovement(projector, read, planet, region, query);
                break;
            case PlanetaryOperationsVerb.Detach:
                detach = BuildDetach(projector, read, planet, region, query);
                break;
        }

        return new OperationsWorkspaceView(
            SessionToken, true,
            projector.BuildHeader(planet),
            projector.BuildMap(planet, query.Overlay, query.FactionId),
            region.Id, query.Verb, orders, movement, detach);
    }

    public WorldDossierView QueryWorldDossier(int planetId, int regionId) =>
        Read == null
            ? new WorldDossierView([], [], [])
            : Projector().BuildWorld(FindPlanet(planetId), FindRegion(regionId));

    public IReadOnlyList<DossierCardView> QueryRegionCards(int regionId) =>
        Read == null ? [] : Projector().BuildRegionCards(FindRegion(regionId));

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

    private OperationsScreenProjector Projector(OperationsReadContext read = null) =>
        new(read ?? Read);

    private MovementOperationsView BuildMovement(
        OperationsScreenProjector projector, OperationsReadContext read,
        Planet planet, Region region,
        OperationsWorkspaceQuery query)
    {
        bool landing = query.Verb == PlanetaryOperationsVerb.Land;
        List<ForceTreeSquad> roster = landing
            ? read.OrbitingShips(planet)
                .SelectMany(ship => ship.LoadedSquads
                    .Where(squad => squad?.IsPresentOperationalForce == true)
                    .Select(squad => new ForceTreeSquad(squad, ship.Name, ship))).ToList()
            : (read.PlayerPresence(region)?.LandedSquads ?? [])
                .Where(squad => squad?.IsPresentOperationalForce == true)
                .Select(squad => new ForceTreeSquad(squad, region.Name)).ToList();

        Ship destinationShip = landing
            ? null : read.FindOrbitingShip(planet, query.SelectedShipId ?? -1);
        IReadOnlyList<SpecialistOption> characters = EnumerateMovableCharacters(
            read, planet, region, landing, destinationShip);

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
                planet, read.PlayerFaction,
                new MovementParty(
                    roster.Where(item => selectedSquadIds.Contains(item.Squad.Id))
                        .Select(item => item.Squad).ToList(),
                    characters.Where(option => selectedCharacterIds.Contains(option.Soldier.Id))
                        .Select(option => option.Soldier).ToList()),
                Personnel));
        int? selectedShipId = query.SelectedShipId is int shipId
            && ships.Any(choice => choice.ShipId == shipId && choice.Fits) ? shipId : null;

        IReadOnlyList<HierarchyTreeItem> tree = PlanetaryForceTreeBuilder
            .Build(roster, landing ? query.Grouping : ForceTreeGrouping.Company,
                query.Filter, selectedSquadIds, read.TreeInputs)
            .Concat(PlanetaryForceTreeBuilder.BuildCharacterGroup(characters, selectedCharacterIds))
            .ToList();

        return new MovementOperationsView(
            projector.BuildRegionCards(region), tree, ships, selectedShipId,
            selectedSquadIds.Count + selectedCharacterIds.Count,
            validSquadIds, validCharacterIds);
    }

    private DetachOperationsView BuildDetach(
        OperationsScreenProjector projector, OperationsReadContext read,
        Planet planet, Region region,
        OperationsWorkspaceQuery query)
    {
        List<PlayerSoldier> casualties = (read.PlayerPresence(region)?.LandedSquads ?? [])
            .SelectMany(SoldierPresenceService.PresentMembers).OfType<PlayerSoldier>()
            .Where(soldier => soldier.IsWounded && soldier.IndividualPosting == null)
            .DistinctBy(soldier => soldier.Id)
            .OrderBy(soldier => soldier.AssignedSquad?.ParentUnit?.Name)
            .ThenBy(soldier => soldier.Name).ToList();
        HashSet<int> validIds = casualties.Select(soldier => soldier.Id).ToHashSet();
        int selectedCount = (query.CasualtyIds ?? new HashSet<int>()).Count(validIds.Contains);

        IReadOnlyList<ShipChoiceView> ships = read.OrbitingShips(planet)
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

    /// <summary>
    /// Who may be lent to an operation staged out of this region. Availability is Operations
    /// policy; the screen only renders the resulting rows.
    /// </summary>
    internal IReadOnlyList<SpecialistOption> EnumerateSpecialists(
        OperationsReadContext read, Region region, Order contextOrder)
    {
        if (region == null) return [];
        IEnumerable<PlayerSoldier> roster = read.PlayerSoldiers;
        return region.GetSelfAndAdjacentRegions()
            .Select(read.PlayerPresence)
            .Where(presence => presence != null)
            .SelectMany(presence => SpecialistAvailability.EnumerateRoster(
                presence, region, roster, Readiness, contextOrder, Personnel))
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

    internal IReadOnlyList<SpecialistOption> EnumerateMovableCharacters(
        OperationsReadContext read, Planet planet, Region region,
        bool landing, Ship destinationShip)
    {
        CampaignLocation destination = landing
            ? CampaignLocation.Landed(region)
            : destinationShip == null ? null : CampaignLocation.Aboard(destinationShip);
        IReadOnlyList<Ship> orbiting = read.OrbitingShips(planet);
        return read.PlayerSoldiers
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
                    : Personnel.EvaluateMovement(
                        PersonnelAvailabilityProjection.ForMovement(character, destination));
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
        OperationsReadContext read = Read;
        if (read == null || string.IsNullOrWhiteSpace(key))
            return new OperationsSelection([], []);
        Planet planet = read.FindPlanet(query.PlanetId);
        Region region = read.FindRegion(query.RegionId);
        if (planet == null || region == null) return new OperationsSelection([], []);

        bool landing = query.Verb == PlanetaryOperationsVerb.Land;
        IReadOnlyList<SpecialistOption> characters =
            query.Verb == PlanetaryOperationsVerb.Order
                ? EnumerateSpecialists(read, region, read.FindOrder(query.OrderId))
                : EnumerateMovableCharacters(read, planet, region, landing,
                    landing ? null : read.FindOrbitingShip(planet, query.SelectedShipId ?? -1));
        List<int> characterIds = PlanetaryForceTreeBuilder
            .ResolveCharacterSelection(characters, key)
            .Select(character => character.Id).ToList();
        if (characterIds.Count > 0) return new OperationsSelection([], characterIds);

        List<ForceTreeSquad> roster;
        if (query.Verb == PlanetaryOperationsVerb.Order)
        {
            AvailableMission mission = FindMission(region, query.MissionKey);
            roster = OperationsScreenProjector.BuildOrderTreeRoster(
                read.BuildEligibility(region, mission, read.FindOrder(query.OrderId)));
            return new OperationsSelection(
                PlanetaryForceTreeBuilder.ResolveSelection(roster, key)
                    .Where(squad => roster.Any(item =>
                        item.Squad == squad && item.Selectable))
                    .Select(squad => squad.Id).ToList(),
                []);
        }

        roster = landing
            ? read.OrbitingShips(planet)
                .SelectMany(ship => ship.LoadedSquads
                    .Select(squad => new ForceTreeSquad(squad, ship.Name, ship))).ToList()
            : (read.PlayerPresence(region)?.LandedSquads ?? [])
                .Select(squad => new ForceTreeSquad(squad, region.Name)).ToList();
        return new OperationsSelection(
            PlanetaryForceTreeBuilder.ResolveSelection(roster, key)
                .Select(squad => squad.Id).ToList(),
            []);
    }

    public int? FindOrderForMission(int regionId, string missionKey)
    {
        OperationsReadContext read = Read;
        if (read == null) return null;
        Region region = read.FindRegion(regionId);
        AvailableMission mission = FindMission(region, missionKey);
        if (mission == null) return null;
        return read.FindEquivalentOrder(
            region, mission, ResolveTargetFactionId(region, mission))?.Id;
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

    internal AvailableMission FindMission(Region region, string key)
    {
        if (region == null || string.IsNullOrWhiteSpace(key)) return null;
        return region.GetSelfAndAdjacentRegions()
            .SelectMany(origin => MissionAvailability.GetAvailableMissions(origin, region))
            .FirstOrDefault(option => option.IdentityKey == key);
    }

    internal Planet FindPlanet(int id) => Read?.FindPlanet(id);

    internal Region FindRegion(int id) => Read?.FindRegion(id);

    internal Order FindOrder(int? id) => Read?.FindOrder(id);

    internal PlayerSoldier FindPlayerSoldier(int id) => Read?.FindPlayerSoldier(id);

    internal List<PlayerSoldier> ResolveCharacters(IReadOnlyList<int> ids) =>
        (ids ?? []).Select(FindPlayerSoldier).Where(soldier => soldier != null)
            .DistinctBy(soldier => soldier.Id).ToList();

    internal static RegionFaction PlayerPresence(Sector sector, Region region)
    {
        if (region == null || sector?.PlayerForce?.Faction == null) return null;
        region.RegionFactionMap.TryGetValue(
            sector.PlayerForce.Faction.Id, out RegionFaction presence);
        return presence;
    }

    internal static IEnumerable<Squad> OrbitingSquads(Sector sector, Planet planet) =>
        planet == null || sector?.PlayerForce?.Faction == null
            ? []
            : PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction)
                .SelectMany(ship => ship.LoadedSquads);

    internal static Ship FindOrbitingShip(Sector sector, Planet planet, int shipId) =>
        planet == null || sector?.PlayerForce?.Faction == null
            ? null
            : PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, sector.PlayerForce.Faction)
                .FirstOrDefault(ship => ship.Id == shipId);

    internal static int ResolveTargetFactionId(Region region, AvailableMission mission)
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
}

/// <summary>
/// Builds the detached Planetary Operations projections. Everything it needs about the campaign
/// arrives through the Operations read context, so a projection always describes the campaign
/// installed for this application lifetime.
/// </summary>
internal sealed class OperationsScreenProjector
{
    private readonly OperationsReadContext _read;

    internal OperationsScreenProjector(OperationsReadContext read) =>
        _read = read ?? throw new ArgumentNullException(nameof(read));

    private int CurrentWeek => _read.CurrentWeek;
    private Faction PlayerFaction => _read.PlayerFaction;
    private ForceTreeInputs TreeInputs => _read.TreeInputs;

    // ---------------------------------------------------------------- header and map

    internal OperationsHeaderView BuildHeader(Planet planet)
    {
        if (planet == null) return new OperationsHeaderView("", 0, 0, 0, 0, "No request");
        Faction player = PlayerFaction;
        int held = planet.Regions.Count(region => region != null
            && RegionControlPresentation.Build(region).State == RegionControlState.Imperial);
        IRequest request = planet.Governor?.ActiveRequest;
        string clock = request == null ? "No request"
            : request.Status is RequestStatus.Fulfilled or RequestStatus.Failed
                ? request.Status.ToString()
                : $"Request due {FormatDate(request.Deadline)}";
        return new OperationsHeaderView(
            planet.Name, held, planet.Regions.Count(),
            CountLandedPlayerForce(planet, player), CountOrbitingPlayerForce(planet, player), clock);
    }

    private static readonly int[] DiamondRowCounts = [1, 2, 3, 4, 3, 2, 1];

    internal PlanetMapProjection BuildMap(
        Planet planet, PlanetMapOverlay overlay, int? selectedFactionId)
    {
        List<Region> regions = (planet?.Regions ?? [])
            .Where(region => region != null)
            .OrderBy(GetVisualRowKey)
            .ThenBy(region => region.Coordinates.X)
            .ThenBy(region => region.Id)
            .ToList();

        List<List<Region>> coordinateRows = regions
            .GroupBy(GetVisualRowKey)
            .OrderBy(group => group.Key)
            .Select(group => group.OrderBy(region => region.Coordinates.X)
                .ThenBy(region => region.Id).ToList())
            .ToList();
        bool validDiamond = coordinateRows.Count == DiamondRowCounts.Length
            && coordinateRows.Select(row => row.Count).SequenceEqual(DiamondRowCounts);
        if (!validDiamond)
        {
            coordinateRows = [];
            int cursor = 0;
            foreach (int count in DiamondRowCounts)
            {
                coordinateRows.Add(regions.Skip(cursor).Take(count).ToList());
                cursor += count;
            }
        }

        List<IReadOnlyList<MapRegionCard>> rows = coordinateRows
            .Select(row => (IReadOnlyList<MapRegionCard>)row
                .Select(region => BuildCard(region, overlay, selectedFactionId)).ToList())
            .ToList();
        return new PlanetMapProjection(rows, overlay, selectedFactionId, OverlayLegend(overlay));
    }

    // Rotate the encoded hex projection counter-clockwise for the compact rectangular map.
    // The old logical coordinates use 2*Y-X as the horizontal axis; using its inverse as the
    // visual row keeps the existing 1-2-3-4-3-2-1 footprint while putting Alpha at the left.
    internal static int GetVisualRowKey(Region region) =>
        region.Coordinates.X - (2 * region.Coordinates.Y);

    private MapRegionCard BuildCard(
        Region region, PlanetMapOverlay overlay, int? selectedFactionId)
    {
        RegionControlPresentationModel control = RegionControlPresentation.Build(region);
        Faction controllingFactionDefinition =
            region.ControllingFaction?.PlanetFaction?.Faction;
        UiAccent borderAccent = control.State switch
        {
            RegionControlState.Imperial => UiAccent.Gold,
            RegionControlState.Enemy => UiAccent.Opposing,
            _ => UiAccent.Contested
        };
        int? borderFactionArgb = control.State == RegionControlState.Enemy
            ? controllingFactionDefinition?.Color.ToArgb()
            : null;
        RegionFaction factionPresence = selectedFactionId.HasValue
            && region.RegionFactionMap.TryGetValue(
                selectedFactionId.Value, out RegionFaction selected)
                && (selected.IsPublic
                    || selected.PlanetFaction.Faction.IsPlayerFaction
                    || selected.PlanetFaction.Faction.IsDefaultFaction)
                ? selected
                : null;
        string value = overlay switch
        {
            PlanetMapOverlay.Control => ControlOverlayText(
                control.State, controllingFactionDefinition?.Name),
            PlanetMapOverlay.Forces => ForceOverlay(region),
            PlanetMapOverlay.Orders => $"Orders: {CountActivePlayerOrders(region)}",
            PlanetMapOverlay.Intelligence => IntelligenceOverlay(region),
            PlanetMapOverlay.Population => region.HasHiddenDefaultFaction()
                ? "Population: unknown"
                : $"Population: {CompactNumber(region.GetVisibleCivilianPopulation())}",
            PlanetMapOverlay.Pdf => $"PDF: {CompactNumber(region.PlanetaryDefenseForces)}",
            PlanetMapOverlay.Entrenchment => DefenseOverlay(factionPresence, DefenseType.Entrenchment),
            PlanetMapOverlay.ListeningPosts => DefenseOverlay(factionPresence, DefenseType.ListeningPost),
            PlanetMapOverlay.AntiAir => DefenseOverlay(factionPresence, DefenseType.AntiAir),
            _ => string.Empty
        };
        List<Squad> playerSquads = region.RegionFactionMap.Values
            .Where(presence => presence?.PlanetFaction?.Faction?.IsPlayerFaction == true)
            .SelectMany(presence => presence.LandedSquads ?? [])
            .Where(squad => squad?.IsPresentOperationalForce == true)
            .DistinctBy(squad => squad.Id)
            .OrderBy(squad => squad.Id)
            .ToList();
        int playerEffectiveStrength = playerSquads.Sum(squad => Strength(squad).DutyReady);
        int playerFullStrength = playerSquads.Sum(squad => Strength(squad).Full);
        List<(RegionFaction Presence, IntelEstimatePresentation Estimate)> hostileEstimates =
            region.RegionFactionMap.Values
                .Where(presence => presence.IsPublic
                    && !FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
                .OrderBy(presence => presence.PlanetFaction.Faction.Name)
                .ThenBy(presence => presence.PlanetFaction.Faction.Id)
                .Select(presence => (
                    Presence: presence,
                    Estimate: IntelEstimatePresentationBuilder.Build(presence, CurrentWeek)))
                .ToList();
        IntelLevel weakest = hostileEstimates.Count == 0
            ? IntelLevel.None : hostileEstimates.Min(item => item.Estimate.Level);
        string intelligenceDetail = hostileEstimates.Count == 0 ? "No hostile estimate"
            : string.Join("\n", hostileEstimates.Select(item => item.Estimate.Value));
        return new MapRegionCard(
            region.Id,
            region.Name,
            control.State,
            control.State == RegionControlState.Contested ? null : controllingFactionDefinition?.Id,
            control.State == RegionControlState.Contested ? null : controllingFactionDefinition?.Name,
            borderAccent,
            borderFactionArgb,
            playerSquads.Count,
            playerEffectiveStrength,
            playerFullStrength,
            CountActivePlayerOrders(region),
            CountUnassignedPlayerSquads(region),
            CountUnassignedSpecialMissions(region),
            hostileEstimates.Select(item => new RegionEnemyForceEstimate(
                item.Presence.PlanetFaction.Faction.Name, item.Estimate.Value)).ToList(),
            control.Presences,
            value,
            $"{region.Name} · {value}\n{intelligenceDetail}"
                + (FactionActivityPresentation.Build(region) is string factionActivity
                    ? $"\n{factionActivity}" : string.Empty),
            IntelEstimatePresentationBuilder.Marks(weakest),
            RegionTerrainPresentation.GetVariantIndex(region),
            playerSquads.Count > 0,
            FactionActivityPresentation.Build(region),
            FactionActivityPresentation.GetIconKey(region),
            Neighbour(region, Direction.North),
            Neighbour(region, Direction.South),
            Neighbour(region, Direction.West),
            Neighbour(region, Direction.East));
    }

    private enum Direction { North, South, West, East }

    // The map is a rotated hex projection, so "up" means the nearest adjacent region on a smaller
    // visual row, breaking ties by horizontal closeness. Keep this with the row-key rule it depends
    // on rather than in the map control.
    private static int? Neighbour(Region region, Direction direction)
    {
        List<Region> neighbours = region.GetAdjacentRegions();
        int rowKey = GetVisualRowKey(region);
        return direction switch
        {
            Direction.North => neighbours
                .Where(candidate => GetVisualRowKey(candidate) < rowKey)
                .OrderByDescending(GetVisualRowKey)
                .ThenBy(candidate => Math.Abs(candidate.Coordinates.X - region.Coordinates.X))
                .FirstOrDefault()?.Id,
            Direction.South => neighbours
                .Where(candidate => GetVisualRowKey(candidate) > rowKey)
                .OrderBy(GetVisualRowKey)
                .ThenBy(candidate => Math.Abs(candidate.Coordinates.X - region.Coordinates.X))
                .FirstOrDefault()?.Id,
            Direction.West => neighbours
                .Where(candidate => candidate.Coordinates.X < region.Coordinates.X)
                .OrderByDescending(candidate => candidate.Coordinates.X)
                .ThenBy(candidate => Math.Abs(GetVisualRowKey(candidate) - rowKey))
                .FirstOrDefault()?.Id,
            _ => neighbours
                .Where(candidate => candidate.Coordinates.X > region.Coordinates.X)
                .OrderBy(candidate => candidate.Coordinates.X)
                .ThenBy(candidate => Math.Abs(GetVisualRowKey(candidate) - rowKey))
                .FirstOrDefault()?.Id
        };
    }

    private SquadStrengthSnapshot Strength(Squad squad) =>
        SquadStrengthSnapshotBuilder.Build(
            squad, program: TreeInputs.Program, doctrine: TreeInputs.Doctrine);

    // ---------------------------------------------------------------- order workspace

    internal RegionalOperationsView BuildRegional(
        Region target, string missionKey, int? orderId, string filter,
        IReadOnlyList<SpecialistOption> specialists)
    {
        List<AvailableMission> all = AvailableMissions(target);
        AvailableMission selectedMission = missionKey == null
            ? null : all.FirstOrDefault(option => option.IdentityKey == missionKey);
        List<Order> active = ActiveOrders(target);
        Order selectedOrder = orderId is int id
            ? active.FirstOrDefault(order => order.Id == id) : null;
        if (selectedOrder != null && selectedMission == null)
        {
            selectedMission = all.FirstOrDefault(option => option.RepresentsOrder(selectedOrder));
        }

        RegionalEligibilityResult eligibility = _read.BuildEligibility(
            target, selectedMission, selectedOrder);
        List<ForceTreeSquad> roster = BuildOrderTreeRoster(eligibility);
        IReadOnlyList<HierarchyTreeItem> forceTree = PlanetaryForceTreeBuilder
            .Build(roster, ForceTreeGrouping.Company, filter, new HashSet<int>(), TreeInputs)
            .Concat(PlanetaryForceTreeBuilder.BuildCharacterGroup(specialists))
            .ToList();

        return new RegionalOperationsView(
            BuildRegionCards(target),
            active.Select(order => new ActiveOrderView(
                order.Id,
                MissionAvailability.GetOrderLabel(order.Mission),
                order.AssignedSquads.Count,
                order.AssignedCharacters.Count,
                order.LevelOfAggression,
                MissionIconKeys.ForOrder(order.Mission),
                BuildMissionTooltip(order, all))).ToList(),
            all.Where(option => option.Kind != MissionAvailabilityKind.Special)
                .Select(option => BuildMissionOption(option, active)).ToList(),
            all.Where(option => option.Kind == MissionAvailabilityKind.Special)
                .Select(option => BuildMissionOption(option, active)).ToList(),
            forceTree,
            BuildEditor(selectedOrder, specialists),
            selectedOrder?.Id,
            selectedMission?.IdentityKey);
    }

    internal List<AvailableMission> AvailableMissions(Region target) =>
        (target == null ? [] : target.GetSelfAndAdjacentRegions())
            .SelectMany(origin => MissionAvailability.GetAvailableMissions(origin, target))
            .GroupBy(option => option.IdentityKey)
            .Select(group => group.First())
            .OrderBy(option => option.Kind)
            .ThenBy(option => option.Label)
            .ToList();

    internal List<Order> ActiveOrders(Region target) =>
        _read.Orders
            .Where(order => order?.Mission?.RegionFaction?.Region == target
                && HasPlayerParticipant(order))
            .OrderBy(order => MissionAvailability.GetOrderLabel(order.Mission))
            .ThenBy(order => order.Id)
            .ToList() ?? [];

    private MissionOptionView BuildMissionOption(
        AvailableMission mission, IReadOnlyList<Order> active) =>
        new(mission.IdentityKey,
            mission.Label,
            MissionIconKeys.ForAvailable(mission),
            BuildMissionTooltip(mission),
            mission.Kind == MissionAvailabilityKind.Special,
            mission.SpecialMission == null ? null
                : SpecialMissionPresentation.FormatRecommendedForce(
                    mission.SpecialMission, CurrentWeek),
            active.Any(order => mission.RepresentsOrder(order)));

    private static OrderEditorView BuildEditor(
        Order order, IReadOnlyList<SpecialistOption> specialists)
    {
        if (order == null) return null;
        List<SpecialistOption> attached = (specialists ?? [])
            .Where(option => ReferenceEquals(option.Soldier.CurrentOrder, order)).ToList();
        List<SpecialistOption> available = (specialists ?? [])
            .Where(option => option.IsAvailable
                && !ReferenceEquals(option.Soldier.CurrentOrder, order)).ToList();
        return new OrderEditorView(
            order.Id,
            MissionAvailability.GetOrderLabel(order.Mission),
            order.AssignedSquads.Count,
            order.AssignedCharacters.Count,
            order.LevelOfAggression,
            order.AssignedSquads.Select(squad =>
                new OrderParticipantView(squad.Id, squad.Name,
                    $"Release {squad.Name} from this order.")).ToList(),
            attached.Select(option => new OrderParticipantView(
                option.Soldier.Id, option.Soldier.Name,
                $"{option.Label}\nAssigned to this order.")).ToList(),
            available.Select(option => new OrderParticipantView(
                option.Soldier.Id, option.Soldier.Name, option.Label)).ToList(),
            (specialists?.Count ?? 0) > 0);
    }

    internal static List<ForceTreeSquad> BuildOrderTreeRoster(
        RegionalEligibilityResult eligibility)
    {
        IEnumerable<RegionalSquadCandidate> candidates = eligibility == null
            ? Enumerable.Empty<RegionalSquadCandidate>()
            : eligibility.Groups.SelectMany(group => group.Candidates)
                .Concat(eligibility.Excluded);

        return candidates
            .Where(candidate => SpecialistAvailability.IsMissionSquadFormation(candidate.Squad))
            .DistinctBy(candidate => candidate.Squad.Id)
            .Select(candidate => new ForceTreeSquad(candidate.Squad, candidate.Origin.Name, null,
                candidate.Exclusion, candidate.IsAssignedToContext)).ToList();
    }

    private string BuildMissionTooltip(AvailableMission mission)
    {
        if (mission?.SpecialMission == null) return mission?.Label ?? "";
        return $"{mission.Label}\n{SpecialMissionPresentation.FormatRecommendedForce(
            mission.SpecialMission, CurrentWeek)}";
    }

    private string BuildMissionTooltip(Order order, IReadOnlyList<AvailableMission> options)
    {
        AvailableMission mission = options?.FirstOrDefault(
            option => option.RepresentsOrder(order));
        if (mission != null) return BuildMissionTooltip(mission);

        // Keep the active row useful if an already-created order is no longer represented by
        // the current availability snapshot (for example, after intelligence changes).
        Mission orderMission = order?.Mission;
        if (orderMission == null) return "";
        Region region = orderMission.RegionFaction?.Region;
        bool isSpecial = region?.SpecialMissions?.Any(
            candidate => candidate?.Id == orderMission.Id) == true;
        if (!isSpecial) return MissionAvailability.GetOrderLabel(orderMission);

        return BuildMissionTooltip(new AvailableMission(
            SpecialMissionPresentation.Format(orderMission, region),
            MissionAvailabilityKind.Special,
            orderMission));
    }

    // ---------------------------------------------------------------- dossier cards

    internal WorldDossierView BuildWorld(Planet planet, Region selectedRegion)
    {
        if (planet == null) return new WorldDossierView([], [], []);

        Faction player = PlayerFaction;
        Region capital = planet.Regions.FirstOrDefault(region =>
            region?.Id == planet.CapitalRegionId) ?? planet.Regions.FirstOrDefault();
        long imperialPopulation = PublicPresences(planet, imperial: true).Sum(p => p.Population);
        long disclosedHostilePopulation =
            PublicPresences(planet, imperial: false).Sum(p => p.Population);
        int contested = planet.Regions.Count(region => region != null
            && RegionControlPresentation.Build(region).State == RegionControlState.Contested);

        List<DossierCardView> profile =
        [
            new DossierCardView("World Profile", planet.Name,
            [
                new("Classification", planet.Template?.Name ?? "Unclassified"),
                new("Allegiance", planet.GetControllingFaction()?.Name ?? "Contested"),
                new("Capital", capital?.Name ?? "Unknown"),
                new("Population", planet.Population.ToString("N0")),
                new("Imperial", imperialPopulation.ToString("N0")),
                new("Contested Regions", contested.ToString()),
                new("Hostile / Unaccounted", Math.Max(
                    disclosedHostilePopulation,
                    planet.Population - imperialPopulation).ToString("N0")),
                new("Tithe Grade", planet.TaxLevel.ToString()),
                new("Governor", planet.Governor?.Name ?? "None"),
                new("Civil Stability", $"{planet.Stability:0.#}%")
            ], UiAccent.Gold)
        ];

        if (planet.Governor?.ActiveRequest is IRequest request)
        {
            profile.Add(new DossierCardView("Governor's Request",
                request.Requester?.Name ?? planet.Governor.Name,
                [
                    new("Target", request.ThreatFaction?.Name ?? capital?.Name ?? "Planetary"),
                    new("Deadline", FormatDate(request.Deadline)),
                    new("Progress", request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed
                        ? "Suppress threat"
                        : $"{request.ProgressBattleValueTime:0.#} BV-weeks"),
                    new("Reward", request.OfferedScheduleKind == PledgeScheduleKind.Standing
                        ? $"{request.OfferedRequisition:N0} Req / {request.OfferedCadenceWeeks} wks"
                        : $"{request.OfferedRequisition:N0} Req")
                ], UiAccent.Gold));
        }

        if (_read.RecruitmentProgram is RecruitmentProgram recruitment
            && recruitment.HomeWorldPlanetId == planet.Id)
        {
            profile.Add(new DossierCardView("Recruitment & Tithe",
                recruitment.IsSetupComplete ? "Active Chapter World" : "Establishing Program",
                [
                    new("Policy", recruitment.Policy.ToString()),
                    new("Unscreened", recruitment.UnscreenedEligiblePopulation.ToString("N0")),
                    new("Qualified Candidates", recruitment.QualifiedCandidates.Count.ToString("N0")),
                    new("Aspirants", recruitment.Aspirants.Count.ToString("N0")),
                    new("Gene-seed Reserve", _read.GeneseedStockpile.ToString("N0")),
                    new("Gene-seed Purity", _read.GeneseedStockpile > 0
                        ? _read.GeneseedPurity.ToString("P0") : "--"),
                    new("Tithe Grade", planet.TaxLevel.ToString())
                ], UiAccent.Player));
        }

        int controlled = planet.Regions.Count(region => region != null
            && RegionControlPresentation.Build(region).State == RegionControlState.Imperial);
        List<DossierCardView> strength =
        [
            new DossierCardView("Imperial Command", "Combined Theater Strength",
            [
                new("Imperial Population", imperialPopulation.ToString("N0")),
                new("PDF Strength", planet.PlanetaryDefenseForces.ToString("N0")),
                new("Controlled Regions", controlled.ToString()),
                new("Astartes Landed", CountLandedPlayerForce(planet, player).ToString("N0")),
                new("Astartes In Orbit", CountOrbitingPlayerForce(planet, player).ToString("N0"))
            ], UiAccent.Player)
        ];

        foreach (IGrouping<Faction, RegionFaction> hostile in PublicPresences(planet, imperial: false)
            .GroupBy(presence => presence.PlanetFaction.Faction)
            .OrderBy(group => group.Key.Name))
        {
            IntelEstimatePresentation estimate =
                IntelEstimatePresentationBuilder.BuildWorld(hostile, CurrentWeek);
            strength.Add(new DossierCardView("Hostile Faction", hostile.Key.Name,
                [
                    new("Force Estimate", estimate.Value),
                    new("Last Report", estimate.LastReport),
                    new("Detected Regions", hostile.Select(presence => presence.Region)
                        .Distinct().Count().ToString())
                ], UiAccent.Opposing, hostile.Key.Color.ToArgb()));
        }

        return new WorldDossierView(profile, strength, BuildRegionCards(selectedRegion));
    }

    private static IEnumerable<RegionFaction> PublicPresences(Planet planet, bool imperial) =>
        planet.Regions.Where(region => region != null)
            .SelectMany(region => region.RegionFactionMap.Values)
            .Where(presence => presence.IsPublic
                && FactionRelationshipService.IsImperial(
                    presence.PlanetFaction.Faction) == imperial);

    internal IReadOnlyList<DossierCardView> BuildRegionCards(Region region)
    {
        if (region == null) return [];
        RegionControlPresentationModel control = RegionControlPresentation.Build(region);
        List<LabeledValue> regionRows =
        [
            new("Control", control.State.ToString()),
            new("Intelligence", RegionFactionDescriptionExtensions.GetIntelligenceLevelDescription(
                region.GetPlayerVisibleIntel())),
            new("Population", region.HasHiddenDefaultFaction()
                ? "Unknown"
                : region.GetVisibleCivilianPopulation().ToString("N0")),
            new("PDF Strength", region.PlanetaryDefenseForces.ToString("N0")),
            new("Detected Factions", control.Presences.Count == 0
                ? "None"
                : string.Join(", ", control.Presences.Select(item => item.FactionName)))
        ];
        if (FactionActivityPresentation.Build(region) is string factionActivity)
        {
            regionRows.Add(new("Faction Activity", factionActivity));
        }
        regionRows.Add(new("Active Orders", CountActivePlayerOrders(region).ToString()));
        List<DossierCardView> cards =
        [
            new DossierCardView("Selected Region", region.Name, regionRows, UiAccent.Gold)
        ];

        List<RegionFaction> visiblePresences = region.RegionFactionMap.Values
            .Where(presence => presence.IsPublic
                || presence.PlanetFaction.Faction.IsPlayerFaction
                || presence.PlanetFaction.Faction.IsDefaultFaction)
            .ToList();

        List<RegionFaction> imperialPresences = visiblePresences
            .Where(presence => FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
            .OrderBy(presence => presence.PlanetFaction.Faction.IsDefaultFaction ? 0 : 1)
            .ThenBy(presence => presence.PlanetFaction.Faction.Name)
            .ThenBy(presence => presence.PlanetFaction.Faction.Id)
            .ToList();

        // The Chapter has its own RegionFaction so landed squads remain attributable to the
        // player, but it shares the ground with the world's Imperial defense presence. Render
        // that allied position as one card and add all allied landed headcount to its Forces.
        if (imperialPresences.Count > 0)
        {
            RegionFaction representative = imperialPresences[0];
            List<Squad> imperialSquads = imperialPresences
                .SelectMany(presence => presence.LandedSquads)
                .DistinctBy(squad => squad.Id)
                .ToList();
            int imperialForces = imperialSquads.Sum(SoldierPresenceService.PresentCount)
                + CountLandedPlayerCharacters(region, imperialSquads);
            cards.Add(new DossierCardView("Imperial Defenses",
                representative.PlanetFaction.Faction.Name,
                [
                    new("Forces", imperialForces.ToString("N0")),
                    new("Entrenchment", DescribeDefense(representative, DefenseType.Entrenchment, true)),
                    new("Listening Post", DescribeDefense(representative, DefenseType.ListeningPost, true)),
                    new("Anti-Air", DescribeDefense(representative, DefenseType.AntiAir, true))
                ], UiAccent.Player));
        }

        foreach (RegionFaction presence in visiblePresences
            .Where(item => !FactionRelationshipService.IsImperial(item.PlanetFaction.Faction))
            .OrderBy(item => item.PlanetFaction.Faction.Name)
            .ThenBy(item => item.PlanetFaction.Faction.Id))
        {
            IntelEstimatePresentation estimate =
                IntelEstimatePresentationBuilder.Build(presence, CurrentWeek);
            cards.Add(new DossierCardView("Hostile Force",
                presence.PlanetFaction.Faction.Name,
                [
                    new("Forces", estimate.Value),
                    new("Entrenchment", DescribeDefense(presence, DefenseType.Entrenchment, false)),
                    new("Listening Post", DescribeDefense(presence, DefenseType.ListeningPost, false)),
                    new("Anti-Air", DescribeDefense(presence, DefenseType.AntiAir, false))
                ], UiAccent.Opposing));
        }
        return cards;
    }

    // ---------------------------------------------------------------- shared counts

    private static string DescribeDefense(RegionFaction presence, DefenseType type, bool exact)
    {
        double value = RegionDefenses.GetShared(presence, type);
        string description = RegionFactionDescriptionExtensions.GetDefenseLevelDescription(value);
        return exact ? $"{description} ({value:0.##})" : description;
    }

    private int CountLandedPlayerForce(Planet planet, Faction player)
    {
        if (planet == null || player == null) return 0;
        List<Squad> landedSquads = planet.Regions
            .Where(region => region != null)
            .SelectMany(region => region.RegionFactionMap.Values)
            .Where(presence => presence?.PlanetFaction?.Faction == player)
            .SelectMany(presence => presence.LandedSquads ?? [])
            .Where(squad => squad != null)
            .DistinctBy(squad => squad.Id)
            .ToList();
        return landedSquads.Sum(SoldierPresenceService.PresentCount)
            + GetUnrepresentedPlayerCharacters(landedSquads).Count(character =>
                CampaignLocationService.ForSoldier(character)?.Region?.Planet == planet);
    }

    private int CountLandedPlayerCharacters(Region region, IEnumerable<Squad> accountedSquads) =>
        region == null ? 0
            : GetUnrepresentedPlayerCharacters(accountedSquads).Count(character =>
                CampaignLocationService.ForSoldier(character)?.Region == region);

    private IEnumerable<PlayerSoldier> GetUnrepresentedPlayerCharacters(
        IEnumerable<Squad> accountedSquads)
    {
        Faction player = PlayerFaction;
        if (player == null) return Enumerable.Empty<PlayerSoldier>();

        HashSet<int> accountedCharacterIds = (accountedSquads ?? Enumerable.Empty<Squad>())
            .Where(squad => squad != null)
            .SelectMany(squad => squad.Members.OfType<PlayerSoldier>())
            .Where(character => character.IndividualPosting == null)
            .Select(character => character.Id)
            .ToHashSet();
        return _read.PlayerSoldiers
            .Where(character => character != null
                && character.AssignedSquad?.Faction == player
                && !accountedCharacterIds.Contains(character.Id));
    }

    private int CountOrbitingPlayerForce(Planet planet, Faction player)
    {
        if (planet == null || player == null) return 0;
        IReadOnlyList<Ship> orbitingShips = _read.OrbitingShips(planet);
        List<Squad> orbitingSquads = orbitingShips
            .SelectMany(ship => ship.LoadedSquads.Concat(ship.AdministrativeStations))
            .Where(squad => squad != null)
            .DistinctBy(squad => squad.Id)
            .ToList();
        HashSet<Ship> shipSet = orbitingShips.ToHashSet();
        return orbitingSquads.Sum(SoldierPresenceService.PresentCount)
            + GetUnrepresentedPlayerCharacters(orbitingSquads).Count(character =>
                shipSet.Contains(CampaignLocationService.ForSoldier(character)?.Ship));
    }

    private int CountActivePlayerOrders(Region region) =>
        _read.Orders.Count(order =>
            order?.Mission?.RegionFaction?.Region == region && HasPlayerParticipant(order));

    private static bool HasPlayerParticipant(Order order) =>
        order?.OwnerFaction?.IsPlayerFaction == true
        || order?.AssignedSquads.Any(squad => squad?.Faction?.IsPlayerFaction == true) == true
        || order?.AssignedCharacters.Any(character =>
            character?.AssignedSquad?.Faction?.IsPlayerFaction == true) == true;

    private static int CountUnassignedPlayerSquads(Region region) =>
        region?.RegionFactionMap.Values
            .Where(presence => presence?.PlanetFaction?.Faction?.IsPlayerFaction == true)
            .SelectMany(presence => presence.LandedSquads ?? [])
            .Where(squad => squad != null && squad.CurrentOrders == null)
            .DistinctBy(squad => squad.Id)
            .Count() ?? 0;

    private int CountUnassignedSpecialMissions(Region region)
    {
        if (region == null) return 0;
        HashSet<int> assignedMissionIds = _read.Orders
            .Where(order => order?.Mission != null
                && order.AssignedSquads?.Any(squad =>
                    squad?.Faction?.IsPlayerFaction == true) == true)
            .Select(order => order.Mission.Id)
            .ToHashSet();
        return region.SpecialMissions
            .Where(MissionAvailability.IsPlayerVisibleSpecialMission)
            .Count(mission => !assignedMissionIds.Contains(mission.Id));
    }

    private int ForcePresent(Region region) => region.RegionFactionMap.Values
        .Where(presence => presence.PlanetFaction.Faction.IsPlayerFaction)
        .SelectMany(presence => presence.LandedSquads)
        .Sum(SoldierPresenceService.PresentCount);

    private string ForceOverlay(Region region)
    {
        List<string> hostile = region.RegionFactionMap.Values
            .Where(presence => presence.IsPublic
                && !FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
            .Select(presence => IntelEstimatePresentationBuilder.Build(presence, CurrentWeek).Value)
            .Distinct()
            .ToList();
        string playerText = $"Astartes: {ForcePresent(region)}";
        return hostile.Count == 0
            ? playerText
            : $"{playerText} · Hostile: {string.Join('/', hostile)}";
    }

    private string IntelligenceOverlay(Region region)
    {
        List<IntelEstimatePresentation> estimates = region.RegionFactionMap.Values
            .Where(presence => presence.IsPublic
                && !FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
            .Select(presence => IntelEstimatePresentationBuilder.Build(presence, CurrentWeek))
            .ToList();
        IntelLevel weakest = estimates.Count == 0
            ? IntelLevel.None : estimates.Min(item => item.Level);
        return $"Intel: {RegionFactionDescriptionExtensions.GetIntelligenceLevelDescription(
            region.GetPlayerVisibleIntel())} · {IntelEstimatePresentationBuilder.Marks(weakest)}";
    }

    private static string DefenseOverlay(RegionFaction presence, DefenseType type)
    {
        if (presence == null) return "NO DISCLOSED PRESENCE";
        double value = RegionDefenses.GetShared(presence, type);
        return presence.PlanetFaction.Faction.IsPlayerFaction
            || presence.PlanetFaction.Faction.IsDefaultFaction
            ? $"{value:0.##}"
            : RegionFactionDescriptionExtensions.GetDefenseLevelDescription(value);
    }

    private static string ControlOverlayText(
        RegionControlState state, string controllingFactionName) => state switch
        {
            RegionControlState.Imperial => "IMPERIAL",
            RegionControlState.Enemy => string.IsNullOrWhiteSpace(controllingFactionName)
                ? "UNIDENTIFIED ENEMY"
                : controllingFactionName.ToUpperInvariant(),
            _ => "CONTESTED"
        };

    private static string OverlayLegend(PlanetMapOverlay overlay) => overlay switch
    {
        PlanetMapOverlay.Control => "CONTROL · Imperial / Enemy / Contested",
        PlanetMapOverlay.Forces => "FORCES · disclosed strength only",
        PlanetMapOverlay.Orders => "ORDERS · active Chapter operations",
        PlanetMapOverlay.Intelligence => "INTELLIGENCE · current player-visible rating",
        PlanetMapOverlay.Population => "POPULATION · disclosed civilian population",
        PlanetMapOverlay.Pdf => "PDF · planetary defense force",
        _ => "DEFENSE · selected faction/alliance regional works"
    };

    private static string CompactNumber(long value)
    {
        if (value >= 1_000_000_000) return $"{value / 1_000_000_000d:0.#}B";
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.#}M";
        if (value >= 1_000) return $"{value / 1_000d:0.#}K";
        return value.ToString("N0");
    }

    private static string FormatDate(Date date) =>
        date == null ? "Unknown" : $"{date.Year:000}.M{date.Millenium} · week {date.Week}";
}

/// <summary>
/// Which icon represents a mission. Key selection is a projection decision; the atlas lookup is
/// the host's.
/// </summary>
public static class MissionIconKeys
{
    public static string ForAvailable(AvailableMission mission)
    {
        if (mission?.Kind == MissionAvailabilityKind.Special) return ForOrder(mission.SpecialMission);
        return mission?.Kind switch
        {
            MissionAvailabilityKind.Recon => "mission_recon",
            MissionAvailabilityKind.Defend => "mission_defend",
            MissionAvailabilityKind.Patrol => "mission_patrol",
            MissionAvailabilityKind.Attack => "mission_attack",
            MissionAvailabilityKind.Diversion => "mission_diversion",
            MissionAvailabilityKind.FortifyEntrenchment => "fortification_entrenchment",
            MissionAvailabilityKind.BuildListeningPost => "fortification_listening_post",
            MissionAvailabilityKind.BuildAntiAir => "fortification_anti_air",
            MissionAvailabilityKind.Move => "route",
            _ => "objective"
        };
    }

    public static string ForOrder(Mission mission)
    {
        if (mission is ConstructionMission construction)
        {
            return construction.ConstructionType switch
            {
                DefenseType.Entrenchment => "fortification_entrenchment",
                DefenseType.ListeningPost => "fortification_listening_post",
                DefenseType.AntiAir => "fortification_anti_air",
                _ => "objective"
            };
        }

        return mission?.MissionType switch
        {
            MissionType.Recon => "mission_recon",
            MissionType.DefenseInDepth => "mission_defend",
            MissionType.Patrol => "mission_patrol",
            MissionType.Advance =>
                mission.TargetFaction?.IsPlayerFaction == true ? "route" : "mission_attack",
            MissionType.Diversion => "mission_diversion",
            MissionType.Ambush or MissionType.Extermination => "mission_ambush",
            MissionType.Sabotage => "mission_sabotage",
            MissionType.ShowOfForce => "mission_show_of_force",
            _ => "objective"
        };
    }
}
