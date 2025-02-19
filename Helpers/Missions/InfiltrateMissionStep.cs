using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class InfiltrateMissionStep : ITestMissionStep
    {
        private readonly IMissionCheck _missionTest;

        public string Description { get { return "Infiltrate"; } }
        public IMissionCheck MissionTest { get; }
        public IMissionStep StepIfSuccess { get; }
        public IMissionStep StepIfFailure { get; }

        public InfiltrateMissionStep()
        {
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            _missionTest = new SquadMissionTest(stealth, 12.5f);
            StepIfFailure = new DetectedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            if (!ShouldContinue(context)) return;
            context.DaysElapsed++;
            // modifiers should include: size of enemy forces, size of player force, terrain, some notion of enemy focus (hunting, defending, hiding), whether enemy is hidden or public
            float margin = _missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                MissionStepOrchestrator.GetMainInitialStep(context).ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                StepIfFailure.ExecuteMissionStep(context, margin, this);
            }
        }

        public bool ShouldContinue(MissionContext context)
        {
            if (context.DaysElapsed >= 6 || context.PlayerSquads.SelectMany(s => s.Members).All(s => s.MoveSpeed == 0)) return false;
            return true;
        }
    }
}
