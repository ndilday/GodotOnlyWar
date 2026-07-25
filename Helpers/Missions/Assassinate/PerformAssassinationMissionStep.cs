using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;

namespace OnlyWar.Helpers.Missions.Assassinate
{
    public class PerformAssassinationMissionStep : IMissionStep
    {
        public string Description => "Assassination Mission";

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            BaseSkill tactics = execution.Rules.Tactics;
            // size 1: Prime
            // size 2: Broodlord
            // size 3: Hive Tyrant
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            // Like PerformSabotageMissionStep this stays anchored to the mission's target faction:
            // it is a check against the target's own protection, and region-wide presence was already
            // priced in by the stealth step that got the force here. It reads deployed strength
            // rather than raw Garrison so a PopulationIsMilitary horde (Tyranids, cults) whose army
            // is its Population puts a real screen around its HQ, instead of the Log10(0) =
            // -infinity that made every assassination attempt against one succeed for free.
            float difficulty = (float)((enemyFaction.Entrenchment + enemyFaction.GetOwnRegionIntel()) * 0.5)
                + MissionStealthDifficulty.TroopMagnitude(enemyFaction.GetDeployedStrength());
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            
            // TODO: my current data design doesn't handle HQ+Bodyguard in a single squad very well, so for now, I should come up with a way to associate each HQ with a particular separate bodyguard squad
            var request = new ForceGenerationRequest
            {
                Faction = context.Order.Mission.RegionFaction.PlanetFaction.Faction,
                TargetBattleValue = (int)margin,
                Profile = ForceCompositionProfile.SpecialHQTarget,
                Tier = context.Order.Mission.MissionSize
            };
            context.OpposingSquads = ForceGenerator.GenerateForce(
                    request,
                    execution.Random,
                    execution.EntityIds)
                .Select(s => new BattleSquad(false, s))
                .ToList();

            BattleSquad targetSquad = context.OpposingSquads.FirstOrDefault();
            context.AssassinationTargetSoldierId = targetSquad?.SquadLeader?.Soldier.Id
                ?? targetSquad?.AbleSoldiers.FirstOrDefault()?.Soldier.Id;

            context.TargetLocated = true;
            context.AddLog($"Day {context.DaysElapsed}: Force has located the assassination target");

            // Fight the generated HQ encounter itself. Routing back through recon stealth here used
            // to let DetectedMissionStep replace OpposingSquads with an interceptor patrol, meaning
            // the located target never entered battle and bodyguard/interceptor kills could be
            // mistaken for the objective.
            new MeetingEngagementMissionStep().ExecuteMissionStep(
                execution,
                margin,
                returnStep: null);

            if (!context.MissionSquads.Any(s => s.ShouldContinueMission()))
            {
                return;
            }

            if (context.Order.Mission.RegionFaction.Region != context.MissionSquads.First().Squad.CurrentRegion)
            {
                new ExfiltrateMissionStep().ExecuteMissionStep(execution, 0.0f, this);
            }
        }
    }
}
