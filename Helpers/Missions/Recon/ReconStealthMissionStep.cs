using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class ReconStealthMissionStep : ITestMissionStep
    {
        private readonly IMissionTest _missionTest;

        public string Description { get { return "Recon"; } }
        public IMissionTest MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public ReconStealthMissionStep()
        {
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            _missionTest = new SquadMissionTest(stealth, 10.0f);
            StepIfSuccess = new PerformReconMissionStep();
            StepIfFailure = new DetectedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            if (context.DaysElapsed >= 6)
            {
                // time to go home

            }
            else
            {
                context.DaysElapsed++;
                float margin = _missionTest.RunMissionTest(context.PlayerSquads);
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
}
