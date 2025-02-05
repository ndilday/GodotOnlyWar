using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
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
            List<BattleSquad> playerForce = context.PlayerSquads.Select(s => new BattleSquad(false, s)).ToList();
            List<BattleSquad> opFor = context.OpposingForces.Select(s => new BattleSquad(false, s)).ToList();
            var squadPostionMap = placer.PlaceSquads(playerForce, opFor);

            // run the battle
            BattleTurnResolver resolver = new BattleTurnResolver(bgm, playerForce, opFor, context.Region.Planet, true);
            bool battleDone = false;
            resolver.OnBattleComplete += (sender, e) => { battleDone = true; };
            while (!battleDone)
            {
                resolver.ProcessNextTurn();
            }

            returnStep.ExecuteMissionStep(context, marginOfSuccess, returnStep);
        }
    }
}
