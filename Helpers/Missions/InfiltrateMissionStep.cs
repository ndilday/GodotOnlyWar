using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System.Linq;

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
            Region region = context.Order.Mission.RegionFaction.Region;
            Faction infiltrator = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Slipping in is contested by everyone watching the ground, not just the faction the
            // mission is aimed at, so this uses the same aggregated model as ReconStealthMissionStep.
            StealthDifficultyTerms terms =
                MissionStealthDifficulty.Calculate(region, headcount, infiltrator);
            float difficulty = terms.Total;
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
                + $"day {context.DaysElapsed}: difficulty={difficulty:F2} (detection={terms.Detection:F2} "
                + $"over {terms.EnemyCount} enemy faction(s), +ownTroops={terms.OwnTroopMod:F2}, "
                + $"+troops={terms.TroopMod:F2}, -intel={terms.IntelMod:F2}), "
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
