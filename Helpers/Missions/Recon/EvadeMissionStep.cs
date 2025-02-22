using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class EvadeMissionStep : ITestMissionStep
    {
        public string Description { get { return "Evade"; } }
        public IMissionCheck MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public EvadeMissionStep()
        {
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to escape enemy force");
            // modify by speeds of each side
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            float enemySpeed = context.OpposingForces.Average(s => s.GetSquadMove());
            float playerSpeed = context.PlayerSquads.Average(s => s.GetSquadMove());
            float difficulty = 10f - playerSpeed + enemySpeed + marginOfSuccess;
            SquadMissionTest missionTest = new SquadMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
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
