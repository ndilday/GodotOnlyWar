using OnlyWar.Builders;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Units;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Extensions;
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
            float difficulty = enemyFaction.GetOwnRegionIntel() * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(context.MissionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // every degree of magnitude of enemy troops garrisoning the region adds to the difficulty
            difficulty += (float)Math.Log(enemyFaction.Garrison, 10);
            // the attacker's own knowledge of the region makes it easier to find a good ambush spot
            Faction attacker = context.MissionSquads.FirstOrDefault()?.Squad.Faction;
            if (attacker != null) difficulty -= enemyFaction.Region.GetFactionRegionIntel(attacker);
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
