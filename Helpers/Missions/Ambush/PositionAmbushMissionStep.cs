using OnlyWar.Builders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.StrategicCombat;

namespace OnlyWar.Helpers.Missions.Ambush
{
    public class PositionAmbushMissionStep : IMissionStep
    {
        public string Description { get { return "Ambush Stealth"; } }

        public PositionAmbushMissionStep() { }

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
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
                .Calculate(enemyFaction.Region, headcount, attacker).Total;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.OpposingSquads = PopulateOpposingForce(
                context.Order.Mission.MissionSize,
                enemyFaction,
                execution.Random,
                execution.EntityIds);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);

            if (margin > 0.0f)
            {
                new PerformAmbushMissionStep().ExecuteMissionStep(execution, margin, null);
            }
            else
            {
                new MeetingEngagementMissionStep().ExecuteMissionStep(execution, margin, null);
            }
        }

        private static List<BattleSquad> PopulateOpposingForce(
            int missionSize,
            RegionFaction enemyFaction,
            IRNG random,
            IEntityIdAllocator entityIds)
        {
            List<BattleSquad> opposingForces = new List<BattleSquad>();
            // determine size of force to generate
            double log = random.GetLinearDouble() + missionSize;
            int forceSize = (int)Math.Pow(10, log);

            // generate opposing force
            var request = new ForceGenerationRequest
            {
                Faction = enemyFaction.PlanetFaction.Faction,
                // Mission size is still expressed in rough headcount bands; convert it using the
                // compressed PDF baseline so it remains in the same strategic unit scale.
                TargetBattleValue = forceSize * StrategicCombatRules.PdfTrooperBattleValue,
                Profile = ForceCompositionProfile.AmbushForce
            };
            return ForceGenerator.GenerateForce(request, random, entityIds)
                .Select(s => new BattleSquad(false, s))
                .ToList();
        }
    }
}
