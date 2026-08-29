using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class CrossDetectionMissionStep : IMissionStep
    {
        public string Description { get { return "Cross-Detection"; } }

        public CrossDetectionMissionStep()
        {
        }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            // decide whether to fight or flee
            // compare size of each force
            float opForSize = context.OpposingSquads.Sum(s => s.AbleSoldiers
                .Sum(member => member.Soldier.Template.BattleValue));
            float attackerSize = context.MissionSquads.Sum(s => s.AbleSoldiers
                .Sum(member => member.Soldier.Template.BattleValue));
            if(context.Order.LevelOfAggression == Aggression.Attritional)
            {
                attackerSize *= 2;
            }
            else if(context.Order.LevelOfAggression == Aggression.Cautious)
            {
                opForSize *= 2;
            }
            if(attackerSize >= opForSize || context.Order.LevelOfAggression == Aggression.Aggressive)
            {
                return MissionStepResult.Continue(
                    new MeetingEngagementMissionStep(), marginOfSuccess, resumeStep);
            }
            // attempt to flee
            return MissionStepResult.Continue(
                new ExfiltrateMissionStep(), marginOfSuccess, resumeStep);
        }
    }
}
