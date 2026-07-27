using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
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
            // is its Population puts a real screen around its HQ.
            //
            // This is deliberately NOT on MissionStealthDifficulty's search-effort model, and must not
            // be "unified" with it later. The question here is "how well guarded is this one target",
            // not "who in this region is looking for me": every body around the HQ is part of the
            // screen whether it is sweeping the countryside or standing at a door, so the ambient cap
            // and the patrol/static split would both be wrong. It borrows only Magnitude's log10(1+x)
            // shape, which keeps an empty holding at 0 instead of the Log10(0) = -infinity that made
            // every assassination attempt against a horde succeed for free.
            // Entrenchment is the side's shared position (RegionDefenses): allies holding a region
            // between them fortify one set of works, and the target shelters behind all of it.
            float difficulty = (float)((
                    RegionDefenses.GetShared(enemyFaction, DefenseType.Entrenchment)
                    + enemyFaction.GetOwnRegionIntel()) * 0.5)
                + MissionStealthDifficulty.Magnitude(enemyFaction.GetDeployedStrength());
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
