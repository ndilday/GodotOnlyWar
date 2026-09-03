using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Turns;

namespace OnlyWar.Helpers.Missions.Assassinate
{
    public class PerformAssassinationMissionStep : IMissionStep
    {
        public string Description => "Assassination Mission";

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
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
                    + enemyFaction.GetOwnRegionAwareness()) * 0.5)
                + MissionStealthDifficulty.Magnitude(enemyFaction.GetDeployedStrength())
                // Aggression's EFFECT axis: taking the shot is what boldness buys. A force unwilling to
                // expose itself cannot get close enough for a clean kill.
                + MissionAggressionModifiers.EffectDifficulty(context.Order.LevelOfAggression);
            LeaderMissionTest missionTest = new LeaderMissionTest(tactics, difficulty);
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);

            Region targetRegion = enemyFaction.Region;
            StrategicInvasionForce physicalForce = GameDataSingleton.Instance?.Sector?.StrategicInvasionForces
                ?.FirstOrDefault(force => force.IsActive
                    && force.Faction == enemyFaction.PlanetFaction.Faction
                    && force.CurrentRegion == targetRegion);
            bool reachedCommander = physicalForce != null
                && FactionCapabilityCampaignProcessor.StrategicCommanderCanBeReached(
                    physicalForce,
                    targetRegion,
                    margin,
                    execution.Random,
                    GameDataSingleton.Instance?.GameRulesData?.FactionBehaviorRules);

            if (reachedCommander)
            {
                // The strategic commander is a real persistent squad. Putting it directly into the encounter
                // lets the existing battle casualty ledger drive the deterministic tactical death
                // hook after the mission resolves.
                context.OpposingSquads = [new BattleSquad(false, physicalForce.CommandSquad)];
            }
            else
            {
                // If the Warboss is not physically present, assassination still affects the local
                // leader/bodyguard only. It must not manufacture a kill against the persistent
                // invasion-force identity from another region (or from transit).
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
            }

            BattleSquad targetSquad = context.OpposingSquads.FirstOrDefault();
            context.AssassinationTargetSoldierId = targetSquad?.SquadLeader?.Soldier.Id
                ?? targetSquad?.AbleSoldiers.FirstOrDefault()?.Soldier.Id;

            context.TargetLocated = true;
            context.AddLog($"Day {context.DaysElapsed}: Force has located the assassination target");

            // Fight the generated HQ encounter itself. Routing back through recon stealth here used
            // to let DetectedMissionStep replace OpposingSquads with an interceptor patrol, meaning
            // the located target never entered battle and bodyguard/interceptor kills could be
            // mistaken for the objective.
            // The withdrawal runs whatever the engagement's outcome, so it is a mandatory follow-up
            // rather than the engagement's resume target - MeetingEngagementMissionStep declines to
            // resume when the force is spent, which would strand a force that withdrew under fire but
            // could still walk home. WithdrawIfAbleMissionStep carries the two conditions this step
            // used to apply inline (still able to continue, and standing on ground it does not hold).
            return MissionStepResult.Continue(
                new MeetingEngagementMissionStep(
                    defendersMayBurrow: false,
                    attackerBattleRole: BattleRole.AssassinationAttacker),
                margin,
                then: new WithdrawIfAbleMissionStep());
        }
    }
}
