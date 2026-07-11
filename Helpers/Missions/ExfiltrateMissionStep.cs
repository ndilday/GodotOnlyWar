using OnlyWar.Helpers.Extensions;
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
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.Skills.Stealth;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float difficulty = enemyFaction.GetOwnRegionIntel() * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(context.MissionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // every degree of magnitude of enemy troops garrisoning the region adds to the difficulty
            difficulty += (float)Math.Log(enemyFaction.Garrison, 10);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            if (context.MissionSquads.SelectMany(s => s.AbleSoldiers).Count() == 0)
            {
                context.AddLog($"Day {context.DaysElapsed}: Contact lost with mission force, assumed dead.");
                return;
            }
            // Bound the detect->exfil->detect loop: a force that cannot slip back out within the week
            // plus a short grace has gone to ground behind enemy lines; end the mission rather than
            // spinning DaysElapsed indefinitely (see MissionContext.MissionDurationDays).
            if (context.DaysElapsed >= MissionContext.MissionDurationDays + MissionContext.ExfiltrationGraceDays)
            {
                context.AddLog($"Day {context.DaysElapsed}: Force could not break contact; gone to ground behind enemy lines.");
                GameLog.Trace(() =>
                    $"Exfiltrate {context.Order.Mission.RegionFaction.Region.Planet.Name}/"
                    + $"{context.Order.Mission.RegionFaction.Region.Name} day {context.DaysElapsed}: "
                    + "grace expired; mission ends (force gone to ground)");
                return;
            }
            context.DaysElapsed++;
            context.AddLog($"Day {context.DaysElapsed}: Force attempting to exfiltrate from {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            if (margin > 0.0f)
            {
                context.AddLog($"Day {context.DaysElapsed}: Force has returned to base.");
                GameLog.Trace(() =>
                    $"Exfiltrate {context.Order.Mission.RegionFaction.Region.Planet.Name}/"
                    + $"{context.Order.Mission.RegionFaction.Region.Name} day {context.DaysElapsed}: "
                    + $"margin={margin:F2} -> returned to base");
                return;
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }
    }
}
