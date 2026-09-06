using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Supply;
using OnlyWar.Models.Events;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Collects the player-facing output produced by one resolved campaign turn.
    /// Keeping this state together lets the controller orchestrate phases without also
    /// serving as the data store shared by every processor.
    /// </summary>
    public sealed class TurnResolutionResult
    {
        public List<MissionContext> MissionContexts { get; } = new();
        public List<Mission> SpecialMissions { get; } = new();
        public List<StrategicCombatResult> StrategicCombatResults { get; } = new();
        // Squad-borne construction resolves without producing a MissionContext, so its outcome has
        // to be carried out of the turn separately or the end-of-turn report cannot mention it.
        public List<ConstructionProgressReport> ConstructionReports { get; } = new();
        // Works that changed hands this turn because the faction holding them left the region.
        public List<FortificationTransferReport> FortificationTransfers { get; } = new();
        // Governor requests that arrived, were fulfilled, or lapsed this turn. These resolve
        // inside the planetary sim without producing a MissionContext, so like construction they
        // have to be carried out of the turn separately or the report cannot mention them.
        public List<GovernorRequestReport> GovernorRequestReports { get; } = new();
        public RecruitmentTurnReport RecruitmentReport { get; set; }
        public string ScenarioNotification { get; set; }
        public List<CampaignEvent> CampaignEvents { get; } = new();
        public CampaignIdentity CampaignIdentity { get; set; }

        public void Clear()
        {
            MissionContexts.Clear();
            SpecialMissions.Clear();
            StrategicCombatResults.Clear();
            ConstructionReports.Clear();
            FortificationTransfers.Clear();
            GovernorRequestReports.Clear();
            RecruitmentReport = null;
            ScenarioNotification = null;
            CampaignEvents.Clear();
            CampaignIdentity = null;
        }
    }
}
