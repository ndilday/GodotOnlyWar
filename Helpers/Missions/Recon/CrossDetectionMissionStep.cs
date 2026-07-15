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

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            // decide whether to fight or flee
            context.AddLog($"Day {context.DaysElapsed}: Force is detected by enemy forces in {context.Order.Mission.RegionFaction.Region.Name}");
            // compare size of each force
            float opForSize = context.OpposingSquads.Sum(s => s.Squad.SquadTemplate.BattleValue);
            float attackerSize = context.MissionSquads.Sum(s => s.Squad.SquadTemplate.BattleValue);
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
                new MeetingEngagementMissionStep().ExecuteMissionStep(
                    execution,
                    marginOfSuccess,
                    returnStep);
            }
            else
            {
                // attempt to flee
                new ExfiltrateMissionStep().ExecuteMissionStep(
                    execution,
                    marginOfSuccess,
                    returnStep);
            }
        }
    }
}
