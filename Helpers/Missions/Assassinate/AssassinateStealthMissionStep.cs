﻿using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Assassinate
{
    public class AssassinateStealthMissionStep : IMissionStep
    {
        public string Description { get { return "Assassinate Stealth"; } }

        public AssassinateStealthMissionStep() { }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float difficulty = enemyFaction.Detection;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(context.MissionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // every degree of magnitude of enemy troops garrisoning the region adds to the difficulty
            difficulty += (float)Math.Log(enemyFaction.Garrison, 10);
            // intelligence makes it easier to find a good ambush spot
            difficulty -= context.Order.Mission.RegionFaction.Region.IntelligenceLevel;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            if (margin > 0.0f)
            {
                new PerformAssassinationMissionStep().ExecuteMissionStep(context, margin, this);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
