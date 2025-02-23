using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    public class ExfiltrateMissionStep : ATestMissionStep
    {

        public override string Description { get { return "Infiltrate"; } }

        public ExfiltrateMissionStep()
        {
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            // negative mod for size of player force
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            _missionTest = new SquadMissionTest(stealth, 10.0f);
        }

        public override void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            if (context.PlayerSquads.SelectMany(s => s.AbleSoldiers).Count() == 0)
            {
                context.Log.Add($"Day {context.DaysElapsed}: Contact lost with mission force, assumed dead.");
                return;
            }
            context.DaysElapsed++;
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to exfiltrate from {context.Region.Name}");
            float margin = _missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                context.Log.Add("Day {context.DaysElapsed}: Force has returned to base.");
                return;
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
