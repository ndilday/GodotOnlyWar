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
