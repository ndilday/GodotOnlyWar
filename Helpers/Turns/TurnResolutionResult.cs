using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Supply;
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
        // Squad-borne construction resolves without producing a MissionContext, so its outcome has
        // to be carried out of the turn separately or the end-of-turn report cannot mention it.
        internal List<ConstructionProgressReport> ConstructionReports { get; } = new();
        // Works that changed hands this turn because the faction holding them left the region.
        internal List<FortificationTransferReport> FortificationTransfers { get; } = new();
        // Governor requests that arrived, were fulfilled, or lapsed this turn. These resolve
        // inside the planetary sim without producing a MissionContext, so like construction they
        // have to be carried out of the turn separately or the report cannot mention them.
        internal List<GovernorRequestReport> GovernorRequestReports { get; } = new();
        internal RecruitmentTurnReport RecruitmentReport { get; set; }
        internal string ScenarioNotification { get; set; }

        internal void Clear()
        {
            MissionContexts.Clear();
            SpecialMissions.Clear();
            StrategicCombatResults.Clear();
            ConstructionReports.Clear();
            FortificationTransfers.Clear();
            GovernorRequestReports.Clear();
            RecruitmentReport = null;
            ScenarioNotification = null;
        }
    }
}
