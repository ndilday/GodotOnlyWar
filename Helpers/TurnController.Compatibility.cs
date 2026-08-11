using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Supply;
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
        public List<StrategicCombatResult> StrategicCombatResults => _lastResult.StrategicCombatResults;
        public List<ConstructionProgressReport> ConstructionReports => _lastResult.ConstructionReports;

        public string ProcessScenario(Sector sector)
        {
            EnsureSessionSector(sector);
            _lastResult.ScenarioNotification = null;
            if (_scenarioTurnProcessor.TryResolve(sector, out string notification))
            {
                _lastResult.ScenarioNotification = notification;
            }
            return _lastResult.ScenarioNotification;
        }
    }
}
