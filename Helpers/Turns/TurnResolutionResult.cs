using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models.Missions;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Collects the player-facing output produced by one resolved campaign turn.
    /// Keeping this state together lets the controller orchestrate phases without also
    /// serving as the data store shared by every processor.
    /// </summary>
    internal sealed class TurnResolutionResult
    {
        internal List<MissionContext> MissionContexts { get; } = new();
        internal List<Mission> SpecialMissions { get; } = new();
        internal List<StrategicCombatResult> StrategicCombatResults { get; } = new();
        internal string ScenarioNotification { get; set; }

        internal void Clear()
        {
            MissionContexts.Clear();
            SpecialMissions.Clear();
            StrategicCombatResults.Clear();
            ScenarioNotification = null;
        }
    }
}
