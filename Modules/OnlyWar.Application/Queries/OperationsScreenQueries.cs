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
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application;

/// <summary>
/// Builds the detached Planetary Operations projections. Everything it needs about the campaign
/// arrives as explicit arguments, so a projection always describes the campaign the caller named.
/// </summary>
internal sealed class OperationsScreenProjector
{
    private readonly Sector _sector;
    private readonly Date _date;
    private readonly IReadinessDecisions _readiness;

    internal OperationsScreenProjector(Sector sector, Date date, IReadinessDecisions readiness)
    {
        _sector = sector;
        _date = date;
        _readiness = readiness;
    }

    private int CurrentWeek => _date?.GetTotalWeeks() ?? 0;
    private RecruitmentProgram Program => _sector?.PlayerForce?.RecruitmentProgram;
    private ChapterOperationalDoctrine Doctrine =>
        _sector?.PlayerForce?.Army?.ChapterOperationalDoctrine;
    private ForceTreeInputs TreeInputs => new(Program, Doctrine);

    // ---------------------------------------------------------------- header and map

    internal OperationsHeaderView BuildHeader(Planet planet)
    {
        if (planet == null) return new OperationsHeaderView("", 0, 0, 0, 0, "No request");
        Faction player = _sector?.PlayerForce?.Faction;
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
        SquadStrengthSnapshotBuilder.Build(squad, program: Program, doctrine: Doctrine);

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

        RegionalEligibilityResult eligibility = RegionalOrderEligibilityService.Build(
            _sector, target, _readiness, selectedMission, selectedOrder);
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
        _sector?.Orders.Values
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

        Faction player = _sector?.PlayerForce?.Faction;
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

        if (_sector?.PlayerForce?.RecruitmentProgram is RecruitmentProgram recruitment
            && recruitment.HomeWorldPlanetId == planet.Id)
        {
            profile.Add(new DossierCardView("Recruitment & Tithe",
                recruitment.IsSetupComplete ? "Active Chapter World" : "Establishing Program",
                [
                    new("Policy", recruitment.Policy.ToString()),
                    new("Unscreened", recruitment.UnscreenedEligiblePopulation.ToString("N0")),
                    new("Qualified Candidates", recruitment.QualifiedCandidates.Count.ToString("N0")),
                    new("Aspirants", recruitment.Aspirants.Count.ToString("N0")),
                    new("Gene-seed Reserve", _sector.PlayerForce.GeneseedStockpile.ToString("N0")),
                    new("Gene-seed Purity", _sector.PlayerForce.GeneseedStockpile > 0
                        ? _sector.PlayerForce.GeneseedPurity.ToString("P0") : "--"),
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
        if (_sector != null)
        {
            regionRows.Add(new("Active Orders", CountActivePlayerOrders(region).ToString()));
        }
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
        Faction player = _sector?.PlayerForce?.Faction;
        if (player == null) return Enumerable.Empty<PlayerSoldier>();

        HashSet<int> accountedCharacterIds = (accountedSquads ?? Enumerable.Empty<Squad>())
            .Where(squad => squad != null)
            .SelectMany(squad => squad.Members.OfType<PlayerSoldier>())
            .Where(character => character.IndividualPosting == null)
            .Select(character => character.Id)
            .ToHashSet();
        return (_sector.PlayerForce.Army?.PlayerSoldierMap?.Values
                ?? Enumerable.Empty<PlayerSoldier>())
            .Where(character => character != null
                && character.AssignedSquad?.Faction == player
                && !accountedCharacterIds.Contains(character.Id));
    }

    private int CountOrbitingPlayerForce(Planet planet, Faction player)
    {
        if (planet == null || player == null) return 0;
        IReadOnlyList<Ship> orbitingShips =
            PlanetForceMovementService.GetOrbitingPlayerShips(planet, player);
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
        _sector?.Orders.Values.Count(order =>
            order?.Mission?.RegionFaction?.Region == region && HasPlayerParticipant(order)) ?? 0;

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
        HashSet<int> assignedMissionIds = (_sector?.Orders?.Values ?? [])
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
