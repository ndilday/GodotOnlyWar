using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class CrossDetectionMissionStep : IMissionStep
    {
        public string Description { get { return "Cross-Detection"; } }

        public CrossDetectionMissionStep()
        {
            BaseSkill perception = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // decide whether to fight or flee
            context.Log.Add($"Day {context.DaysElapsed}: Force is detected by enemy forces in {context.Region.Name}");
            // compare size of each force
            float opForSize = context.OpposingForces.Sum(s => s.SquadTemplate.BattleValue);
            float playerSize = context.PlayerSquads.Sum(s => s.SquadTemplate.BattleValue);
            if(context.Aggression == Aggression.Attritional)
            {
                playerSize *= 2;
            }
            else if(context.Aggression == Aggression.Cautious)
            {
                opForSize *= 2;
            }
            if(playerSize >= opForSize || context.Aggression == Aggression.Aggressive)
            {
                new MeetingEngagementMissionStep().ExecuteMissionStep(context, 0.0f, returnStep);
            }
            else
            {
                // attempt to flee
                new ExfiltrateMissionStep().ExecuteMissionStep(context, 0.0f, this);
            }
        }
    }
}
