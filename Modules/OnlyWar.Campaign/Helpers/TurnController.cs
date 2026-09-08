using OnlyWar.Helpers.Readiness;
using OnlyWar.Helpers.Turns;
using OnlyWar.Helpers.Medical;
using OnlyWar.Helpers.Missions;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Events;
using OnlyWar.Helpers.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public partial class TurnController
    {
        private readonly TurnOrderPlanner _orderPlanner;
        private readonly ChapterUpkeepProcessor _chapterUpkeepProcessor;
        private readonly FleetTurnProcessor _fleetTurnProcessor;
        private readonly MissionTurnProcessor _missionTurnProcessor;
        private readonly MissionAftermathProcessor _missionAftermathProcessor;
        private readonly PlanetTurnProcessor _planetTurnProcessor;
        private readonly PlanetIntelligenceProcessor _planetIntelligenceProcessor;
        private readonly PlanetForwardSimulator _planetForwardSimulator;
        private readonly ScenarioTurnProcessor _scenarioTurnProcessor;
        private readonly ChapterSupplyTurnProcessor _chapterSupplyTurnProcessor;
        private readonly RecruitmentTurnProcessor _recruitmentTurnProcessor;
        private readonly FactionCapabilityCampaignProcessor _factionCapabilityCampaignProcessor;
        private readonly CampaignTurnContext _turn;
        private readonly TurnIntelligenceLedger _intelLedger;
        private readonly OrganicPopulationGrowthLedger _organicPopulationGrowthLedger;
        private readonly TurnResolutionResult _lastResult;
        private readonly IReadinessDecisions _readiness;
        private readonly IOperationsPersonnelSurface _personnel;
        private readonly IOrderCommitmentSurface _commitments;
        private readonly IEngagementResolver _engagements;
        private readonly IEngagementElementFactory _engagementElements;

        public TurnController(
            ICampaignSimulationSession session,
            IReadinessDecisions readiness,
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments,
            IEngagementResolver engagements,
            IEngagementElementFactory engagementElements,
            ISoldierTrainingService trainingService = null)
        {
            _turn = CampaignTurnContext.From(session);
            _readiness = readiness ?? throw new System.ArgumentNullException(nameof(readiness));
            _personnel = personnel ?? throw new System.ArgumentNullException(nameof(personnel));
            _commitments = commitments ?? throw new System.ArgumentNullException(nameof(commitments));
            _engagements = engagements ?? throw new System.ArgumentNullException(nameof(engagements));
            _engagementElements = engagementElements
                ?? throw new System.ArgumentNullException(nameof(engagementElements));
            _orderPlanner = new TurnOrderPlanner(
                _turn,
                new FactionStrategyController(
                    _turn.Random,
                    _turn.Rules.FactionBehaviorRules,
                    _turn.Identity));
            _chapterUpkeepProcessor = new ChapterUpkeepProcessor(_turn, trainingService);
            _fleetTurnProcessor = new FleetTurnProcessor(_chapterUpkeepProcessor);
            _lastResult = new TurnResolutionResult();
            _intelLedger = new TurnIntelligenceLedger();
            _organicPopulationGrowthLedger = new OrganicPopulationGrowthLedger();
            _planetIntelligenceProcessor = new PlanetIntelligenceProcessor(
                _turn,
                _lastResult.SpecialMissions,
                _intelLedger);
            _planetTurnProcessor = new PlanetTurnProcessor(
                _turn,
                _planetIntelligenceProcessor,
                _organicPopulationGrowthLedger,
                _lastResult.FortificationTransfers,
                _lastResult.GovernorRequestReports);
            _missionTurnProcessor = new MissionTurnProcessor(new MissionTurnDependencies
            {
                Sector = _turn.Sector,
                Rules = _turn.Rules,
                CurrentDate = _turn.CurrentDate,
                Random = _turn.Random,
                Readiness = _readiness,
                Engagements = _engagements,
                MissionRules = new MissionRules(
                    _turn.Rules.Skills.Stealth,
                    _turn.Rules.Skills.Tactics),
                Doctrine = _turn.Sector.PlayerForce?.Army?.ChapterOperationalDoctrine,
                Recruitment = _turn.Sector.PlayerForce?.RecruitmentProgram,
                InvasionForces = _turn.Sector.StrategicInvasionForces,
                FactionRules = _turn.Rules.FactionBehaviorRules,
                Personnel = _personnel,
                EngagementElements = _engagementElements,
                ApplyDailyHealing = MedicalTurnProcessor.ApplyDailyHealing,
                ResolveMedicalSkills = () => FieldCareService.ResolveMedicalSkills(
                    _turn.Rules.RatingDefinitions,
                    _turn.Rules.BaseSkillMap,
                    _turn.Rules.RatingConsumers),
                ApplyDailyFieldCare = (order, report, skills, day, ratings) =>
                    FieldCareService.ApplyDailyFieldCare(
                        _turn.Random, order, report, skills, day, ratings),
                RecordIntelGain = _planetIntelligenceProcessor.RecordIntelGain,
                RecordTargetObservation = _planetIntelligenceProcessor.RecordTargetObservation,
                RecordScenarioPdfLost = ScenarioMetricsCollector.RecordScenarioPdfLost,
                StrategicCommanderCanBeReached =
                    (force, region, margin, random, rules) =>
                        FactionCapabilityCampaignProcessor.StrategicCommanderCanBeReached(
                            force, region, margin, random, rules)
            });
            ConsumptionTurnProcessor.ConfigureScenarioMetrics(
                ScenarioMetricsCollector.RecordScenarioBlighting,
                ScenarioMetricsCollector.RecordScenarioCivilianKills);
            _missionAftermathProcessor = new MissionAftermathProcessor(
                _planetIntelligenceProcessor.RecordReconEvidence,
                ScenarioMetricsCollector.RecordScenarioPdfLost,
                _planetIntelligenceProcessor.RecordTargetObservation);
            _planetForwardSimulator = new PlanetForwardSimulator(
                _turn,
                _orderPlanner,
                _missionTurnProcessor,
                _missionAftermathProcessor,
                _planetTurnProcessor,
                _planetIntelligenceProcessor,
                _intelLedger,
                _lastResult);
            _scenarioTurnProcessor = new ScenarioTurnProcessor(
                _turn, _personnel, _commitments);
            _chapterSupplyTurnProcessor = new ChapterSupplyTurnProcessor(_turn);
            _recruitmentTurnProcessor = new RecruitmentTurnProcessor(
                _turn,
                _organicPopulationGrowthLedger,
                _readiness);
            _factionCapabilityCampaignProcessor = new FactionCapabilityCampaignProcessor(_turn);
        }

        public TurnResolutionResult ProcessTurn(Sector sector)
        {
            EnsureSessionSector(sector);

            // Ending the displayed turn advances the campaign into the week whose events are
            // about to be resolved. Keeping this in the turn controller ensures every caller
            // (including simulations outside the main screen) observes the same campaign date.
            _turn.CurrentDate.IncrementWeek();

            _lastResult.Clear();
            _turn.Sector.PlayerForce?.CurrentTurnEvents.Clear();
            _planetIntelligenceProcessor.ClearTurnGains();
            _organicPopulationGrowthLedger.Clear();
            Faction defaultFaction = _turn.Rules.DefaultFaction;
            ScenarioMetricsCollector.BeginScenarioRegionMetrics(
                ScenarioMetricsCollector.GetScenarioMetricsPlanet(sector),
                defaultFaction);
            InitializeWorldControlEpisodes(sector, defaultFaction);
            HashSet<(int PlanetId, int FactionId)> hiddenCults = SnapshotHiddenCults(sector);
            // Ghost sources and already-active strategic invasion forces resolve before NPC planning, so a newly
            // consolidated force can act in the same week it announces itself.
            _factionCapabilityCampaignProcessor.ProcessWeeklyState(sector);

            // There is no longer a pre-planning shaping phase. Diversions used to resolve here, before
            // NPC planning, so their projected threat could inflate the garrison the enemy chose to
            // hold. That effect is gone deliberately: a feint begun on Monday cannot retroactively
            // change planning the enemy did on Sunday. Diversions now resolve inside the day scheduler
            // with every other mission, where they shape who is looking where each day
            // (OnlyWar_TDD.md §6.4).
            SimulationContext context = new(
                _turn,
                _lastResult,
                _intelLedger,
                sector.Orders.Values);
            List<Order> playerOrdersThisTurn = context.PlayerOrders;
            List<Order> allOrdersThisTurn = context.AllOrders;

            // --- 1. Strategic Planning Phase ---
            // Let each NPC faction generate its orders
            _orderPlanner.AppendNpcOrders(allOrdersThisTurn, sector);

            // --- 2. Mission Execution Phase ---
            var strategicCombatOrders = allOrdersThisTurn.Where(o => o.Mission is StrategicCombatMission);
            _missionTurnProcessor.ProcessStrategicCombatMissions(
                strategicCombatOrders, _lastResult.StrategicCombatResults);
            foreach (StrategicCombatResult strategicResult in _lastResult.StrategicCombatResults)
            {
                if (strategicResult.ControlChanged)
                {
                    _factionCapabilityCampaignProcessor.AffiliateCapturedRegion(sector, strategicResult);
                }
            }
            _factionCapabilityCampaignProcessor.ResolveStrategicLeaderDeaths(
                sector, _lastResult.StrategicCombatResults);

            var combatOrders = allOrdersThisTurn.Where(o =>
                !o.Force.IsEmpty
                && o.Mission?.MissionType != MissionType.Recruitment);
            _missionTurnProcessor.ProcessCombatMissions(
                combatOrders, _lastResult.MissionContexts, _lastResult.ConstructionReports);

            var constructionOrders = allOrdersThisTurn.Where(o => o.Force.IsEmpty && o.Mission is ConstructionMission);
            MissionTurnProcessor.ProcessConstructionOrders(constructionOrders);

            // Feeding rides alongside construction: squad-less, resolved instantly, no mission
            // context. It runs after combat so a consumer that lost ground this week eats on what it
            // still holds (Design/Reference/ConsumptionFeedingAsMission.md).
            var feedOrders = allOrdersThisTurn.Where(o => o.Force.IsEmpty && o.Mission is FeedMission);
            MissionTurnProcessor.ProcessFeedOrders(feedOrders);
            MissionAftermathProcessor.RemoveConsumedSpecialMissions(playerOrdersThisTurn);

            // --- 3. Planetary Simulation & Resolution Phase ---
            _missionAftermathProcessor.ApplyMissionResults(_lastResult.MissionContexts);
            _factionCapabilityCampaignProcessor.AffiliateTacticalCaptures(
                sector, _lastResult.MissionContexts);
            _factionCapabilityCampaignProcessor.ResolveTacticalLeaderDeaths(
                sector, _lastResult.MissionContexts);
            _chapterUpkeepProcessor.ProcessMedical(sector);
            // Days a mission did not need become training credit, so the upkeep pass needs to know how
            // long each squad was actually committed for.
            _chapterUpkeepProcessor.TrainNonDeployedPlayerForces(sector, BuildMissionDaysBySquad());
            _fleetTurnProcessor.AdvanceFleetMovement(sector);
            _planetTurnProcessor.UpdatePlanets(sector.Planets.Values);
            _factionCapabilityCampaignProcessor.ProcessAttractionAndFragmentation(sector);
            RelocateAdministrativeStationsAfterHomeWorldLoss(sector);
            RecordStrategicNarrativeEvents(sector, defaultFaction, hiddenCults);
            _lastResult.RecruitmentReport = _recruitmentTurnProcessor.Process();
            MissionAftermathProcessor.PruneInvalidSpecialMissions(sector.Planets.Values);
            _planetIntelligenceProcessor.RefreshSpecialMissions(sector.Planets.Values);
            _chapterSupplyTurnProcessor.ProcessDeliveries();

            // --- 4. Scenario Resolution Phase ---
            // Resolve the opening objective after the planet sim has settled this turn, so the
            // win/lapse checks read the post-combat, post-growth state of the promised world.
            if (_scenarioTurnProcessor.TryResolve(sector, out string scenarioNotification))
            {
                _lastResult.ScenarioNotification = scenarioNotification;
            }
            ScenarioMetricsCollector.LogScenarioRegionMetrics($"date={_turn.CurrentDate}");
            ScenarioMetricsCollector.EndScenarioRegionMetrics();
            MissionAftermathProcessor.CleanupResolvedPlayerOrders(sector, playerOrdersThisTurn);
            _lastResult.CampaignEvents.AddRange(_turn.Sector.PlayerForce?.CurrentTurnEvents ?? []);
            _lastResult.CampaignIdentity = _turn.Sector.PlayerForce?.CampaignIdentity;
            ChapterChronicleProjector.ReconcileRecent(
                _turn.Sector.PlayerForce?.CampaignEventLedger,
                _turn.Sector.PlayerForce?.ChapterChronicle,
                _turn.Sector.PlayerForce?.CurrentTurnEvents,
                _turn.Sector.PlayerForce?.CampaignIdentity);
            return _lastResult;
        }

        private void RelocateAdministrativeStationsAfterHomeWorldLoss(Sector sector)
        {
            PlayerForce force = sector?.PlayerForce;
            if (force?.HomeWorldPlanetId == null || force.Army?.OrderOfBattle == null)
            {
                return;
            }

            Planet homeWorld = sector.Planets.GetValueOrDefault(force.HomeWorldPlanetId.Value);
            if (homeWorld == null || homeWorld.GetControllingFaction() == force.Faction)
            {
                return;
            }

            List<Squad> stationedOnHomeWorld = force.Army.OrderOfBattle.GetAllSquads()
                .Where(squad => squad.PermitsIndividualDeployment
                    && squad.DutyStation?.Region?.Planet == homeWorld)
                .ToList();
            if (stationedOnHomeWorld.Count == 0)
            {
                return;
            }

            List<Ship> ships = force.Fleet?.TaskForces
                .SelectMany(taskForce => taskForce.Ships)
                .ToList() ?? [];
            Ship flagship = new FlagshipService().EnsureSinglePlayerFlagship(force.Faction, ships);
            AdministrativeStationResult result = new AdministrativeStationService(
                    _personnel, _commitments)
                .MoveAllToFlagship(force.Army.OrderOfBattle, flagship);
            if (!result.Succeeded)
            {
                GameLog.Warn(() =>
                    $"Administrative stations could not leave lost Home World {homeWorld.Name}: "
                    + result.Message);
            }
        }

        private void InitializeWorldControlEpisodes(Sector sector, Faction imperialFaction)
        {
            PlayerForce force = sector.PlayerForce;
            foreach (Planet planet in sector.Planets.Values)
            {
                Faction controller = planet.GetControllingFaction();
                force?.WorldControlEpisodes.Observe(
                    planet.Id,
                    planet.Name,
                    imperialFaction.Id,
                    controller?.Id,
                    planet.IsContested(),
                    _turn.CurrentDate.GetTotalWeeks(),
                    isImperialControlled: controller != null
                        && FactionRelationshipService.IsImperial(controller));
            }
        }

        private void RecordStrategicNarrativeEvents(
            Sector sector,
            Faction imperialFaction,
            HashSet<(int PlanetId, int FactionId)> previouslyHiddenCults)
        {
            PlayerForce force = sector.PlayerForce;
            if (force == null) return;
            int week = _turn.CurrentDate.GetTotalWeeks();
            foreach (Planet planet in sector.Planets.Values)
            {
                bool participated = force.CurrentTurnEvents.Any(@event =>
                    (@event.Type is CampaignEventType.BattleParticipation
                        or CampaignEventType.BattleResolved
                        or CampaignEventType.MissionOutcome)
                    && @event.Entities.Any(entity => entity.Kind == CampaignEntityKind.Planet
                        && entity.EntityId == planet.Id));
                Faction controller = planet.GetControllingFaction();
                WorldControlChangedPayload completed = force.WorldControlEpisodes.Observe(
                    planet.Id,
                    planet.Name,
                    imperialFaction.Id,
                    controller?.Id,
                    planet.IsContested(),
                    week,
                    participated,
                    controller != null && FactionRelationshipService.IsImperial(controller));
                if (completed != null)
                    force.GetCampaignEventRecorder().RecordWorldControlChanged(completed);

                foreach (PlanetFaction presence in planet.PlanetFactionMap.Values.Where(item =>
                    item.IsPublic
                    && item.Faction.GrowthType == GrowthType.Conversion
                    && previouslyHiddenCults.Contains((planet.Id, item.Faction.Id))))
                {
                    force.GetCampaignEventRecorder().RecordHiddenCultRevealed(
                        planet.Id, planet.Name, presence.Faction.Id, presence.Faction.Name, week);
                }
            }
        }

        private static HashSet<(int PlanetId, int FactionId)> SnapshotHiddenCults(Sector sector) =>
            sector.Planets.Values
                .SelectMany(planet => planet.PlanetFactionMap.Values
                    .Where(presence => !presence.IsPublic
                        && presence.Faction.GrowthType == GrowthType.Conversion)
                    .Select(presence => (planet.Id, presence.Faction.Id)))
                .ToHashSet();

        // Runs a planet-scoped slice of the weekly turn for a single world, for the given number of
        // weeks. Used by the opening-scenario stamp to let the promised world evolve during
        // generation before the player arrives — the revealed cult grinds the PDF down, then the
        // stranded Tyranid swarm feeds and spreads (Design/Reference/OpeningScenario.md, "Opening
        // Scenario Application"). It deliberately omits everything that is not local to this planet
        // or that belongs to the player's own upkeep: no player training or medical, no fleet
        // movement, no other planets, and no scenario resolution (the scenario is not yet assigned
        // during generation). The date is not advanced, so the Chapter's founding date is unaffected.
        public void SimulatePlanetForward(Sector sector, Planet planet, int turns)
        {
            EnsureSessionSector(sector);
            _planetForwardSimulator.Simulate(sector, planet, turns);
        }

        // Longest mission each squad ran this turn. A squad can appear in more than one mission context
        // when an order fans out into independent elements (recon), so the maximum is the honest read of
        // how much of its week was spoken for.
        private Dictionary<int, int> BuildMissionDaysBySquad()
        {
            Dictionary<int, int> daysBySquad = new();
            foreach (MissionContext context in _lastResult.MissionContexts)
            {
                foreach (OperationalMissionElement element in context.MissionSquads)
                {
                    int squadId = element.CampaignSquad?.Id ?? element.Id;
                    int days = context.DaysElapsed;
                    daysBySquad[squadId] = daysBySquad.TryGetValue(squadId, out int existing)
                        ? System.Math.Max(existing, days)
                        : days;
                }
            }
            return daysBySquad;
        }

        private void EnsureSessionSector(Sector sector)
        {
            if (sector == null)
            {
                throw new System.ArgumentNullException(nameof(sector));
            }
            if (!ReferenceEquals(sector, _turn.Sector))
            {
                throw new System.ArgumentException(
                    "The supplied sector must be the sector owned by this game session.",
                    nameof(sector));
            }
        }

    }
}
