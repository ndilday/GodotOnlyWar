using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Missions.Recon
{
    public class DetectedMissionStep : ITestMissionStep
    {
        private readonly IMissionTest _missionTest;

        public string Description { get { return "Detected"; } }
        public IMissionTest MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public DetectedMissionStep()
        {
            BaseSkill perception = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Perception");
            _missionTest = new SquadMissionTest(perception, 10.0f);
            StepIfSuccess = new CrossDetectionMissionStep();
            StepIfFailure = new AmbushedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // build OpFor, size increases the lower the MoS, and pushes engagement range in favor of the OpFor
            int numberOfOpposingSquads = 1 - (ushort)marginOfSuccess;
            // any fractional value of margin of Success is treated as the probability of an additional squad being added.
            float fraction = Math.Abs( marginOfSuccess - (ushort)marginOfSuccess );
            if (RNG.GetLinearDouble() < fraction)
            {
                numberOfOpposingSquads++; 
            }

            // shouldn't all be the same squad type
            // a flexible, but verbose method would be to define a table in the game rules that maps some concept of "situation" and faction ID to "lottery balls". 
            // Then, here we would total the number of qualifying units lottery balls, and roll an int against that to generate a reasonable mix of units.

            float margin = _missionTest.RunMissionTest(context.Squad);
            if (margin > 0.0f)
            {
                StepIfSuccess.ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                StepIfFailure.ExecuteMissionStep(context, margin, returnStep);
            }
        }
    }
}
