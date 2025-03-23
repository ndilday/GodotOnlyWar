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

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // decide whether to fight or flee
            context.Log.Add($"Day {context.DaysElapsed}: Force is detected by enemy forces in {context.Order.Mission.RegionFaction.Region.Name}");
            // compare size of each force
            float opForSize = context.OpposingForces.Sum(s => s.Squad.SquadTemplate.BattleValue);
            float playerSize = context.PlayerSquads.Sum(s => s.Squad.SquadTemplate.BattleValue);
            if(context.Order.LevelOfAggression == Aggression.Attritional)
            {
                playerSize *= 2;
            }
            else if(context.Order.LevelOfAggression == Aggression.Cautious)
            {
                opForSize *= 2;
            }
            if(playerSize >= opForSize || context.Order.LevelOfAggression == Aggression.Aggressive)
            {
                new MeetingEngagementMissionStep().ExecuteMissionStep(context, marginOfSuccess, returnStep);
            }
            else
            {
                // attempt to flee
                new ExfiltrateMissionStep().ExecuteMissionStep(context, marginOfSuccess, returnStep);
            }
        }
    }
}
