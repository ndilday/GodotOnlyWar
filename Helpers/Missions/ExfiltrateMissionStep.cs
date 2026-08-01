using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Linq;

namespace OnlyWar.Helpers.Missions
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
            Faction force = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Slipping back out is contested by every enemy watching the region, the same aggregated
            // model as the way in (ReconStealthMissionStep / InfiltrateMissionStep).
            float difficulty = MissionStealthDifficulty
                .Calculate(region, headcount, force).Total;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            if (context.MissionSquads.SelectMany(s => s.AbleSoldiers).Count() == 0)
            {
                MarkForceLostBehindEnemyLines(context, region);
                context.AddLog($"Day {context.DaysElapsed}: Contact lost with mission force, assumed dead.");
                return MissionStepResult.Complete;
            }
            // Bound the detect->exfil->detect loop: a force that cannot slip back out within the week
            // plus a short grace has gone to ground behind enemy lines; end the mission rather than
            // spinning DaysElapsed indefinitely (see MissionContext.MissionDurationDays).
            if (context.DaysElapsed >= MissionContext.MissionDurationDays + MissionContext.ExfiltrationGraceDays)
            {
                DeployForceInTargetRegion(context, region);
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

        private static void MarkForceLostBehindEnemyLines(MissionContext context, Region region)
        {
            context.ForceLostContact = true;
            MoveForce(context, region, registerAsLanded: false);
        }

        private static void DeployForceInTargetRegion(MissionContext context, Region region) =>
            MoveForce(context, region, registerAsLanded: true);

        private static void MoveForce(
            MissionContext context,
            Region region,
            bool registerAsLanded)
        {
            foreach (BattleSquad missionSquad in context.MissionSquads)
            {
                if (missionSquad?.Squad != null)
                {
                    Squad squad = missionSquad.Squad;
                    Region previousRegion = squad.CurrentRegion;
                    if (previousRegion != null
                        && squad.Faction != null
                        && previousRegion.RegionFactionMap.TryGetValue(
                            squad.Faction.Id, out RegionFaction previousPresence))
                    {
                        previousPresence.LandedSquads.Remove(squad);
                    }
                    squad.CurrentRegion = region;

                    if (!registerAsLanded || squad.Faction == null || region?.Planet == null)
                    {
                        continue;
                    }

                    if (!region.Planet.PlanetFactionMap.TryGetValue(
                        squad.Faction.Id, out PlanetFaction planetPresence))
                    {
                        planetPresence = new PlanetFaction(squad.Faction) { IsPublic = true };
                        region.Planet.PlanetFactionMap[squad.Faction.Id] = planetPresence;
                    }
                    if (!region.RegionFactionMap.TryGetValue(
                        squad.Faction.Id, out RegionFaction regionalPresence))
                    {
                        regionalPresence = new RegionFaction(planetPresence, region) { IsPublic = true };
                        region.RegionFactionMap[squad.Faction.Id] = regionalPresence;
                    }
                    else
                    {
                        regionalPresence.IsPublic = true;
                    }
                    if (!regionalPresence.LandedSquads.Contains(squad))
                    {
                        regionalPresence.LandedSquads.Add(squad);
                    }
                }
            }
        }
    }
}
