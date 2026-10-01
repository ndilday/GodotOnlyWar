using OnlyWar.Runtime.Factories;
using OnlyWar.Domain.Extensions;
using OnlyWar.Operations.Extensions;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using System.Linq;

namespace OnlyWar.Operations.Missions
{
    public class InfiltrateMissionStep : IMissionStep
    {
        public string Description { get { return "Infiltrate"; } }

        public bool ConsumesDay => true;

        public InfiltrateMissionStep(){ }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            Region region = context.Order.Mission.RegionFaction.Region;
            Faction infiltrator = context.MissionSquads.FirstOrDefault()?.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleMembers.Count);
            // Slipping in is contested by everyone watching the ground, not just the faction the
            // mission is aimed at, so this uses the same aggregated model as ReconStealthMissionStep.
            StealthDifficultyTerms terms =
                MissionStealthDifficulty.Calculate(region, headcount, infiltrator);
            float difficulty = terms.Total;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            if (!ShouldContinue(context))
            {
                return MissionStepResult.Complete;
            }
            context.DaysElapsed++;
            // modifiers should include: size of enemy forces, size of player force, terrain, some notion of enemy focus (hunting, defending, hiding), whether enemy is hidden or public
            float bestStealth = context.MissionSquads
                .SelectMany(s => s.AbleMembers)
                .Select(sol => sol.GetTotalSkillValue(stealth))
                .DefaultIfEmpty(0f)
                .Max();
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            RegionFaction infTarget = context.Order.Mission.RegionFaction;
            GameLog.Trace(() =>
                $"Infiltrate {context.MissionSquads.FirstOrDefault()?.Faction?.Name ?? "?"} -> "
                + $"{infTarget.Region.Planet.Name}/{infTarget.Region.Name}/{infTarget.PlanetFaction.Faction.Name} "
                + $"day {context.DaysElapsed}: difficulty={difficulty:F2} (detection={terms.Detection:F2} "
                + $"over {terms.EnemyCount} enemy faction(s), +patrol={terms.PatrolMod:F2}, "
                + $"+ambient={terms.AmbientMod:F2}, +ownTroops={terms.OwnTroopMod:F2}, "
                + $"-intel={terms.IntelMod:F2}), "
                + $"bestStealthSkill={bestStealth:F2}, margin={margin:F2} -> {(margin > 0 ? "INFILTRATED" : "DETECTED")}");
            bool detected = margin <= 0.0f;
            // The faction that caught the force is drawn from the factions hostile to it whose watch
            // made the crossing hard (ReconStealthMissionStep does the same). Without it
            // DetectedMissionStep fell back to the mission's anchor faction, which for a Move is the
            // Chapter's own presence in the region - so the Chapter's own patrols there would have
            // intercepted the squad moving in. A failed check with no hostile watcher present has
            // nobody to be seen by, so it succeeds.
            if (detected)
            {
                context.Spotter = region.SelectSpotter(infiltrator, execution.Random);
                detected = context.Spotter != null;
            }
            if (!detected)
            {
                context.ForceEnteredTargetRegion = true;
                context.AddLog(
                    $"Day {context.DaysElapsed}: Force succeeded in infiltrating "
                    + $"{context.Order.Mission.RegionFaction.Region.Name} undetected.");
                return MissionStepResult.Continue(
                    MissionStepOrchestrator.GetMainInitialStep(execution), margin, resumeStep);
            }
            return MissionStepResult.Continue(new DetectedMissionStep(), margin, this);
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
