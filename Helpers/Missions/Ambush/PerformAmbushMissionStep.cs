using OnlyWar.Helpers.Missions.Assassination;
using OnlyWar.Helpers.Missions.Recon;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Units;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Helpers.Extensions;

namespace OnlyWar.Helpers.Missions.Ambush
{
    public class PerformAmbushMissionStep : IMissionStep
    {
        public string Description { get { return "Ambush"; } }

        public PerformAmbushMissionStep() { }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.BaseSkillMap.Values.First(s => s.Name == "Stealth");
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float difficulty = enemyFaction.Detection;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(context.PlayerSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // intelligence makes it easier to find a stealthy route
            difficulty -= context.Order.Mission.RegionFaction.Region.IntelligenceLevel;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);
            context.OpposingForces = PopulateOpposingForce(context.Order.Mission.MissionSize, enemyFaction);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.PlayerSquads);
            if (margin > 0.0f)
            {
                // every point of margin of success modifies the starting range by 20 yards
                ushort range = (ushort)(70 - marginOfSuccess * 20);
                range = Math.Max(range, (ushort)20);
                // set up Ambush battle with OpFor attacker and context.Squad defender
                BattleGridManager bgm = new BattleGridManager();
                AmbushPlacer placer = new AmbushPlacer(bgm, range);
                var squadPostionMap = placer.PlaceSquads(context.OpposingForces, context.PlayerSquads);
                int oppForSize = context.OpposingForces.Sum(s => s.AbleSoldiers.Count);
                string log = $"Day {context.DaysElapsed}: Force ambushed {oppForSize} {context.OpposingForces.First().Squad.Faction.Name}\n";
                context.Log.Add(log);
                // run the battle
                BattleTurnResolver resolver = new BattleTurnResolver(bgm, context.PlayerSquads, context.OpposingForces, context.Order.Mission.RegionFaction.Region);
                bool battleDone = false;
                resolver.OnBattleComplete += (sender, e) => { battleDone = true; };
                while (!battleDone)
                {
                    resolver.ProcessNextTurn();
                }
                context.EnemiesKilled += resolver.BattleHistory.EnemiesKilled;
                context.Log.Add(resolver.BattleHistory.GetBattleLog());
                new ExfiltrateMissionStep().ExecuteMissionStep(context, 0, null);
            }
            else
            {
                new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, new ExfiltrateMissionStep());
            }
        }

        private List<BattleSquad> PopulateOpposingForce(int missionSize, RegionFaction enemyFaction)
        {
            List<BattleSquad> opposingForces = new List<BattleSquad>();
            // determine size of force to generate
            double log = RNG.GetLinearDouble() + missionSize;
            int forceSize = (int)Math.Pow(10, log);
            // generate opposing force
            int totalGenerated = 0;
            while (totalGenerated < forceSize)
            {
                Unit enemyUnit = TempArmyBuilder.GenerateArmyFromRegionFaction(enemyFaction);
                opposingForces.AddRange(enemyUnit.GetAllSquads().Select(s => new BattleSquad(false, s)));
                totalGenerated += enemyUnit.GetAllMembers().Count();
            }
            return opposingForces;
        }
    }
}
