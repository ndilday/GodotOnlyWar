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

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            // The defender's awareness of its own ground (unified intel; a patrol sweeping the region
            // raises this directly, so a patrolled region is intrinsically harder to slip into).
            float detection = enemyFaction.GetOwnRegionIntel() * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            float ownTroopMod = (float)Math.Log(context.MissionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // every degree of magnitude of enemy troops garrisoning the region adds to the difficulty
            float garrisonMod = (float)Math.Log(enemyFaction.Garrison, 10);
            // the infiltrator's own knowledge of the region makes it easier to find a stealthy route
            Faction infiltrator = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            float intelMod = infiltrator == null ? 0f : enemyFaction.Region.GetFactionRegionIntel(infiltrator);
            float difficulty = detection + ownTroopMod + garrisonMod - intelMod;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            if (!ShouldContinue(context))
            {
                return;
            }
            context.DaysElapsed++;
            // modifiers should include: size of enemy forces, size of player force, terrain, some notion of enemy focus (hunting, defending, hiding), whether enemy is hidden or public
            float bestStealth = context.MissionSquads
                .SelectMany(s => s.AbleSoldiers)
                .Select(sol => sol.Soldier.GetTotalSkillValue(stealth))
                .DefaultIfEmpty(0f)
                .Max();
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            RegionFaction infTarget = context.Order.Mission.RegionFaction;
            GameLog.Trace(() =>
                $"Infiltrate {context.MissionSquads.FirstOrDefault()?.Squad.Faction?.Name ?? "?"} -> "
                + $"{infTarget.Region.Planet.Name}/{infTarget.Region.Name}/{infTarget.PlanetFaction.Faction.Name} "
                + $"day {context.DaysElapsed}: difficulty={difficulty:F2} (detection={detection:F2}, "
                + $"+ownTroops={ownTroopMod:F2}, +garrison={garrisonMod:F2}, -intel={intelMod:F2}), "
                + $"bestStealthSkill={bestStealth:F2}, margin={margin:F2} -> {(margin > 0 ? "INFILTRATED" : "DETECTED")}");
            if (margin > 0.0f)
            {
                context.AddLog(
                    $"Day {context.DaysElapsed}: Force succeeded in infiltrating "
                    + $"{context.Order.Mission.RegionFaction.Region.Name} undetected.");
                MissionStepOrchestrator.GetMainInitialStep(execution)
                    .ExecuteMissionStep(execution, margin, returnStep);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(execution, margin, this);
            }
        }

        public bool ShouldContinue(MissionContext context)
        {
            if (context.DaysElapsed >= 6)
            {
                context.ObjectiveAborted = true;
                context.AddLog("Mission failed: Force unable to infiltrate into region");
                return false;
            }
            else if (context.MissionSquads.Where(s => s.ShouldContinueMission()).Count() == 0)
            {
                context.ObjectiveAborted = true;
                context.AddLog("Mission aborted: too many casualties");
                return false;
            }
            return true;
        }
    }
}
