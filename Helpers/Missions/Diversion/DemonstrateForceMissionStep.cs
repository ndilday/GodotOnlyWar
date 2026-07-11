using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;

namespace OnlyWar.Helpers.Missions.Diversion
{
    // A diversion is deliberately overt: the force makes a show of strength to draw enemy
    // attention rather than infiltrating. Each day it runs a Tactics-based feint check; positive
    // margins accumulate into Impact, which is later converted (superlinearly, capped by
    // MissionSize) into a perceived-threat/provocation effect on the enemy's planning.
    public class DemonstrateForceMissionStep : IMissionStep
    {
        public string Description => "Demonstrate Force";

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.Skills.Tactics;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            // The harder the enemy is to bluff (better detection, larger garrison able to
            // appraise the threat), the harder it is to project a convincing feint.
            float difficulty = enemyFaction.GetOwnRegionIntel() * 0.5f;
            difficulty += (float)Math.Log10(Math.Max(enemyFaction.Garrison, 1));
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);

            context.DaysElapsed++;
            context.AddLog($"Day {context.DaysElapsed}: Force makes a show of strength against {enemyFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            if (margin > 0)
            {
                context.Impact += margin;
            }

            // The feint force stays in the open for the whole turn; it does not infiltrate or
            // exfiltrate, so just keep demonstrating until the campaign week is spent.
            if (context.DaysElapsed < 7)
            {
                ExecuteMissionStep(context, marginOfSuccess, returnStep);
            }
        }
    }
}
