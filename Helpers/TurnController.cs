using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    partial class TurnController
    {
        private readonly TurnOrderPlanner _orderPlanner;
        private readonly ChapterUpkeepProcessor _chapterUpkeepProcessor;
        private readonly FleetTurnProcessor _fleetTurnProcessor;
        private readonly MissionTurnProcessor _missionTurnProcessor;
        private readonly MissionAftermathProcessor _missionAftermathProcessor;
        private readonly PlanetTurnProcessor _planetTurnProcessor;
        private readonly PlanetForwardSimulator _planetForwardSimulator;
        private readonly ScenarioTurnProcessor _scenarioTurnProcessor;
        private readonly ChapterSupplyTurnProcessor _chapterSupplyTurnProcessor;
        private readonly RecruitmentTurnProcessor _recruitmentTurnProcessor;
        private readonly GameSession _session;
        private readonly TurnIntelLedger _intelLedger;
        private readonly TurnResolutionResult _lastResult;

        public TurnController() : this(CreateCurrentSession(), null)
        {
        }

        public TurnController(ISoldierTrainingService trainingService)
            : this(CreateCurrentSession(), trainingService)
        {
        }

        internal TurnController(
            GameSession session,
            ISoldierTrainingService trainingService = null)
        {
            _session = session ?? throw new System.ArgumentNullException(nameof(session));
            _orderPlanner = new TurnOrderPlanner(_session, new FactionStrategyController());
            _chapterUpkeepProcessor = new ChapterUpkeepProcessor(_session, trainingService);
            _fleetTurnProcessor = new FleetTurnProcessor(_chapterUpkeepProcessor);
            _lastResult = new TurnResolutionResult();
            _intelLedger = new TurnIntelLedger();
            _planetTurnProcessor = new PlanetTurnProcessor(
                _session,
                _lastResult.SpecialMissions,
                _intelLedger,
                _lastResult.FortificationTransfers,
                _lastResult.GovernorRequestReports);
            _missionTurnProcessor = new MissionTurnProcessor(
                _session,
                _planetTurnProcessor.RecordIntelGain,
                ScenarioMetricsCollector.RecordScenarioPdfLost);
            _missionAftermathProcessor = new MissionAftermathProcessor(
                _planetTurnProcessor.RecordReconEvidence,
                ScenarioMetricsCollector.RecordScenarioPdfLost);
            _planetForwardSimulator = new PlanetForwardSimulator(
                _session,
                _orderPlanner,
                _missionTurnProcessor,
                _missionAftermathProcessor,
                _planetTurnProcessor,
                _intelLedger,
                _lastResult);
            _scenarioTurnProcessor = new ScenarioTurnProcessor(_session);
            _chapterSupplyTurnProcessor = new ChapterSupplyTurnProcessor(_session);
            _recruitmentTurnProcessor = new RecruitmentTurnProcessor(
                _session, _planetTurnProcessor);
        }

        public TurnResolutionResult ProcessTurn(Sector sector)
        {
            EnsureSessionSector(sector);

            // Ending the displayed turn advances the campaign into the week whose events are
            // about to be resolved. Keeping this in the turn controller ensures every caller
            // (including simulations outside the main screen) observes the same campaign date.
            _session.CurrentDate.IncrementWeek();

            _lastResult.Clear();
            _planetTurnProcessor.ClearTurnIntelGains();
            _planetTurnProcessor.ClearOrganicPopulationGrowth();
            Faction defaultFaction = _session.Rules.DefaultFaction;
            ScenarioMetricsCollector.BeginScenarioRegionMetrics(
                ScenarioMetricsCollector.GetScenarioMetricsPlanet(sector),
                defaultFaction);

            // There is no longer a pre-planning shaping phase. Diversions used to resolve here, before
            // NPC planning, so their projected threat could inflate the garrison the enemy chose to
            // hold. That effect is gone deliberately: a feint begun on Monday cannot retroactively
            // change planning the enemy did on Sunday. Diversions now resolve inside the day scheduler
            // with every other mission, where they shape who is looking where each day
            // (Design/Active/DailyMissionResolution.md §4).
            SimulationContext context = new(
                _session,
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
            _missionTurnProcessor.ProcessStrategicCombatMissions(strategicCombatOrders, StrategicCombatResults);

            var combatOrders = allOrdersThisTurn.Where(o => o.AssignedSquads.Any());
            _missionTurnProcessor.ProcessCombatMissions(
                combatOrders, MissionContexts, ConstructionReports);

            var constructionOrders = allOrdersThisTurn.Where(o => !o.AssignedSquads.Any() && o.Mission is ConstructionMission);
            MissionTurnProcessor.ProcessConstructionOrders(constructionOrders);
            MissionAftermathProcessor.RemoveConsumedSpecialMissions(playerOrdersThisTurn);

            // --- 3. Planetary Simulation & Resolution Phase ---
            _missionAftermathProcessor.ApplyMissionResults(MissionContexts);
            _chapterUpkeepProcessor.ProcessMedical(sector);
            // Days a mission did not need become training credit, so the upkeep pass needs to know how
            // long each squad was actually committed for.
            _chapterUpkeepProcessor.TrainNonDeployedPlayerForces(sector, BuildMissionDaysBySquad());
            _fleetTurnProcessor.AdvanceFleetMovement(sector);
            _planetTurnProcessor.UpdatePlanets(sector.Planets.Values);
            _lastResult.RecruitmentReport = _recruitmentTurnProcessor.Process();
            MissionAftermathProcessor.PruneInvalidSpecialMissions(sector.Planets.Values);
            _planetTurnProcessor.UpdateIntelligence(sector.Planets.Values);
            _chapterSupplyTurnProcessor.ProcessDeliveries();

            // --- 4. Scenario Resolution Phase ---
            // Resolve the opening objective after the planet sim has settled this turn, so the
            // win/lapse checks read the post-combat, post-growth state of the promised world.
            ProcessScenario(sector);
            ScenarioMetricsCollector.LogScenarioRegionMetrics($"date={_session.CurrentDate}");
            ScenarioMetricsCollector.EndScenarioRegionMetrics();
            MissionAftermathProcessor.CleanupResolvedPlayerOrders(sector, playerOrdersThisTurn);
            return _lastResult;
        }

        // Runs a planet-scoped slice of the weekly turn for a single world, for the given number of
        // weeks. Used by the opening-scenario stamp to let the promised world evolve during
        // generation before the player arrives — the revealed cult grinds the PDF down, then the
        // stranded Tyranid swarm feeds and spreads (Design/OpeningScenario.md §4.24, "Opening
        // Scenario Application"). It deliberately omits everything that is not local to this planet
        // or that belongs to the player's own upkeep: no player training or medical, no fleet
        // movement, no other planets, and no scenario resolution (the scenario is not yet assigned
        // during generation). The date is not advanced, so the Chapter's founding date is unaffected.
        internal void SimulatePlanetForward(Sector sector, Planet planet, int turns)
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
            foreach (MissionContext context in MissionContexts)
            {
                foreach (Battles.BattleSquad battleSquad in context.MissionSquads)
                {
                    int squadId = battleSquad.Squad?.Id ?? battleSquad.Id;
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
            if (!ReferenceEquals(sector, _session.Sector))
            {
                throw new System.ArgumentException(
                    "The supplied sector must be the sector owned by this game session.",
                    nameof(sector));
            }
        }

        private static GameSession CreateCurrentSession()
        {
            GameDataSingleton gameData = GameDataSingleton.Instance;
            return new GameSession(
                gameData.GameRulesData,
                gameData.Sector,
                gameData.Date,
                StaticRNG.Instance);
        }
    }
}
