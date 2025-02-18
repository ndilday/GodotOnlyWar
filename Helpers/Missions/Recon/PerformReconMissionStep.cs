using OnlyWar.Models;
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
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            _missionTest = new SquadMissionTest(tactics, 12.5f);
            StepIfSuccess = new ReconStealthMissionStep();
            StepIfFailure = null;
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // move the generation of new missions to the turn controller, rather than the individual mission steps
            float margin = _missionTest.RunMissionTest(context.PlayerSquads);
            context.Region.IntelligenceLevel += margin;
            StepIfSuccess.ExecuteMissionStep(context, margin, this);

        }
    }
}
