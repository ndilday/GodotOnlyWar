using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;
using System.Collections.Generic;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Assault
{
    public class PrepareAssaultMissionStep : IMissionStep
    {
        public string Description { get { return "Prepare Assault"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, 10.0f);
            // move the generation of new missions to the turn controller, rather than the individual mission steps
            context.Log.Add($"Day {context.DaysElapsed}: Force prepares to assault {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            int enemySoldiers = context.Order.Mission.RegionFaction.Garrison;
            float cdf = GaussianCalculator.ApproximateNormalCDF(margin);
            float multiplier = (float)Math.Pow(2, 1 - (2 * cdf));
            enemySoldiers = (int)(enemySoldiers * multiplier);
            context.OpposingForces = PopulateOpposingForce(enemySoldiers, context.Order.Mission.RegionFaction);

            new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, null);
        }

        private List<BattleSquad> PopulateOpposingForce(int forceSize, RegionFaction enemyFaction)
        {
            List<BattleSquad> opposingForces = new List<BattleSquad>();
            // determine size of force to generate
            // generate opposing force
            int totalGenerated = 0;
            while (totalGenerated < forceSize)
            {
                Unit enemyUnit = TempArmyBuilder.GenerateArmyFromRegionFaction(enemyFaction);
                opposingForces.AddRange(enemyUnit.GetAllSquads().Select(s => new BattleSquad(false, s)));
                totalGenerated += enemyUnit.GetAllMembers().Count();
            }
            return opposingForces;
        }
    }
}
