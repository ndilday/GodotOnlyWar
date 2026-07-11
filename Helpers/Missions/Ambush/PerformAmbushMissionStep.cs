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
            List<BattleSquad> missionSquads = context.MissionSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            List<BattleSquad> opposingSquads = context.OpposingSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            if (missionSquads.Count == 0 || opposingSquads.Count == 0)
            {
                context.AddLog($"Day {context.DaysElapsed}: No combat-capable forces remain for ambush.");
                return;
            }

            // negative mod for size of enemy force
            // mod for terrain
            // mod for enemy recon focus
            // mod for equipment
            BaseSkill stealth = GameDataSingleton.Instance.GameRulesData.Skills.Stealth;
            RegionFaction enemyFaction = context.Order.Mission.RegionFaction;
            float difficulty = enemyFaction.GetOwnRegionIntel() * 0.5f;
            // every degree of magnitude of troops adds one to the difficulty
            difficulty += (float)Math.Log(missionSquads.Sum(s => s.AbleSoldiers.Count), 10);
            // the attacker's own knowledge of the region makes it easier to find a stealthy route
            Faction attacker = missionSquads.FirstOrDefault()?.Squad.Faction;
            if (attacker != null) difficulty -= enemyFaction.Region.GetFactionRegionIntel(attacker);
            SquadMissionTest missionTest = new SquadMissionTest(stealth, difficulty);

            context.DaysElapsed++;
            float margin = missionTest.RunMissionCheck(missionSquads);
            if (margin > 0.0f)
            {
                // every point of margin of success modifies the starting range by 20 yards
                ushort range = (ushort)(70 - marginOfSuccess * 20);
                range = Math.Max(range, (ushort)20);
                // set up Ambush battle with OpFor attacker and context.Squad defender
                BattleGridManager bgm = new BattleGridManager();
                AmbushPlacer placer = new AmbushPlacer(bgm, range);
                var squadPostionMap = placer.PlaceSquads(opposingSquads, missionSquads);
                // burrowing squads erupt straight into melee — see Design/EvasionBurrowAndAmbush.md
                BurrowPlacer.PlaceBurrowers(bgm, missionSquads.Concat(opposingSquads));
                int oppForSize = opposingSquads.Sum(s => s.AbleSoldiers.Count);
                string log = $"Day {context.DaysElapsed}: Force ambushed {oppForSize} {opposingSquads.First().Squad.Faction.Name}\n";
                context.AddLog(log);
                // run the battle
                BattleTurnResolver resolver = new BattleTurnResolver(bgm, missionSquads, opposingSquads, context.Order.Mission.RegionFaction.Region);
                bool battleDone = false;
                resolver.OnBattleComplete += (sender, e) => { battleDone = true; };
                while (!battleDone)
                {
                    resolver.ProcessNextTurn();
                }
                context.EnemiesKilled += resolver.BattleHistory.EnemiesKilled;
                context.AddBattleLog(resolver.BattleHistory.GetBattleLog(), resolver.BattleHistory);
                new ExfiltrateMissionStep().ExecuteMissionStep(context, 0, null);
            }
            else
            {
                new MeetingEngagementMissionStep().ExecuteMissionStep(context, margin, new ExfiltrateMissionStep());
            }
        }
    }
}
