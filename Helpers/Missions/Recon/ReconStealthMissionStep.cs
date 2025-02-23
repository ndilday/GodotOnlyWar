using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class ReconStealthMissionStep : ATestMissionStep
    {
        public override string Description { get { return "Recon"; } }

        public ReconStealthMissionStep()
        {
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            _missionTest = new SquadMissionTest(stealth, 12.5f);
        }

        public override void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            context.DaysElapsed++;
            float margin = _missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                new PerformReconMissionStep().ExecuteMissionStep(context, margin, this);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
