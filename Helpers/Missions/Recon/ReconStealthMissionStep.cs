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

        public bool ConsumesDay => true;

        public ReconStealthMissionStep(){}

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
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
                return context.MustExfiltrate
                    ? MissionStepResult.Continue(new ExfiltrateMissionStep())
                    : MissionStepResult.Complete;
            }

            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            Region region = context.Order.Mission.RegionFaction.Region;
            Faction scout = context.MissionSquads.FirstOrDefault()?.Faction;
            int scoutHeadcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Detection aggregates across every enemy faction in the region (one stealth check per
            // day, not N independent rolls); the terms are broken out for the trace.
            StealthDifficultyTerms terms =
                MissionStealthDifficulty.Calculate(region, scoutHeadcount, scout);
            // Aggression trades exposure for effect (MissionAggressionModifiers): a cautious sweep
            // keeps its distance and is harder to spot, a bold one presses close and is seen. The
            // other half of the trade is the intelligence check in PerformReconMissionStep, which
            // moves the opposite way - neither setting is strictly better than the other.
            float aggressionMod =
                MissionAggressionModifiers.ExposureDifficulty(context.Order.LevelOfAggression);
            float difficulty = terms.Total + aggressionMod;
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
                + $"-intel={terms.IntelMod:F2}, "
                + $"+aggression={aggressionMod:F2} [{context.Order.LevelOfAggression}]), "
                + $"bestStealthSkill={bestStealth:F2}, margin={margin:F2} -> "
                + $"{(slippedIn ? "SLIPPED IN" : $"DETECTED by {DescribeSpotter(context.Spotter)}")}");
            return slippedIn
                ? MissionStepResult.Continue(new PerformReconMissionStep(), margin, this)
                : MissionStepResult.Continue(new DetectedMissionStep(), margin, this);
        }

        private static string DescribeFaction(MissionContext context) =>
            context.MissionSquads.FirstOrDefault()?.Faction?.Name ?? "Unknown";

        private static string DescribeTarget(MissionContext context)
        {
            RegionFaction target = context.Order.Mission.RegionFaction;
            return $"{target.Region.Planet.Name}/{target.Region.Name}/{target.PlanetFaction.Faction.Name}";
        }

        private static string DescribeSpotter(RegionFaction spotter) =>
            spotter?.PlanetFaction.Faction.Name ?? "no one (uncontested)";
    }
}
