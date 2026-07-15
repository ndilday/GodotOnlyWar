using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Assassinate
{
    public class AssassinateStealthMissionStep : IMissionStep
    {
        public string Description { get { return "Assassinate Stealth"; } }

        public AssassinateStealthMissionStep() { }

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

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads, execution.Random);
            if (margin > 0.0f)
            {
                new PerformAssassinationMissionStep().ExecuteMissionStep(execution, margin, this);
            }
            else
            {
                new DetectedMissionStep().ExecuteMissionStep(execution, margin, this);
            }
        }
    }
}
