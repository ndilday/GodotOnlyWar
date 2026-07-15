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

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            BaseSkill tactics = execution.Rules.Tactics;
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, 10.0f);
            // move the generation of new missions to the turn controller, rather than the individual mission steps
            context.AddLog($"Day {context.DaysElapsed}: Force performs reconnisance in {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            // a particularly bad result means bad intel
            if(margin > 0 || margin < -0.5f)
            {
                context.Impact += margin;
            }

            if (context.DaysElapsed >= 6)
            {
                // time to go home
                if (context.Order.Mission.RegionFaction.Region != context.MissionSquads.First().Squad.CurrentRegion)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(execution, 0.0f, this);
                }
                else if(context.DaysElapsed >= 7)
                {
                    //we don't have to go anywhere so just exit.
                    return;
                }
            }
            else
            {
                new ReconStealthMissionStep().ExecuteMissionStep(execution, marginOfSuccess, this);
            }
        }
    }
}
