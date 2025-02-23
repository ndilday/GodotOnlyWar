﻿using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class EvadeMissionStep : ATestMissionStep
    {
        public override string Description { get { return "Evade"; } }

        public EvadeMissionStep()
        {
        }

        public override void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to escape enemy force");
            // modify by speeds of each side
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            float enemySpeed = context.OpposingForces.Average(s => s.GetSquadMove());
            float playerSpeed = context.PlayerSquads.Average(s => s.GetSquadMove());
            float difficulty = 10f - playerSpeed + enemySpeed + marginOfSuccess;
            SquadMissionTest missionTest = new SquadMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                context.Log.Add($"Day {context.DaysElapsed}: Force successfully escaped enemy force");
                returnStep.ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                // attempt failed
                context.Log.Add($"Day {context.DaysElapsed}: Escape failed");
                new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, returnStep);
            }

        }
    }
}
