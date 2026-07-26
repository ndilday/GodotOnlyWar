using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Sabotage
{
    public class SabotageStealthMissionStep : IMissionStep
    {
        public string Description { get { return "Sabotage Stealth"; } }

        public SabotageStealthMissionStep() { }

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            // The mission runs for at most a week, and a force that infiltrated owes a day to get back
            // out, so it stops operating after day 6 (see MissionContext.OperatingDaysSpent). Mirrors
            // ReconStealthMissionStep, and this step is where the cap has to live: DetectedMissionStep
            // is handed `this` as its returnStep, so an intercepted force that survives its engagement
            // re-enters the loop here from AmbushedMissionStep / MeetingEngagementMissionStep. With no
            // check at all, a force that kept being detected but kept surviving incremented DaysElapsed
            // and rolled stealth again with no upper bound, running until a stealth check finally
            // succeeded - well past the one-week strategic turn.
            if (context.OperatingDaysSpent)
            {
                GameLog.Trace(() =>
                    $"Sabotage stealth {DescribeFaction(context)} -> {DescribeTarget(context)}: "
                    + $"operating days spent at day {context.DaysElapsed}; breaking contact");
                if (context.MustExfiltrate)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(execution, 0.0f, null);
                }
                return;
            }

            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            Region region = context.Order.Mission.RegionFaction.Region;
            Faction saboteur = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Being seen is a property of the region, not of the faction whose installations are the
            // target: the same aggregated search-effort model as ReconStealthMissionStep, so a
            // saboteur is priced against what the region's occupants are actually doing to find him
            // (surveillance and patrols, plus a capped allowance for sheer density) rather than
            // against a raw Garrison that is permanently zero for a horde.
            float difficulty = MissionStealthDifficulty
                .Calculate(region, headcount, saboteur).Total;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            if (margin > 0.0f)
            {
                new PerformSabotageMissionStep().ExecuteMissionStep(execution, margin, this);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(execution, margin, this);
            }
        }

        private static string DescribeFaction(MissionContext context) =>
            context.MissionSquads.FirstOrDefault()?.Squad.Faction?.Name ?? "Unknown";

        private static string DescribeTarget(MissionContext context)
        {
            RegionFaction target = context.Order.Mission.RegionFaction;
            return $"{target.Region.Planet.Name}/{target.Region.Name}/{target.PlanetFaction.Faction.Name}";
        }
    }
}
