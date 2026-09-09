using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain;
using System.Linq;

namespace OnlyWar.Operations.Missions.Recon
{
    public class EvadeMissionStep : IMissionStep
    {
        public string Description { get { return "Evade"; } }

        public EvadeMissionStep(){}

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            context.AddLog($"Day {context.DaysElapsed}: Force attempting to escape enemy force");
            // modify by speeds of each side
            // TODO: increase difficulty based on enemy force size?
            BaseSkill tactics = execution.Rules.Tactics;
            float enemySpeed = context.OpposingSquads.Average(s => s.GetSquadMove());
            float attackerSpeed = context.MissionSquads.Average(s => s.GetSquadMove());
            float difficulty = 10f - attackerSpeed + enemySpeed + marginOfSuccess;
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            if (margin > 0.0f)
            {
                context.ForceBrokeContact = true;
                context.AddLog($"Day {context.DaysElapsed}: Force successfully escaped enemy force");
                // A null resume target ends the mission here rather than throwing, which is what the
                // old unconditional returnStep.ExecuteMissionStep would have done.
                return MissionStepResult.Continue(resumeStep, margin, resumeStep);
            }
            // attempt failed
            context.AddLog($"Day {context.DaysElapsed}: Escape failed");
            return MissionStepResult.Continue(
                new MeetingEngagementMissionStep(), margin, resumeStep);
        }
    }
}
