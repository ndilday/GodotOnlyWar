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
            difficulty += (float)Math.Log(context.MissionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // intelligence makes it easier to find a stealthy route
            difficulty -= context.Order.Mission.RegionFaction.Region.IntelligenceLevel;
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(context.MissionSquads);
            if (margin > 0.0f)
            {
                // every point of margin of success modifies the starting range by 20 yards
                ushort range = (ushort)(70 - marginOfSuccess * 20);
                range = Math.Max(range, (ushort)20);
                // set up Ambush battle with OpFor attacker and context.Squad defender
                BattleGridManager bgm = new BattleGridManager();
                AmbushPlacer placer = new AmbushPlacer(bgm, range);
                var squadPostionMap = placer.PlaceSquads(context.OpposingSquads, context.MissionSquads);
                int oppForSize = context.OpposingSquads.Sum(s => s.AbleSoldiers.Count);
                string log = $"Day {context.DaysElapsed}: Force ambushed {oppForSize} {context.OpposingSquads.First().Squad.Faction.Name}\n";
                context.Log.Add(log);
                // run the battle
                BattleTurnResolver resolver = new BattleTurnResolver(bgm, context.MissionSquads, context.OpposingSquads, context.Order.Mission.RegionFaction.Region);
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
    }
}
