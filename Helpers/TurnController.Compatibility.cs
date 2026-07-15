using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Transitional surface retained for callers and focused tests that historically
    /// reached phase helpers through TurnController. New orchestration should consume
    /// <see cref="TurnResolutionResult"/> or the focused processor directly.
    /// </summary>
    partial class TurnController
    {
        public List<MissionContext> MissionContexts => _lastResult.MissionContexts;
        public List<Mission> SpecialMissions => _lastResult.SpecialMissions;
        public List<StrategicCombatResult> StrategicCombatResults => _lastResult.StrategicCombatResults;
        public string ScenarioNotification => _lastResult.ScenarioNotification;

        public void ProcessScenario(Sector sector)
        {
            EnsureSessionSector(sector);
            if (_scenarioTurnProcessor.TryResolve(sector, out string notification))
            {
                _lastResult.ScenarioNotification = notification;
            }
        }

        internal void ProcessStrategicCombatMissions(IEnumerable<Order> strategicCombatOrders)
        {
            _missionTurnProcessor.ProcessStrategicCombatMissions(
                strategicCombatOrders,
                StrategicCombatResults);
        }

        internal static void ResolveReconResult(
            Faction reconningFaction,
            RegionFaction target,
            float impact,
            Action<PlanetFaction, Region, float> recordIntelGain = null)
        {
            MissionAftermathProcessor.ResolveReconResult(
                reconningFaction,
                target,
                impact,
                recordIntelGain);
        }

        internal static void EstablishInvaderPresence(Faction attacker, Region region, long survivors)
        {
            InvaderPresenceService.Establish(attacker, region, survivors);
        }

        internal void EndOfTurnRegionFactionsUpdate(RegionFaction regionFaction, float pdfRatio)
        {
            _planetTurnProcessor.EndOfTurnRegionFactionsUpdate(regionFaction, pdfRatio);
        }

        internal static void ResolveTyranidExpansion(Planet planet)
        {
            PlanetTurnProcessor.ResolveTyranidExpansion(planet);
        }

        internal static void ResolveBiomassConsumption(Region region)
        {
            PlanetTurnProcessor.ResolveBiomassConsumption(region);
        }

        internal static void RecoverCarryingCapacity(Region region)
        {
            PlanetTurnProcessor.RecoverCarryingCapacity(region);
        }

        internal static void ResolveCultManeuvers(Region region)
        {
            PlanetTurnProcessor.ResolveCultManeuvers(region);
        }

        internal static void UpdateImperialRemnantState(Region region)
        {
            PlanetTurnProcessor.UpdateImperialRemnantState(region);
        }

        internal static void DecayUnmannedDefenses(Region region)
        {
            PlanetTurnProcessor.DecayUnmannedDefenses(region);
        }

        internal static void ProcessImperialEmigration(Region region)
        {
            PlanetTurnProcessor.ProcessImperialEmigration(region);
        }

        public void HandlePublicFactionIntelligence(
            RegionFaction enemyRegionFaction,
            float specMissionBudget)
        {
            _planetTurnProcessor.HandlePublicFactionIntelligence(
                enemyRegionFaction,
                specMissionBudget);
        }

        public void HandleHiddenFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            _planetTurnProcessor.HandleHiddenFactionIntelligence(enemyRegionFaction);
        }
    }
}
