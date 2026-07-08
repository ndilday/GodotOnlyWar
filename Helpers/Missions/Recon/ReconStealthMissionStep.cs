using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
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
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            // The defender's awareness of its own ground (unified intel; a patrol sweeping the region
            // raises this directly, so a patrolled region is intrinsically harder to scout unseen).
            float detection = enemyFaction.GetOwnRegionIntel() * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            float ownTroopMod = (float)Math.Log(context.MissionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // every degree of magnitude of enemy troops garrisoning the region adds to the difficulty
            float garrisonMod = (float)Math.Log(enemyFaction.Garrison, 10);
            // the scout's own knowledge of the region makes it easier to find a stealthy route
            Faction scout = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            float intelMod = scout == null ? 0f : enemyFaction.Region.GetFactionRegionIntel(scout);
            float difficulty = detection + ownTroopMod + garrisonMod - intelMod;
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
            GameLog.Trace(() =>
                $"Recon stealth {DescribeFaction(context)} -> {DescribeTarget(context)} day {context.DaysElapsed}: "
                + $"difficulty={difficulty:F2} (detection={detection:F2}, +ownTroops={ownTroopMod:F2}, "
                + $"+garrison={garrisonMod:F2}, -intel={intelMod:F2}), "
                + $"bestStealthSkill={bestStealth:F2}, margin={margin:F2} -> {(margin > 0 ? "SLIPPED IN" : "DETECTED")}");
            if (margin > 0.0f)
            {
                new PerformReconMissionStep().ExecuteMissionStep(context, margin, this);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(context, margin, this);
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
