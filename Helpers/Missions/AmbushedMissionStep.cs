﻿using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Missions;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    internal class AmbushedMissionStep : IMissionStep
    {
        public string Description { get { return "Ambushed"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // every point of margin of success modifies the starting range by 20 yards
            ushort range = (ushort)(70 + marginOfSuccess * 20);
            // set up Ambush battle with OpFor attacker and context.Squad defender
            BattleGridManager bgm = new BattleGridManager();
            AmbushPlacer placer = new AmbushPlacer(bgm, range);
            var squadPostionMap = placer.PlaceSquads(context.MissionSquads, context.OpposingSquads);
            int oppForSize = context.OpposingSquads.Sum(s => s.AbleSoldiers.Count);
            string log = $"Day {context.DaysElapsed}: Force was ambushed by {oppForSize} {context.OpposingSquads.First().Squad.Faction.Name}\n";
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
            if(returnStep == null)
            {
                return;
            }
            returnStep.ExecuteMissionStep(context, 0, this);
        }
    }
}
