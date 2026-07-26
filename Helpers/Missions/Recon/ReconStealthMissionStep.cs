using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class ReconStealthMissionStep : IMissionStep
    {
        public string Description { get { return "Recon Stealth"; } }

        public ReconStealthMissionStep(){}

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            // The mission runs for at most a week, and a force that infiltrated owes a day to get back
            // out, so it stops scouting after day 6 (see MissionContext.OperatingDaysSpent). This is
            // also the safety net for the detect->engage->continue loop: a scout that keeps failing
            // stealth, or that is intercepted and stays on mission, re-enters here rather than through
            // PerformReconMissionStep, and would otherwise scout past its budget - or loop
            // indefinitely. Once the days are spent, break contact.
            if (context.OperatingDaysSpent)
            {
                GameLog.Trace(() =>
                    $"Recon stealth {DescribeFaction(context)} -> {DescribeTarget(context)}: "
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
            Faction scout = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            int scoutHeadcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Detection aggregates across every enemy faction in the region (one stealth check per
            // day, not N independent rolls); the terms are broken out for the trace.
            StealthDifficultyTerms terms =
                MissionStealthDifficulty.Calculate(region, scoutHeadcount, scout);
            float difficulty = terms.Total;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            // The best (highest-skill) able scout's stealth value, so the log shows the gap between
            // the skill the check is rolled on and the difficulty it faces.
            float bestStealth = context.MissionSquads
                .SelectMany(s => s.AbleSoldiers)
                .Select(sol => sol.Soldier.GetTotalSkillValue(stealth))
                .DefaultIfEmpty(0f)
                .Max();
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            bool slippedIn = margin > 0.0f;
            // On detection, resolve which of the region's enemy factions spotted the scout now, so the
            // trace can name it and DetectedMissionStep raises the interceptor from that faction (which
            // need not be the mission's anchor RegionFaction) rather than the target.
            if (!slippedIn)
            {
                context.Spotter = region.SelectSpotter(execution.Random);
            }
            GameLog.Trace(() =>
                $"Recon stealth {DescribeFaction(context)} -> {DescribeTarget(context)} day {context.DaysElapsed}: "
                + $"difficulty={difficulty:F2} (detection={terms.Detection:F2} over "
                + $"{terms.EnemyCount} enemy faction(s), +patrol={terms.PatrolMod:F2}, "
                + $"+ambient={terms.AmbientMod:F2}, +ownTroops={terms.OwnTroopMod:F2}, "
                + $"-intel={terms.IntelMod:F2}), "
                + $"bestStealthSkill={bestStealth:F2}, margin={margin:F2} -> "
                + $"{(slippedIn ? "SLIPPED IN" : $"DETECTED by {DescribeSpotter(context.Spotter)}")}");
            if (slippedIn)
            {
                new PerformReconMissionStep().ExecuteMissionStep(execution, margin, this);
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

        private static string DescribeSpotter(RegionFaction spotter) =>
            spotter?.PlanetFaction.Faction.Name ?? "no one (uncontested)";
    }
}
