using OnlyWar.Models.Soldiers;
using System;
using System.Linq;

namespace OnlyWar.Models.Missions.Recon
{
    public class InfiltrateMissionStep : ITestMissionStep
    {
        private readonly IMissionTest _missionTest;

        public string Description { get { return "Infiltrate"; } }
        public IMissionTest MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public InfiltrateMissionStep()
        {
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            _missionTest = new SquadMissionTest(stealth, 10.0f);
            StepIfSuccess = new ReconStealthMissionStep();
            StepIfFailure = new DetectedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            if (!ShouldContinue(context)) return;
            context.DaysElapsed++;
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

        public bool ShouldContinue(MissionContext context)
        {
            if (context.DaysElapsed >= 6 || context.Squad.Members.All(s => s.MoveSpeed == 0)) return false;
            return true;
        }
    }
}
