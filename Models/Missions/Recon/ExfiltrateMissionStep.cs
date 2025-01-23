using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Missions.Recon
{
    public class ExfiltrateMissionStep : ITestMissionStep
    {
        private readonly IMissionTest _missionTest;

        public string Description { get { return "Infiltrate"; } }
        public IMissionTest MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public ExfiltrateMissionStep()
        {
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            _missionTest = new SquadMissionTest(stealth, 10.0f);
            StepIfSuccess = new ReconStealthMissionStep();
            StepIfFailure = new DetectedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            if(context.Squad.Members.All(s => s.MoveSpeed == 0.0f))
            {
                // they're dead, Jim
                return;
            }
            float margin = _missionTest.RunMissionTest(context.Squad);
            if (margin > 0.0f)
            {
                StepIfSuccess.ExecuteMissionStep(context, margin, this);
            }
            else
            {
                StepIfFailure.ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
