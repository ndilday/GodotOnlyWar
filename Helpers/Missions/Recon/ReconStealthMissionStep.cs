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
        private readonly IMissionCheck _missionTest;

        public string Description { get { return "Recon"; } }
        public IMissionCheck MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public ReconStealthMissionStep()
        {
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            _missionTest = new SquadMissionTest(stealth, 12.5f);
            StepIfSuccess = new PerformReconMissionStep();
            StepIfFailure = new DetectedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            if (context.DaysElapsed >= 6)
            {
                // time to go home
                if (context.Region != context.PlayerSquads.First().CurrentRegion)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(context, 0.0f, this);
                }
            }
            else
            {
                context.DaysElapsed++;
                float margin = _missionTest.RunMissionCheck(context.PlayerSquads);
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
