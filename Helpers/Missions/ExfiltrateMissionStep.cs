using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    public class ExfiltrateMissionStep : ITestMissionStep
    {
        private readonly IMissionCheck _missionTest;

        public string Description { get { return "Infiltrate"; } }
        public IMissionCheck MissionTest { get; }
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
            if (context.PlayerSquads.SelectMany(s => s.Members).All(s => s.MoveSpeed == 0.0f))
            {
                context.Log.Add($"Contact lost with mission force, assumed dead.");
                return;
            }
            context.DaysElapsed++;
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to exfiltrate from {context.Region.Name}");
            float margin = _missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                context.Log.Add("Mission Success");
                return;
            }
            else
            {
                StepIfFailure.ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
