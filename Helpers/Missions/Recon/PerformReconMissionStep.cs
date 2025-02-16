﻿using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class PerformReconMissionStep : ITestMissionStep
    {
        private readonly IMissionTest _missionTest;

        public string Description { get { return "Recon"; } }
        public IMissionTest MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public PerformReconMissionStep()
        {
            BaseSkill perception = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            _missionTest = new SquadMissionTest(perception, 12.5f);
            StepIfSuccess = new ReconStealthMissionStep();
            StepIfFailure = new ReconStealthMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            float margin = _missionTest.RunMissionTest(context.PlayerSquads);
            if (margin > 0.0f)
            {
                // TODO: add some kind of recon data to the context
                // do some sort of test to see whether a special mission opportunity is found
                // if not, improve the inteligence level by the margin
                context.Region.IntelligenceLevel += margin / 5;
                StepIfSuccess.ExecuteMissionStep(context, 0, returnStep);
            }
            else
            {
                StepIfFailure.ExecuteMissionStep(context, 0, returnStep);
            }
        }
    }
}
