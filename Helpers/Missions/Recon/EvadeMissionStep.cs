using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class EvadeMissionStep : IMissionStep
    {
        public string Description { get { return "Evade"; } }

        public EvadeMissionStep(){}

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to escape enemy force");
            // modify by speeds of each side
            // TODO: increase difficulty based on enemy force size?
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            float enemySpeed = context.OpposingSquads.Average(s => s.GetSquadMove());
            float playerSpeed = context.MissionSquads.Average(s => s.GetSquadMove());
            float difficulty = 10f - playerSpeed + enemySpeed + marginOfSuccess;
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            if (margin > 0.0f)
            {
                context.Log.Add($"Day {context.DaysElapsed}: Force successfully escaped enemy force");
                returnStep.ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                // attempt failed
                context.Log.Add($"Day {context.DaysElapsed}: Escape failed");
                new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, returnStep);
            }

        }
    }
}
