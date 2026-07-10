using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Recon
{
    public class ReconStealthMissionStep : IMissionStep
    {
        public string Description { get { return "Recon Stealth"; } }

        public ReconStealthMissionStep(){}

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // The mission runs for at most a week. This cap is the safety net for the detect->evade
            // loop: the graceful day check lives in PerformReconMissionStep and only fires on a
            // *successful* infiltration, so a scout that keeps failing stealth would otherwise loop
            // indefinitely (see MissionContext.MissionDurationDays). Once the week is spent, break
            // contact — exfiltrate if we infiltrated the target region, otherwise the sortie is over.
            if (context.DaysElapsed >= MissionContext.MissionDurationDays)
            {
                GameLog.Trace(() =>
                    $"Recon stealth {DescribeFaction(context)} -> {DescribeTarget(context)}: "
                    + $"week elapsed at day {context.DaysElapsed}; breaking contact");
                if (context.Order.Mission.RegionFaction.Region != context.MissionSquads.First().Squad.CurrentRegion)
                {
                    new ExfiltrateMissionStep().ExecuteMissionStep(context, 0.0f, null);
                }
                return;
            }

            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.Skills.Stealth;
            Region region = context.Order.Mission.RegionFaction.Region;
            // the scout's own knowledge of the region makes it easier to find a stealthy route
            Faction scout = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            int scoutHeadcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Detection aggregates across every enemy faction in the region (one stealth check per
            // day, not N independent rolls); the terms are broken out for the trace.
            float difficulty = CalculateStealthDifficulty(region, scoutHeadcount, scout,
                out float detection, out float ownTroopMod, out float garrisonMod, out float intelMod,
                out int enemyCount);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            // The best (highest-skill) able scout's stealth value, so the log shows the gap between
            // the skill the check is rolled on and the difficulty it faces.
            float bestStealth = context.MissionSquads
                .SelectMany(s => s.AbleSoldiers)
                .Select(sol => sol.Soldier.GetTotalSkillValue(stealth))
                .DefaultIfEmpty(0f)
                .Max();
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            bool slippedIn = margin > 0.0f;
            // On detection, resolve which of the region's enemy factions spotted the scout now, so the
            // trace can name it and DetectedMissionStep raises the interceptor from that faction (which
            // need not be the mission's anchor RegionFaction) rather than the target.
            if (!slippedIn)
            {
                context.Spotter = region.SelectSpotter();
            }
            GameLog.Trace(() =>
                $"Recon stealth {DescribeFaction(context)} -> {DescribeTarget(context)} day {context.DaysElapsed}: "
                + $"difficulty={difficulty:F2} (detection={detection:F2} over {enemyCount} enemy faction(s), "
                + $"+ownTroops={ownTroopMod:F2}, +troops={garrisonMod:F2}, -intel={intelMod:F2}), "
                + $"bestStealthSkill={bestStealth:F2}, margin={margin:F2} -> "
                + $"{(slippedIn ? "SLIPPED IN" : $"DETECTED by {DescribeSpotter(context.Spotter)}")}");
            if (slippedIn)
            {
                new PerformReconMissionStep().ExecuteMissionStep(context, margin, this);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
            }
        }

        // Aggregated daily stealth difficulty for a recon in a (possibly multi-faction) region,
        // exposed as a static so the detection model can be unit-tested without a full mission run.
        // Both the awareness and troop terms sum over the same enemy set the spotter is later drawn
        // from (Design/MultiFactionRegions.md WI-3). The Max(1, ...) guard is mandatory: deployed
        // strength (the horde-correct troop count) can be zero — a PopulationIsMilitary horde carries
        // no Garrison — and Log(0) is -infinity, which would make a zero-garrison region trivially
        // infiltrable. The per-term out values feed the trace line.
        public static float CalculateStealthDifficulty(Region region, int scoutHeadcount, Faction scout,
            out float detection, out float ownTroopMod, out float garrisonMod, out float intelMod,
            out int enemyCount)
        {
            List<RegionFaction> enemies = region.GetDetectingEnemyFactions();
            enemyCount = enemies.Count;
            // The defenders' combined awareness of their own ground (unified intel; a patrol sweeping
            // the region raises this directly, so a patrolled region is intrinsically harder to scout).
            detection = enemies.Sum(rf => rf.GetOwnRegionIntel()) * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            ownTroopMod = (float)Math.Log(scoutHeadcount, 10);
            // every degree of magnitude of enemy troops fielded in the region adds to the difficulty
            garrisonMod = (float)Math.Log(Math.Max(1L, enemies.Sum(rf => rf.GetDeployedStrength())), 10);
            intelMod = scout == null ? 0f : region.GetFactionRegionIntel(scout);
            return detection + ownTroopMod + garrisonMod - intelMod;
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
