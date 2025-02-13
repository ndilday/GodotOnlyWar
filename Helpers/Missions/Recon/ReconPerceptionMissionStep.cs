using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class ReconPerceptionMissionStep : ITestMissionStep
    {
        private readonly IMissionTest _missionTest;

        public string Description { get { return "Recon"; } }
        public IMissionTest MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public ReconPerceptionMissionStep()
        {
            BaseSkill perception = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            _missionTest = new SquadMissionTest(perception, 10.0f);
            StepIfSuccess = new ReconStealthMissionStep();
            StepIfFailure = new ReconStealthMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            float margin = _missionTest.RunMissionTest(context.PlayerSquads);
            if (margin > 0.0f)
            {
                // TODO: add some kind of recon data to the context
                StepIfSuccess.ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                StepIfFailure.ExecuteMissionStep(context, margin, returnStep);
            }
        }
    }
}
