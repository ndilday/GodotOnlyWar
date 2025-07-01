using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models;
using System;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;

namespace OnlyWar.Helpers.Missions.Assassinate
{
    public class PerformAssassinationMissionStep : IMissionStep
    {
        public string Description => "Assassination Mission";

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            BaseSkill tactics = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Tactics");
            // size 1: Prime
            // size 2: Broodlord
            // size 3: Hive Tyrant
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float difficulty = enemyFaction.Entrenchment + enemyFaction.Detection + (float)Math.Log10(enemyFaction.Garrison);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            
            // TODO: my current data design doesn't handle HQ+Bodyguard in a single squad very well, so for now, I should come up with a way to associate each HQ with a particular separate bodyguard squad
            var request = new ForceGenerationRequest
            {
                Faction = context.Order.Mission.RegionFaction.PlanetFaction.Faction,
                TargetBattleValue = (int)margin,
                Profile = ForceCompositionProfile.SpecialHQTarget,
                Tier = context.Order.Mission.MissionSize
            };
            context.OpposingSquads = ForceGenerator.GenerateForce(request).Select(s => new BattleSquad(false, s)).ToList();

            context.Log.Add($"Day {context.DaysElapsed}: Force has located the assassination target");

            if (context.DaysElapsed >= 6)
            {
                // time to go home
                if (context.Order.Mission.RegionFaction.Region != context.MissionSquads.First().Squad.CurrentRegion)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(context, 0.0f, this);
                }
                else if (context.DaysElapsed >= 7)
                {
                    //we don't have to go anywhere so just exit.
                    return;
                }
            }
            else
            {
                new ReconStealthMissionStep().ExecuteMissionStep(context, marginOfSuccess, this);
            }
        }
    }
}
