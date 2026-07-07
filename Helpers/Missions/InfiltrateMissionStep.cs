using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
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
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.Skills.Stealth;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float detection = enemyFaction.Detection;
            // every degree of magnitude of troops adds one to the difficulty
            float ownTroopMod = (float)Math.Log(context.MissionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // every degree of magnitude of enemy troops garrisoning the region adds to the difficulty
            float garrisonMod = (float)Math.Log(enemyFaction.Garrison, 10);
            // intelligence makes it easier to find a stealthy route
            float intelMod = context.Order.Mission.RegionFaction.Region.IntelligenceLevel;
            // a standing patrol actively hunting intruders makes the region far harder to slip into
            float patrolMod = enemyFaction.GetPatrolStealthPenalty();
            float difficulty = detection + ownTroopMod + garrisonMod - intelMod + patrolMod;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            if (!ShouldContinue(context))
            {
                return;
            }
            context.DaysElapsed++;
            context.Log.Add($"Day {context.DaysElapsed}: Force attempting to infiltrate into {context.Order.Mission.RegionFaction.Region.Name}");
            // modifiers should include: size of enemy forces, size of player force, terrain, some notion of enemy focus (hunting, defending, hiding), whether enemy is hidden or public
            float bestStealth = context.MissionSquads
                .SelectMany(s => s.AbleSoldiers)
                .Select(sol => sol.Soldier.GetTotalSkillValue(stealth))
                .DefaultIfEmpty(0f)
                .Max();
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            RegionFaction infTarget = context.Order.Mission.RegionFaction;
            GameLog.Trace(() =>
                $"Infiltrate {context.MissionSquads.FirstOrDefault()?.Squad.Faction?.Name ?? "?"} -> "
                + $"{infTarget.Region.Planet.Name}/{infTarget.Region.Name}/{infTarget.PlanetFaction.Faction.Name} "
                + $"day {context.DaysElapsed}: difficulty={difficulty:F2} (detection={detection:F0}, "
                + $"+ownTroops={ownTroopMod:F2}, +garrison={garrisonMod:F2}, -intel={intelMod:F2}, +patrol={patrolMod:F2}), "
                + $"bestStealthSkill={bestStealth:F2}, margin={margin:F2} -> {(margin > 0 ? "INFILTRATED" : "DETECTED")}");
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
            else if (context.MissionSquads.Where(s => s.ShouldContinueMission()).Count() == 0)
            {
                context.Log.Add("Mission aborted: too many casualties");
                return false;
            }
            return true;
        }
    }
}
