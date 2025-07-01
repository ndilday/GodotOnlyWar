using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Helpers.Missions
{
    public class MeetingEngagementMissionStep : IMissionStep
    {
        public string Description { get { return "Meeting Engagement"; } }

        public void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep)
        {
            // set up meeting engagement with OpFor and context.Squad
            // convert margin of success to a CDF, and use that to adjust the engagement range between the preference of the two sides
            float rangeModifier = GaussianCalculator.ApproximateNormalCDF(marginOfSuccess);
            BattleSoldier enemySoldier = context.OpposingSquads.First().GetRandomSquadMember();
            BattleSoldier playerSoldier = context.MissionSquads.First().GetRandomSquadMember();
            double playerRange = context.MissionSquads.Average(s => s.GetPreferredEngagementRange(enemySoldier.Soldier.Size, enemySoldier.Armor.Template.ArmorProvided, enemySoldier.Soldier.Constitution));
            double enemyRange = context.OpposingSquads.Average(s => s.GetPreferredEngagementRange(playerSoldier.Soldier.Size, playerSoldier.Armor.Template.ArmorProvided, playerSoldier.Soldier.Constitution));
            double halfway = (playerRange + enemyRange) / 2;
            ushort range = (ushort)(halfway + (playerRange - halfway) * rangeModifier);
            // set up meeting engagement battle
            BattleGridManager bgm = new BattleGridManager();
            AnnihilationPlacer placer = new AnnihilationPlacer(bgm, range);
            var squadPostionMap = placer.PlaceSquads(context.MissionSquads, context.OpposingSquads);
            int oppForSize = context.OpposingSquads.Sum(s => s.AbleSoldiers.Count);
            string log = $"Day {context.DaysElapsed}: Force accepted engagement with {oppForSize} {context.OpposingSquads.First().Squad.Faction.Name}\n";
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
            returnStep.ExecuteMissionStep(context, marginOfSuccess, returnStep);
        }
    }
}
