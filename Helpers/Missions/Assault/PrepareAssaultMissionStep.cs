using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
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
            var request = new ForceGenerationRequest
            {
                Faction = context.Order.Mission.RegionFaction.PlanetFaction.Faction,
                TargetBattleValue = enemySoldiers * 10, // for now, assume about 10 BV per soldier
                Profile = ForceCompositionProfile.AssaultForce
            };
            context.OpposingForces = ForceGenerator.GenerateForce(request).Select(s => new BattleSquad(false, s)).ToList();

            new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, null);
        }
    }
}
