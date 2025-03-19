using OnlyWar.Helpers.Missions.Assassination;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Units;

namespace OnlyWar.Helpers.Missions.Ambush
{
    public class PerformAmbushMissionStep : IMissionStep
    {
        public string Description { get { return "Ambush"; } }

        public PerformAmbushMissionStep() { }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            RegionFaction enemyFaction = context.Order.Mission.Region.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
            float difficulty = enemyFaction.Detection;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(context.PlayerSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // intelligence makes it easier to find a stealthy route
            difficulty -= context.Order.Mission.Region.IntelligenceLevel;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            context.OpposingForces = PopulateOpposingForce(context.Order.Mission.MissionSize, enemyFaction);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                new PerformAssassinationMissionStep().ExecuteMissionStep(context, margin, this);
            }
            else
            {
                new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }

        private List<BattleSquad> PopulateOpposingForce(int missionSize, RegionFaction enemyFaction)
        {
            List<BattleSquad> opposingForces = new List<BattleSquad>();
            // determine size of force to generate
            double log = RNG.GetLinearDouble() + missionSize;
            int forceSize = (int)Math.Pow(10, log);
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
