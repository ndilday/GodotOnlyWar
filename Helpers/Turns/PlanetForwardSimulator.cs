using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Runs the planet-scoped subset of weekly resolution used while stamping the
    /// opening scenario during sector generation.
    /// </summary>
    internal sealed class PlanetForwardSimulator
    {
        private readonly GameSession _session;
        private readonly TurnOrderPlanner _orderPlanner;
        private readonly MissionTurnProcessor _missionTurnProcessor;
        private readonly MissionAftermathProcessor _missionAftermathProcessor;
        private readonly PlanetTurnProcessor _planetTurnProcessor;
        private readonly TurnIntelLedger _intelLedger;
        private readonly TurnResolutionResult _result;

        internal PlanetForwardSimulator(
            GameSession session,
            TurnOrderPlanner orderPlanner,
            MissionTurnProcessor missionTurnProcessor,
            MissionAftermathProcessor missionAftermathProcessor,
            PlanetTurnProcessor planetTurnProcessor,
            TurnIntelLedger intelLedger,
            TurnResolutionResult result)
        {
            _session = session;
            _orderPlanner = orderPlanner;
            _missionTurnProcessor = missionTurnProcessor;
            _missionAftermathProcessor = missionAftermathProcessor;
            _planetTurnProcessor = planetTurnProcessor;
            _intelLedger = intelLedger;
            _result = result;
        }

        internal void Simulate(Sector sector, Planet planet, int turns)
        {
            Faction defaultFaction = _session.Rules.DefaultFaction;

            GameLog.Info(() => $"SimulatePlanetForward '{planet.Name}': {turns} turns");
            for (int week = 0; week < turns; week++)
            {
                System.Diagnostics.Stopwatch weekTimer = System.Diagnostics.Stopwatch.StartNew();
                _result.Clear();
                _planetTurnProcessor.ClearTurnIntelGains();
                ScenarioMetricsCollector.BeginScenarioRegionMetrics(planet, defaultFaction);

                SimulationContext context = new(
                    _session,
                    _result,
                    _intelLedger,
                    planetScope: planet);
                List<Order> orders = context.AllOrders;
                _orderPlanner.AppendNpcOrders(orders, sector, planet);

                List<Order> strategicCombatOrders = orders
                    .Where(order => order.Mission is StrategicCombatMission)
                    .ToList();
                _missionTurnProcessor.ProcessStrategicCombatMissions(
                    strategicCombatOrders,
                    _result.StrategicCombatResults);

                List<Order> combatOrders = orders
                    .Where(order => order.AssignedSquads.Any())
                    .ToList();
                GameLog.Debug(() =>
                    $"  week {week + 1}/{turns} '{planet.Name}': {orders.Count} orders, "
                    + $"{strategicCombatOrders.Count} strategic, {combatOrders.Count} combat "
                    + $"({combatOrders.Sum(order => order.AssignedSquads.Sum(squad => squad.Members.Count))} soldiers committed)");

                _missionTurnProcessor.ProcessCombatMissions(
                    combatOrders,
                    _result.MissionContexts);
                MissionTurnProcessor.ProcessConstructionOrders(
                    orders.Where(order =>
                        !order.AssignedSquads.Any()
                        && order.Mission is ConstructionMission));

                _missionAftermathProcessor.ApplyMissionResults(_result.MissionContexts);
                _planetTurnProcessor.UpdatePlanet(planet);
                MissionAftermathProcessor.PruneInvalidSpecialMissions(new[] { planet });
                _planetTurnProcessor.UpdateIntelligence(planet);
                ScenarioMetricsCollector.LogScenarioRegionMetrics(
                    $"generationWeek={week + 1}/{turns}");
                ScenarioMetricsCollector.EndScenarioRegionMetrics();

                GameLog.Info(() =>
                    $"  week {week + 1}/{turns} '{planet.Name}' done in {weekTimer.ElapsedMilliseconds}ms");
            }
        }
    }
}
