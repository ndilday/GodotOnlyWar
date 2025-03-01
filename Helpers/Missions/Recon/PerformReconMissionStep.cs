using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class PerformReconMissionStep : IMissionStep
    {
        public string Description { get { return "Recon"; } }

        public PerformReconMissionStep()
        {
            
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, 10.0f);
            // move the generation of new missions to the turn controller, rather than the individual mission steps
            context.Log.Add($"Day {context.DaysElapsed}: Force performs reconnisance in {context.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            context.Region.IntelligenceLevel += margin;
            if (context.DaysElapsed >= 6)
            {
                // time to go home
                if (context.Region != context.PlayerSquads.First().Squad.CurrentRegion)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(context, 0.0f, this);
                }
                else if(context.DaysElapsed >= 7)
                {
                    //we don't have to go anywhere so just exit.
                    return;
                }
            }
            else
            {
                new ReconStealthMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
