using OnlyWar.Operations.Abstractions;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Operations.Missions.Recon;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Linq;

namespace OnlyWar.Operations.Missions
{
    public class ExfiltrateMissionStep : IMissionStep
    {

        public string Description { get { return "Exfiltrate"; } }

        public bool ConsumesDay => true;

        public ExfiltrateMissionStep(){}

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            Region region = context.Order.Mission.RegionFaction.Region;
            Faction force = context.MissionSquads.FirstOrDefault()?.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleMembers.Count);
            // Slipping back out is contested by every enemy watching the region, the same aggregated
            // model as the way in (ReconStealthMissionStep / InfiltrateMissionStep).
            float difficulty = MissionStealthDifficulty
                .Calculate(region, headcount, force).Total;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            if (context.MissionSquads.SelectMany(s => s.AbleMembers).Count() == 0)
            {
                MarkForceLostBehindEnemyLines(
                    context, region, execution.Campaign.Date, execution.Personnel);
                context.AddLog($"Day {context.DaysElapsed}: Contact lost with mission force, assumed dead.");
                return MissionStepResult.Complete;
            }
            // Bound the detect->exfil->detect loop: a force that cannot slip back out within the week
            // plus a short grace has gone to ground behind enemy lines; end the mission rather than
            // spinning DaysElapsed indefinitely (see MissionContext.MissionDurationDays).
            if (context.DaysElapsed >= MissionContext.MissionDurationDays + MissionContext.ExfiltrationGraceDays)
            {
                DeployForceInTargetRegion(
                    context, region, execution.Campaign.Date, execution.Personnel);
                context.ForceRemainedInTargetRegion = true;
                context.AddLog(
                    $"Day {context.DaysElapsed}: Force could not exfiltrate and remains deployed in {region.Name}.");
                GameLog.Trace(() =>
                    $"Exfiltrate {context.Order.Mission.RegionFaction.Region.Planet.Name}/"
                    + $"{context.Order.Mission.RegionFaction.Region.Name} day {context.DaysElapsed}: "
                    + "grace expired; mission ends with force deployed in target region");
                return MissionStepResult.Complete;
            }
            context.DaysElapsed++;
            context.AddLog($"Day {context.DaysElapsed}: Force attempting to exfiltrate from {context.Order.Mission.RegionFaction.Region.Name}");
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            if (margin > 0.0f)
            {
                context.ForceReturnedToBase = true;
                context.AddLog($"Day {context.DaysElapsed}: Force has returned to base.");
                GameLog.Trace(() =>
                    $"Exfiltrate {context.Order.Mission.RegionFaction.Region.Planet.Name}/"
                    + $"{context.Order.Mission.RegionFaction.Region.Name} day {context.DaysElapsed}: "
                    + $"margin={margin:F2} -> returned to base");
                return MissionStepResult.Complete;
            }
            return MissionStepResult.Continue(new DetectedMissionStep(), margin, this);
        }

        private static void MarkForceLostBehindEnemyLines(
            MissionContext context,
            Region region,
            Date currentDate,
            IPhysicalPostingCommands personnel)
        {
            context.ForceLostContact = true;
            MissionForceRelocation.MoveForce(
                context,
                region,
                registerAsLanded: false,
                currentDate: currentDate,
                personnel: personnel);
        }

        private static void DeployForceInTargetRegion(
            MissionContext context,
            Region region,
            Date currentDate,
            IPhysicalPostingCommands personnel) =>
            MissionForceRelocation.MoveForce(
                context,
                region,
                registerAsLanded: true,
                currentDate: currentDate,
                personnel: personnel);
    }
}
