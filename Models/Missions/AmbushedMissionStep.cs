using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Missions
{
    internal class AmbushedMissionStep : IMissionStep
    {
        public string Description { get { return "Ambushed"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // every point of margin of success modifies the starting range by 20 yards
            ushort range = (ushort)(70 + (marginOfSuccess * 20));
            // set up Ambush battle with OpFor attacker and context.Squad defender
            BattleGridManager bgm = new BattleGridManager();
            AmbushPlacer placer = new AmbushPlacer(bgm, range);
            List<BattleSquad> playerForce = new List<BattleSquad> { new BattleSquad(true, context.Squad) };
            List<BattleSquad> opFor = context.OpposingForces.Select(s => new BattleSquad(false, s)).ToList();
            var squadPostionMap = placer.PlaceSquads(playerForce, opFor);

            // run the battle

            returnStep.ExecuteMissionStep(context, marginOfSuccess, returnStep);
        }
    }
}
