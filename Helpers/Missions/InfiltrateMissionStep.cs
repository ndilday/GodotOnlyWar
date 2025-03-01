using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Linq;
using System.Net.Mime;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class InfiltrateMissionStep : IMissionStep
    {
        public string Description { get { return "Infiltrate"; } }

        public InfiltrateMissionStep(){ }

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
            if (!ShouldContinue(context))
            {
                return;
            }
            context.DaysElapsed++;
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to infiltrate into {context.Region.Name}");
            // modifiers should include: size of enemy forces, size of player force, terrain, some notion of enemy focus (hunting, defending, hiding), whether enemy is hidden or public
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                MissionStepOrchestrator.GetMainInitialStep(context).ExecuteMissionStep(context, margin, returnStep);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }

        public bool ShouldContinue(MissionContext context)
        {
            if (context.DaysElapsed >= 6)
            {
                context.Log.Add("Mission failed: Force unable to infiltrate into region");
                return false;
            }
            else if (context.PlayerSquads.Where(s => s.ShouldContinueMission()).Count() == 0)
            {
                context.Log.Add("Mission aborted: too many casualties");
                return false;
            }
            return true;
        }
    }
}
