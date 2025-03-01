using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    public class ExfiltrateMissionStep : IMissionStep
    {

        public string Description { get { return "Infiltrate"; } }

        public ExfiltrateMissionStep(){}

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            RegionFaction enemyFaction = context.Region.RegionFactionMap.Values.First(rf => !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
            float difficulty = enemyFaction.Detection;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(context.PlayerSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // every degree of magnitude of enemy troops garrisoning the region adds to the difficulty
            difficulty += (float)Math.Log(enemyFaction.Garrison, 10);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            if (context.PlayerSquads.SelectMany(s => s.AbleSoldiers).Count() == 0)
            {
                context.Log.Add($"Day {context.DaysElapsed}: Contact lost with mission force, assumed dead.");
                return;
            }
            context.DaysElapsed++;
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to exfiltrate from {context.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                context.Log.Add($"Day {context.DaysElapsed}: Force has returned to base.");
                return;
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
