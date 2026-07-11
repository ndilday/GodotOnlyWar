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
            List<BattleSquad> missionSquads = context.MissionSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            List<BattleSquad> opposingSquads = context.OpposingSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            if (missionSquads.Count == 0 || opposingSquads.Count == 0)
            {
                context.AddLog($"Day {context.DaysElapsed}: No combat-capable forces remain for engagement.");
                return;
            }

            // set up meeting engagement with OpFor and context.Squad
            // convert margin of success to a CDF, and use that to adjust the engagement range between the preference of the two sides
            float rangeModifier = GaussianCalculator.ApproximateNormalCDF(marginOfSuccess);
            BattleSoldier enemySoldier = opposingSquads.First().GetRandomSquadMember();
            BattleSoldier playerSoldier = missionSquads.First().GetRandomSquadMember();
            double playerRange = missionSquads.Average(s => s.GetPreferredEngagementRange(enemySoldier.Soldier.Size, enemySoldier.Armor.Template.ArmorProvided, enemySoldier.Soldier.Constitution, enemySoldier.Soldier.Template.Species.RangedEvasion));
            double enemyRange = opposingSquads.Average(s => s.GetPreferredEngagementRange(playerSoldier.Soldier.Size, playerSoldier.Armor.Template.ArmorProvided, playerSoldier.Soldier.Constitution, playerSoldier.Soldier.Template.Species.RangedEvasion));
            double halfway = (playerRange + enemyRange) / 2;
            ushort range = (ushort)(halfway + (playerRange - halfway) * rangeModifier);
            // set up meeting engagement battle
            BattleGridManager bgm = new BattleGridManager();
            AnnihilationPlacer placer = new AnnihilationPlacer(bgm, range);
            var squadPostionMap = placer.PlaceSquads(missionSquads, opposingSquads);
            // burrow-capable squads (e.g. Raveners) erupt directly into melee instead
            // of advancing across the gap — see Design/EvasionBurrowAndAmbush.md
            BurrowPlacer.PlaceBurrowers(bgm, missionSquads.Concat(opposingSquads));
            int oppForSize = opposingSquads.Sum(s => s.AbleSoldiers.Count);
            string log = $"Day {context.DaysElapsed}: Force accepted engagement with {oppForSize} {opposingSquads.First().Squad.Faction.Name}\n";
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
            // A force left combat-ineffective by the engagement ends its mission here rather than
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
            returnStep.ExecuteMissionStep(context, marginOfSuccess, returnStep);
        }
    }
}
