using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Missions;
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

namespace OnlyWar.Helpers.PlanetaryOperations
{
    public enum PlanetaryOperationsVerb
    {
        Order,
        Land,
        Embark,
        Detach
    }

    public enum PlanetMapOverlay
    {
        Control,
        Forces,
        Orders,
        Intelligence,
        Population,
        Pdf,
        Entrenchment,
        ListeningPosts,
        AntiAir
    }

    public sealed record RegionMapCardViewModel(
        Region Region,
        string Name,
        RegionControlState Control,
        int? ControlFactionId,
        string ControlFactionName,
        Color ControlBorderColor,
        int PlayerSquads,
        int PlayerEffectiveStrength,
        int PlayerFullStrength,
        int ActiveOrders,
        int UnassignedSquads,
        int MissionOpportunities,
        IReadOnlyList<RegionEnemyForceEstimate> PublicEnemyForces,
        IReadOnlyList<RegionPresencePresentation> Presences,
        string OverlayText,
        string OverlayTooltip,
        string IntelConfidence,
        int TerrainVariant,
        bool HasPlayerForces,
        string FactionActivity = null,
        string FactionActivityIconKey = null)
    {
    }

    public sealed record RegionEnemyForceEstimate(
        string FactionName,
        string ForceEstimate);

    public sealed record PlanetRegionMapViewModel(
        IReadOnlyList<IReadOnlyList<RegionMapCardViewModel>> Rows,
        PlanetMapOverlay Overlay,
        int? SelectedFactionId,
        string OverlayLegend);

    public sealed record WorldDossierViewModel(
        IReadOnlyList<DossierCardData> ProfileCards,
        IReadOnlyList<DossierCardData> StrengthCards,
        IReadOnlyList<DossierCardData> SelectedRegionCards);

    public sealed record RegionalOperationsViewModel(
        RegionalEligibilityResult Eligibility,
        IReadOnlyList<DossierCardData> SelectedRegionCards,
        IReadOnlyList<Order> ActiveOrders,
        IReadOnlyList<AvailableMission> OrdinaryMissions,
        IReadOnlyList<AvailableMission> SpecialMissions);

    public sealed record PlanetaryOperationsNavigationState(
        int PlanetId,
        int RegionId,
        PlanetMapOverlay Overlay,
        int? FactionId,
        int? OrderId = null,
        string MissionKey = null,
        PlanetaryOperationsVerb Verb = PlanetaryOperationsVerb.Order);

    public sealed record PlanetaryOperationsHeaderViewModel(
        string PlanetName,
        int ImperialRegions,
        int TotalRegions,
        int Landed,
        int InOrbit,
        string RequestClock);

    public static class PlanetRegionMapViewModelBuilder
    {
        private static readonly int[] DiamondRowCounts = [1, 2, 3, 4, 3, 2, 1];

        public static PlanetRegionMapViewModel Build(
            Sector sector,
            Planet planet,
            PlanetMapOverlay overlay,
            int? selectedFactionId)
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
                .Select(group => group.OrderBy(region => region.Coordinates.X).ThenBy(region => region.Id).ToList())
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

            List<IReadOnlyList<RegionMapCardViewModel>> rows = coordinateRows
                .Select(row => (IReadOnlyList<RegionMapCardViewModel>)row
                    .Select(region => BuildCard(
                        sector, region, overlay, selectedFactionId))
                    .ToList())
                .ToList();
            return new PlanetRegionMapViewModel(
                rows,
                overlay,
                selectedFactionId,
                OverlayLegend(overlay));
        }

        // Rotate the encoded hex projection counter-clockwise for the compact rectangular map.
        // The old logical coordinates use 2*Y-X as the horizontal axis; using its inverse as the
        // visual row keeps the existing 1-2-3-4-3-2-1 footprint while putting Alpha at the left.
        internal static int GetVisualRowKey(Region region) =>
            region.Coordinates.X - (2 * region.Coordinates.Y);

        private static RegionMapCardViewModel BuildCard(
            Sector sector,
            Region region,
            PlanetMapOverlay overlay,
            int? selectedFactionId)
        {
            RegionControlPresentationModel control = RegionControlPresentation.Build(region);
            RegionFaction controllingFaction = region.ControllingFaction;
            Faction controllingFactionDefinition = controllingFaction?.PlanetFaction?.Faction;
            Color controlBorderColor = control.State switch
            {
                RegionControlState.Imperial => OnlyWarStyle.Gold,
                RegionControlState.Enemy => controllingFactionDefinition?.Color.ToGodotColor()
                    ?? OnlyWarStyle.OpposingAccent,
                _ => OnlyWarStyle.MapContested
            };
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
                PlanetMapOverlay.Forces => ForceOverlay(region, CurrentWeek()),
                PlanetMapOverlay.Orders => OrderOverlay(sector, region),
                PlanetMapOverlay.Intelligence => IntelligenceOverlay(region, CurrentWeek()),
                PlanetMapOverlay.Population => region.HasHiddenDefaultFaction()
                    ? "Population: unknown"
                    : $"Population: {CompactNumber(region.GetVisibleCivilianPopulation())}",
                PlanetMapOverlay.Pdf => $"PDF: {CompactNumber(region.PlanetaryDefenseForces)}",
                PlanetMapOverlay.Entrenchment => DefenseOverlay(
                    factionPresence, DefenseType.Entrenchment),
                PlanetMapOverlay.ListeningPosts => DefenseOverlay(
                    factionPresence, DefenseType.ListeningPost),
                PlanetMapOverlay.AntiAir => DefenseOverlay(
                    factionPresence, DefenseType.AntiAir),
                _ => string.Empty
            };
            List<Squad> playerSquads = region.RegionFactionMap.Values
                .Where(presence => presence?.PlanetFaction?.Faction?.IsPlayerFaction == true)
                .SelectMany(presence => presence.LandedSquads ?? [])
                .Where(squad => squad?.IsPresentOperationalForce == true)
                .DistinctBy(squad => squad.Id)
                .OrderBy(squad => squad.Id)
                .ToList();
            bool playerForces = playerSquads.Count > 0;
            RecruitmentProgram recruitmentProgram = sector?.PlayerForce?.RecruitmentProgram;
            int playerEffectiveStrength = playerSquads.Sum(squad =>
                SquadStrengthSnapshotBuilder.Build(squad, recruitmentProgram).Effective);
            int playerFullStrength = playerSquads.Sum(squad =>
                SquadStrengthSnapshotBuilder.Build(squad, recruitmentProgram).Full);
            int activeOrders = CountActivePlayerOrders(sector, region);
            List<(RegionFaction Presence, IntelEstimatePresentation Estimate)> hostileEstimates = region.RegionFactionMap.Values
                .Where(presence => presence.IsPublic
                    && !FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
                .OrderBy(presence => presence.PlanetFaction.Faction.Name)
                .ThenBy(presence => presence.PlanetFaction.Faction.Id)
                .Select(presence => (
                    Presence: presence,
                    Estimate: IntelEstimatePresentationBuilder.Build(presence, CurrentWeek())))
                .ToList();
            IntelLevel weakest = hostileEstimates.Count == 0
                ? IntelLevel.None : hostileEstimates.Min(item => item.Estimate.Level);
            string intelligenceDetail = hostileEstimates.Count == 0 ? "No hostile estimate"
                : string.Join("\n", hostileEstimates.Select(item => item.Estimate.Value));
            List<RegionEnemyForceEstimate> publicEnemyForces = hostileEstimates
                .Select(item => new RegionEnemyForceEstimate(
                    item.Presence.PlanetFaction.Faction.Name,
                    item.Estimate.Value))
                .ToList();
            return new RegionMapCardViewModel(
                region,
                region.Name,
                control.State,
                control.State == RegionControlState.Contested ? null : controllingFactionDefinition?.Id,
                control.State == RegionControlState.Contested ? null : controllingFactionDefinition?.Name,
                controlBorderColor,
                playerSquads.Count,
                playerEffectiveStrength,
                playerFullStrength,
                activeOrders,
                CountUnassignedPlayerSquads(region),
                CountUnassignedSpecialMissions(sector, region),
                publicEnemyForces,
                control.Presences,
                value,
                $"{region.Name} · {value}\n{intelligenceDetail}"
                    + (FactionActivityPresentation.Build(region) is string factionActivity
                        ? $"\n{factionActivity}" : string.Empty),
                IntelEstimatePresentationBuilder.Marks(weakest),
                RegionTerrainPresentation.GetVariantIndex(region),
                playerForces,
                FactionActivityPresentation.Build(region),
                FactionActivityPresentation.GetIconKey(region));
        }

        private static int CountUnassignedPlayerSquads(Region region) =>
            region?.RegionFactionMap.Values
                .Where(presence => presence?.PlanetFaction?.Faction?.IsPlayerFaction == true)
                .SelectMany(presence => presence.LandedSquads ?? [])
                .Where(squad => squad != null && squad.CurrentOrders == null)
                .DistinctBy(squad => squad.Id)
                .Count() ?? 0;

        private static int CountUnassignedSpecialMissions(Sector sector, Region region)
        {
            if (region == null) return 0;

            HashSet<int> assignedMissionIds = (sector?.Orders?.Values ?? [])
                .Where(order => order?.Mission != null
                    && order.AssignedSquads?.Any(squad =>
                        squad?.Faction?.IsPlayerFaction == true) == true)
                .Select(order => order.Mission.Id)
                .ToHashSet();
            return region.SpecialMissions
                .Where(IsPlayerVisibleSpecialMission)
                .Count(mission => !assignedMissionIds.Contains(mission.Id));
        }

        private static bool IsPlayerVisibleSpecialMission(Mission mission) =>
            MissionAvailability.IsPlayerVisibleSpecialMission(mission);

        private static string ForceOverlay(Region region, int currentWeek)
        {
            int player = region.RegionFactionMap.Values
                .Where(presence => presence.PlanetFaction.Faction.IsPlayerFaction)
                .SelectMany(presence => presence.LandedSquads)
                .Sum(SoldierPresenceService.PresentCount);
            List<string> hostile = region.RegionFactionMap.Values
                .Where(presence => presence.IsPublic
                    && !FactionRelationshipService.IsImperial(
                        presence.PlanetFaction.Faction))
                .Select(presence => IntelEstimatePresentationBuilder.Build(
                    presence, currentWeek).Value)
                .Distinct()
                .ToList();
            string playerText = $"Astartes: {player}";
            return hostile.Count == 0
                ? playerText
                : $"{playerText} · Hostile: {string.Join('/', hostile)}";
        }

        private static string IntelligenceOverlay(Region region, int currentWeek)
        {
            List<IntelEstimatePresentation> estimates = region.RegionFactionMap.Values
                .Where(presence => presence.IsPublic
                    && !FactionRelationshipService.IsImperial(presence.PlanetFaction.Faction))
                .Select(presence => IntelEstimatePresentationBuilder.Build(presence, currentWeek))
                .ToList();
            IntelLevel weakest = estimates.Count == 0
                ? IntelLevel.None : estimates.Min(item => item.Level);
            return $"Intel: {RegionFactionExtensions.GetIntelligenceLevelDescription(
                region.GetPlayerVisibleIntel())} · {IntelEstimatePresentationBuilder.Marks(weakest)}";
        }

        private static int CurrentWeek() =>
            GameDataSingleton.Instance?.Date?.GetTotalWeeks() ?? 0;

        private static int CountActivePlayerOrders(Sector sector, Region region) =>
            sector?.Orders.Values.Count(order =>
                order?.Mission?.RegionFaction?.Region == region
                && HasPlayerParticipant(order)) ?? 0;

        private static bool HasPlayerParticipant(Order order) =>
            order?.OwnerFaction?.IsPlayerFaction == true
            || order?.AssignedSquads.Any(squad => squad?.Faction?.IsPlayerFaction == true) == true
            || order?.AssignedCharacters.Any(character =>
                character?.AssignedSquad?.Faction?.IsPlayerFaction == true) == true;

        private static string OrderOverlay(Sector sector, Region region)
        {
            return $"Orders: {CountActivePlayerOrders(sector, region)}";
        }

        private static string DefenseOverlay(
            RegionFaction presence,
            DefenseType type)
        {
            if (presence == null) return "NO DISCLOSED PRESENCE";
            double value = RegionDefenses.GetShared(presence, type);
            return presence.PlanetFaction.Faction.IsPlayerFaction
                || presence.PlanetFaction.Faction.IsDefaultFaction
                ? $"{value:0.##}"
                : RegionFactionExtensions.GetDefenseLevelDescription(value);
        }

        private static string ControlOverlayText(
            RegionControlState state,
            string controllingFactionName) => state switch
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
    }

    public static class PlanetaryOperationsViewModelBuilder
    {
        private static bool HasPlayerParticipant(Order order) =>
            order?.OwnerFaction?.IsPlayerFaction == true
            || order?.AssignedSquads.Any(squad => squad?.Faction?.IsPlayerFaction == true) == true
            || order?.AssignedCharacters.Any(character =>
                character?.AssignedSquad?.Faction?.IsPlayerFaction == true) == true;

        public static WorldDossierViewModel BuildWorld(
            Sector sector,
            Planet planet,
            Region selectedRegion)
        {
            if (planet == null)
            {
                return new WorldDossierViewModel([], [], []);
            }

            Faction player = sector?.PlayerForce?.Faction;
            Region capital = planet.Regions.FirstOrDefault(region =>
                region?.Id == planet.CapitalRegionId) ?? planet.Regions.FirstOrDefault();
            long imperialPopulation = planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Where(presence => presence.IsPublic
                    && FactionRelationshipService.IsImperial(
                        presence.PlanetFaction.Faction))
                .Sum(presence => presence.Population);
            long disclosedHostilePopulation = planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Where(presence => presence.IsPublic
                    && !FactionRelationshipService.IsImperial(
                        presence.PlanetFaction.Faction))
                .Sum(presence => presence.Population);
            int contested = planet.Regions.Count(region =>
                region != null
                && RegionControlPresentation.Build(region).State
                    == RegionControlState.Contested);

            List<DossierCardData> profile =
            [
                new DossierCardData(
                    "World Profile",
                    planet.Name,
                    [
                        Row("Classification", planet.Template?.Name ?? "Unclassified"),
                        Row("Allegiance", planet.GetControllingFaction()?.Name ?? "Contested"),
                        Row("Capital", capital?.Name ?? "Unknown"),
                        Row("Population", planet.Population.ToString("N0")),
                        Row("Imperial", imperialPopulation.ToString("N0")),
                        Row("Contested Regions", contested.ToString()),
                        Row("Hostile / Unaccounted", System.Math.Max(
                            disclosedHostilePopulation,
                            planet.Population - imperialPopulation).ToString("N0")),
                        Row("Tithe Grade", planet.TaxLevel.ToString()),
                        Row("Governor", planet.Governor?.Name ?? "None"),
                        Row("Civil Stability", $"{planet.Stability:0.#}%")
                    ],
                    OnlyWarStyle.Gold)
            ];

            if (planet.Governor?.ActiveRequest is IRequest request)
            {
                profile.Add(new DossierCardData(
                    "Governor's Request",
                    request.Requester?.Name ?? planet.Governor.Name,
                    [
                        Row("Target", request.ThreatFaction?.Name ?? capital?.Name ?? "Planetary"),
                        Row("Deadline", FormatDate(request.Deadline)),
                        Row("Progress", request.FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed
                            ? "Suppress threat"
                            : $"{request.ProgressBattleValueTime:0.#} BV-weeks"),
                        Row("Reward", request.OfferedScheduleKind == PledgeScheduleKind.Standing
                            ? $"{request.OfferedRequisition:N0} Req / {request.OfferedCadenceWeeks} wks"
                            : $"{request.OfferedRequisition:N0} Req")
                    ],
                    OnlyWarStyle.Gold));
            }

            if (sector?.PlayerForce?.RecruitmentProgram is Models.Recruitment.RecruitmentProgram recruitment
                && recruitment.HomeWorldPlanetId == planet.Id)
            {
                profile.Add(new DossierCardData(
                    "Recruitment & Tithe",
                    recruitment.IsSetupComplete ? "Active Chapter World" : "Establishing Program",
                    [
                        Row("Policy", recruitment.Policy.ToString()),
                        Row("Unscreened", recruitment.UnscreenedEligiblePopulation.ToString("N0")),
                        Row("Qualified Candidates", recruitment.QualifiedCandidates.Count.ToString("N0")),
                        Row("Aspirants", recruitment.Aspirants.Count.ToString("N0")),
                        Row("Gene-seed Reserve", sector.PlayerForce.GeneseedStockpile.ToString("N0")),
                        Row("Gene-seed Purity", sector.PlayerForce.GeneseedStockpile > 0
                            ? sector.PlayerForce.GeneseedPurity.ToString("P0") : "--"),
                        Row("Tithe Grade", planet.TaxLevel.ToString())
                    ],
                    OnlyWarStyle.PlayerAccent));
            }

            int landed = CountLandedPlayerForce(sector, planet, player);
            int orbit = CountOrbitingPlayerForce(sector, planet, player);
            int controlled = planet.Regions.Count(region =>
                region != null
                && RegionControlPresentation.Build(region).State
                    == RegionControlState.Imperial);
            List<DossierCardData> strength =
            [
                new DossierCardData(
                    "Imperial Command",
                    "Combined Theater Strength",
                    [
                        Row("Imperial Population", imperialPopulation.ToString("N0")),
                        Row("PDF Strength", planet.PlanetaryDefenseForces.ToString("N0")),
                        Row("Controlled Regions", controlled.ToString()),
                        Row("Astartes Landed", landed.ToString("N0")),
                        Row("Astartes In Orbit", orbit.ToString("N0"))
                    ],
                    OnlyWarStyle.PlayerAccent)
            ];

            foreach (IGrouping<Faction, RegionFaction> hostile in planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Where(presence => presence.IsPublic
                    && !FactionRelationshipService.IsImperial(
                        presence.PlanetFaction.Faction))
                .GroupBy(presence => presence.PlanetFaction.Faction)
                .OrderBy(group => group.Key.Name))
            {
                IntelEstimatePresentation estimate = IntelEstimatePresentationBuilder.BuildWorld(
                    hostile, CurrentCampaignWeek());
                strength.Add(new DossierCardData(
                    "Hostile Faction",
                    hostile.Key.Name,
                    [
                        Row("Force Estimate", estimate.Value),
                        Row("Last Report", estimate.LastReport),
                        Row("Detected Regions", hostile.Select(presence => presence.Region)
                            .Distinct().Count().ToString())
                    ],
                    hostile.Key.Color.ToGodotColor()));
            }

            return new WorldDossierViewModel(
                profile,
                strength,
                BuildRegionCards(selectedRegion, sector));
        }

        public static PlanetaryOperationsHeaderViewModel BuildHeader(
            Sector sector,
            Planet planet)
        {
            if (planet == null) return new PlanetaryOperationsHeaderViewModel("", 0, 0, 0, 0, "No request");
            Faction player = sector?.PlayerForce?.Faction;
            int held = planet.Regions.Count(region => region != null
                && RegionControlPresentation.Build(region).State == RegionControlState.Imperial);
            int landed = CountLandedPlayerForce(sector, planet, player);
            int orbit = CountOrbitingPlayerForce(sector, planet, player);
            IRequest request = planet.Governor?.ActiveRequest;
            string clock = request == null ? "No request"
                : request.Status is RequestStatus.Fulfilled or RequestStatus.Failed
                    ? request.Status.ToString()
                    : $"Request due {FormatDate(request.Deadline)}";
            return new PlanetaryOperationsHeaderViewModel(
                planet.Name, held, planet.Regions.Count(), landed, orbit, clock);
        }

        public static RegionalOperationsViewModel BuildRegional(
            Sector sector,
            Region target,
            AvailableMission selectedMission,
            Order selectedOrder)
        {
            List<Region> origins = target == null
                ? []
                : target.GetSelfAndAdjacentRegions();
            List<AvailableMission> all = origins
                .SelectMany(origin => MissionAvailability.GetAvailableMissions(origin, target))
                .GroupBy(option => option.IdentityKey)
                .Select(group => group.First())
                .OrderBy(option => option.Kind)
                .ThenBy(option => option.Label)
                .ToList();
            List<Order> active = sector?.Orders.Values
                .Where(order => order?.Mission?.RegionFaction?.Region == target
                    && HasPlayerParticipant(order))
                .OrderBy(order => MissionAvailability.GetOrderLabel(order.Mission))
                .ThenBy(order => order.Id)
                .ToList() ?? [];
            return new RegionalOperationsViewModel(
                RegionalOrderEligibilityService.Build(
                    sector, target, selectedMission, selectedOrder),
                BuildRegionCards(target, sector),
                active,
                all.Where(option => option.Kind != MissionAvailabilityKind.Special).ToList(),
                all.Where(option => option.Kind == MissionAvailabilityKind.Special).ToList());
        }

        public static IReadOnlyList<DossierCardData> BuildRegionCards(
            Region region,
            Sector sector = null)
        {
            if (region == null) return [];
            RegionControlPresentationModel control = RegionControlPresentation.Build(region);
            List<(string Label, string Value)> regionRows =
            [
                Row("Control", control.State.ToString()),
                Row("Intelligence", RegionFactionExtensions.GetIntelligenceLevelDescription(
                    region.GetPlayerVisibleIntel())),
                Row("Population", region.HasHiddenDefaultFaction()
                    ? "Unknown"
                    : region.GetVisibleCivilianPopulation().ToString("N0")),
                Row("PDF Strength", region.PlanetaryDefenseForces.ToString("N0")),
                Row("Detected Factions", control.Presences.Count == 0
                    ? "None"
                    : string.Join(", ", control.Presences.Select(item => item.FactionName)))
            ];
            if (FactionActivityPresentation.Build(region) is string factionActivity)
            {
                regionRows.Add(Row("Faction Activity", factionActivity));
            }
            if (sector != null)
            {
                int activeOrders = sector.Orders.Values.Count(order =>
                    order?.Mission?.RegionFaction?.Region == region
                    && HasPlayerParticipant(order));
                regionRows.Add(Row("Active Orders", activeOrders.ToString()));
            }
            List<DossierCardData> cards =
            [
                new DossierCardData(
                    "Selected Region",
                    region.Name,
                    regionRows,
                    OnlyWarStyle.Gold)
            ];

            List<RegionFaction> visiblePresences = region.RegionFactionMap.Values
                .Where(presence => presence.IsPublic
                    || presence.PlanetFaction.Faction.IsPlayerFaction
                    || presence.PlanetFaction.Faction.IsDefaultFaction)
                .ToList();

            List<RegionFaction> imperialPresences = visiblePresences
                .Where(presence => FactionRelationshipService.IsImperial(
                    presence.PlanetFaction.Faction))
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
                    + CountLandedPlayerCharacters(sector, region, imperialSquads);
                List<(string Label, string Value)> forceRows =
                [
                    Row("Forces", imperialForces.ToString("N0"))
                ];
                forceRows.Add(Row("Entrenchment", DescribeDefense(
                    representative, DefenseType.Entrenchment, true)));
                forceRows.Add(Row("Listening Post", DescribeDefense(
                    representative, DefenseType.ListeningPost, true)));
                forceRows.Add(Row("Anti-Air", DescribeDefense(
                    representative, DefenseType.AntiAir, true)));
                cards.Add(new DossierCardData(
                    "Imperial Defenses",
                    representative.PlanetFaction.Faction.Name,
                    forceRows,
                    OnlyWarStyle.PlayerAccent));
            }

            IEnumerable<RegionFaction> hostilePresences = visiblePresences
                .Where(presence => !FactionRelationshipService.IsImperial(
                    presence.PlanetFaction.Faction))
                .OrderBy(presence => presence.PlanetFaction.Faction.Name)
                .ThenBy(presence => presence.PlanetFaction.Faction.Id);

            foreach (RegionFaction presence in hostilePresences)
            {
                IntelEstimatePresentation estimate =
                    IntelEstimatePresentationBuilder.Build(presence, CurrentCampaignWeek());
                List<(string Label, string Value)> forceRows =
                [
                    Row("Forces", estimate.Value)
                ];
                forceRows.Add(Row("Entrenchment", DescribeDefense(
                    presence, DefenseType.Entrenchment, false)));
                forceRows.Add(Row("Listening Post", DescribeDefense(
                    presence, DefenseType.ListeningPost, false)));
                forceRows.Add(Row("Anti-Air", DescribeDefense(
                    presence, DefenseType.AntiAir, false)));
                cards.Add(new DossierCardData(
                    "Hostile Force",
                    presence.PlanetFaction.Faction.Name,
                    forceRows,
                    OnlyWarStyle.OpposingAccent));
            }
            return cards;
        }

        private static string DescribeDefense(
            RegionFaction presence,
            DefenseType type,
            bool exact)
        {
            double value = RegionDefenses.GetShared(presence, type);
            string description = RegionFactionExtensions.GetDefenseLevelDescription(value);
            return exact ? $"{description} ({value:0.##})" : description;
        }

        private static int CountLandedPlayerForce(
            Sector sector,
            Planet planet,
            Faction player)
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
                + CountLandedPlayerCharacters(sector, planet, landedSquads);
        }

        private static int CountLandedPlayerCharacters(
            Sector sector,
            Planet planet,
            IEnumerable<Squad> accountedSquads) =>
            planet == null
                ? 0
                : GetUnrepresentedPlayerCharacters(sector, accountedSquads)
                    .Count(character => CampaignLocationService.ForSoldier(character)?.Region?.Planet
                        == planet);

        private static int CountLandedPlayerCharacters(
            Sector sector,
            Region region,
            IEnumerable<Squad> accountedSquads) =>
            region == null
                ? 0
                : GetUnrepresentedPlayerCharacters(sector, accountedSquads)
                    .Count(character => CampaignLocationService.ForSoldier(character)?.Region
                        == region);

        private static IEnumerable<PlayerSoldier> GetUnrepresentedPlayerCharacters(
            Sector sector,
            IEnumerable<Squad> accountedSquads)
        {
            Faction player = sector?.PlayerForce?.Faction;
            if (player == null) return Enumerable.Empty<PlayerSoldier>();

            HashSet<int> accountedCharacterIds = (accountedSquads
                    ?? Enumerable.Empty<Squad>())
                .Where(squad => squad != null)
                .SelectMany(squad => squad.Members.OfType<PlayerSoldier>())
                .Where(character => character.IndividualPosting == null)
                .Select(character => character.Id)
                .ToHashSet();
            return (sector.PlayerForce.Army?.PlayerSoldierMap?.Values
                    ?? Enumerable.Empty<PlayerSoldier>())
                .Where(character => character != null
                    && character.AssignedSquad?.Faction == player
                    && !accountedCharacterIds.Contains(character.Id));
        }

        private static int CountOrbitingPlayerForce(
            Sector sector,
            Planet planet,
            Faction player)
        {
            if (planet == null || player == null) return 0;
            IReadOnlyList<Ship> orbitingShips = PlanetForceMovementService
                .GetOrbitingPlayerShips(planet, player);
            List<Squad> orbitingSquads = orbitingShips
                .SelectMany(ship => ship.LoadedSquads.Concat(ship.AdministrativeStations))
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .ToList();
            int squadStrength = orbitingShips
                .SelectMany(ship => ship.LoadedSquads.Concat(ship.AdministrativeStations))
                .Where(squad => squad != null)
                .DistinctBy(squad => squad.Id)
                .Sum(SoldierPresenceService.PresentCount);
            HashSet<Ship> shipSet = orbitingShips.ToHashSet();
            int postedCharacters = GetUnrepresentedPlayerCharacters(sector, orbitingSquads)
                .Count(character => shipSet.Contains(
                    CampaignLocationService.ForSoldier(character)?.Ship));
            return squadStrength + postedCharacters;
        }

        private static string FormatDate(Date date) =>
            date == null ? "Unknown" : $"{date.Year:000}.M{date.Millenium} · week {date.Week}";

        private static int CurrentCampaignWeek() =>
            GameDataSingleton.Instance?.Date?.GetTotalWeeks() ?? 0;

        private static ValueTuple<string, string> Row(string label, string value) =>
            new(label, value);
    }
}
