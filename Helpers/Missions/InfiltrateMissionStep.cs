using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;
using System.Net.Mime;

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
            // negative mod for size of player force
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            _missionTest = new SquadMissionTest(stealth, 12.5f);
            StepIfFailure = new DetectedMissionStep();
        }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            if (!ShouldContinue(context))
            {
                return;
            }
            context.DaysElapsed++;
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to infiltrate into {context.Region.Name}");
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
            if (context.DaysElapsed >= 6)
            {
                context.Log.Add("Mission failed: Force unable to infiltrate into region");
                return false;
            }
            else if (context.PlayerSquads.Where(s => s.ShouldContinueMission()).Count() == 0)
            {
                context.Log.Add("Mission aborted: too many casualties");
                return false;
            }
            return true;
        }
    }
}
