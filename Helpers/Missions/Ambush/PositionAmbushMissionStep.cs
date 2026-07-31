using OnlyWar.Builders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Ambush
{
    public class PositionAmbushMissionStep : IMissionStep
    {
        public string Description { get { return "Ambush Stealth"; } }

        public bool ConsumesDay => true;

        public PositionAmbushMissionStep() { }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
        {
            MissionContext context = execution.State;
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = execution.Rules.Stealth;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            Faction attacker = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            int headcount = context.MissionSquads.Sum(s => s.AbleSoldiers.Count);
            // Setting an ambush without being seen first is contested by everyone watching the
            // ground, not just the faction being ambushed, so this uses the same aggregated
            // search-effort model as ReconStealthMissionStep - and with it that model's log10(1 + x)
            // shape, so a region held by a zero-Garrison horde can no longer produce Log(0) =
            // -infinity, which used to guarantee the ambushers got into position however badly they
            // rolled. Patrolled ground is now the thing that spoils an ambush setup, not raw mass.
            float difficulty = MissionStealthDifficulty
                .Calculate(enemyFaction.Region, headcount, attacker).Total
                // Aggression's EXPOSURE axis: a cautious ambush takes its time and is harder to spot.
                //
                // There is deliberately no separate effect-axis CHECK here, for the same reason
                // Assault has none (OnlyWar_TDD.md §6.4): an ambush's
                // objective IS the engagement, so aggression's existing casualty threshold is
                // already its effect axis - press the ambush and you destroy more of the enemy, break
                // off early and you destroy less. This margin additionally decides the range the
                // ambush is sprung at (see PerformAmbushMissionStep / MissionOpeningRange), so a
                // patient setup also buys the fight on the ambusher's own terms.
                + MissionAggressionModifiers.ExposureDifficulty(context.Order.LevelOfAggression);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.OpposingSquads = PopulateOpposingForce(
                context.Order.Mission,
                enemyFaction,
                execution.Random,
                execution.EntityIds);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);

            return margin > 0.0f
                ? MissionStepResult.Continue(new PerformAmbushMissionStep(), margin)
                : MissionStepResult.Continue(new MeetingEngagementMissionStep(), margin);
        }

        private static List<BattleSquad> PopulateOpposingForce(
            Mission mission,
            RegionFaction enemyFaction,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            List<BattleSquad> opposingForces = new List<BattleSquad>();
            long targetBattleValue =
                AmbushMissionSizing.ResolveTargetBattleValue(mission, random);

            // generate opposing force
            var request = new ForceGenerationRequest
            {
                Faction = enemyFaction.PlanetFaction.Faction,
                // Intelligence-discovered ambushes persist this concrete budget when the opportunity
                // is created. Legacy and non-special ambush orders retain the old execution-time roll.
                TargetBattleValue = targetBattleValue,
                Profile = ForceCompositionProfile.AmbushForce
            };
            return ForceGenerator.GenerateForce(request, random, entityIds)
                .Select(s => new BattleSquad(false, s))
                .ToList();
        }
    }
}
