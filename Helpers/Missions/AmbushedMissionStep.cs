using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    internal class AmbushedMissionStep : IMissionStep
    {
        public string Description { get { return "Ambushed"; } }

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

            // every point of margin of success modifies the starting range by 20 yards
            ushort range = (ushort)Math.Clamp((int)Math.Round(70 + marginOfSuccess * 20), 1, 200);
            // set up Ambush battle with OpFor attacker and context.Squad defender
            BattleGridManager bgm = new BattleGridManager();
            AmbushPlacer placer = new AmbushPlacer(bgm, range);
            var squadPostionMap = placer.PlaceSquads(missionSquads, opposingSquads);
            // burrowing ambushers erupt straight into melee — see Design/EvasionBurrowAndAmbush.md
            BurrowPlacer.PlaceBurrowers(bgm, missionSquads.Concat(opposingSquads));
            int oppForSize = opposingSquads.Sum(s => s.AbleSoldiers.Count);
            string log = $"Day {context.DaysElapsed}: Force was ambushed by {oppForSize} {opposingSquads.First().Squad.Faction.Name}\n";
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
            // A force left combat-ineffective by the ambush ends its mission here rather than
            // recursing into steps that assume a manned squad (placement/checks index into
            // AbleSoldiers and would throw). Mirrors InfiltrateMissionStep.ShouldContinue's
            // casualty abort, applied at the point the battle actually depletes the squad.
            if (!context.MissionSquads.Any(s => s.ShouldContinueMission()))
            {
                context.AddLog($"Day {context.DaysElapsed}: Force combat-ineffective; mission ended.");
                return;
            }
            if(returnStep == null)
            {
                return;
            }
            returnStep.ExecuteMissionStep(context, 0, this);
        }
    }
}
