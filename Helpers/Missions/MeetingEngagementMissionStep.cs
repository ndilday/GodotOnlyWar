using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Battles;
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

        public void ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep returnStep)
        {
            MissionContext context = execution.State;
            List<BattleSquad> missionSquads = context.MissionSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            List<BattleSquad> opposingSquads = context.OpposingSquads
                .Where(squad => squad.AbleSoldiers.Count > 0)
                .ToList();
            if (missionSquads.Count == 0 || opposingSquads.Count == 0)
            {
                context.NoViableTarget = true;
                context.AddLog($"Day {context.DaysElapsed}: No combat-capable forces remain for engagement.");
                return;
            }

            // set up meeting engagement between the mission force (attacker) and the defenders.
            // The attacker's prep-check margin, as a CDF, slides the opening range between the two
            // sides' preferred engagement ranges: a decisive attacker closes toward its own
            // preference, a repelled one is held out at the defender's preference. Interpolating
            // between the two preferences (rather than from their midpoint) means neither side is
            // ever dragged past the other's preferred range.
            float rangeModifier = GaussianCalculator.ApproximateNormalCDF(marginOfSuccess);
            BattleSoldier defenderSoldier = opposingSquads.First()
                .GetRandomSquadMember(execution.Random);
            BattleSoldier attackerSoldier = missionSquads.First()
                .GetRandomSquadMember(execution.Random);
            double attackerRange = missionSquads.Average(s => s.GetPreferredOpeningRange(defenderSoldier.Soldier.Size, defenderSoldier.Armor.Template.ArmorProvided, defenderSoldier.Soldier.Constitution, defenderSoldier.Soldier.Template.Species.RangedEvasion));
            double defenderRange = opposingSquads.Average(s => s.GetPreferredOpeningRange(attackerSoldier.Soldier.Size, attackerSoldier.Armor.Template.ArmorProvided, attackerSoldier.Soldier.Constitution, attackerSoldier.Soldier.Template.Species.RangedEvasion));
            ushort range = (ushort)(defenderRange + (attackerRange - defenderRange) * rangeModifier);
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
            BattleTurnResolver resolver = new BattleTurnResolver(
                bgm,
                missionSquads,
                opposingSquads,
                context.Order.Mission.RegionFaction.Region,
                execution.Battle,
                context.CreateMissionBattleProfile(BattleRole.Attacker),
                MissionContext.CreateOpposingBattleProfile(opposingSquads, BattleRole.Defender));
            bool battleDone = false;
            resolver.OnBattleComplete += (sender, e) => { battleDone = true; };
            while (!battleDone)
            {
                resolver.ProcessNextTurn();
            }
            context.RecordBattleOutcome(resolver.BattleHistory);
            context.AddBattleReport(resolver.BattleHistory);
            // A force left combat-ineffective by the engagement ends its mission here rather than
            // recursing into steps that assume a manned squad (placement/checks index into
            // AbleSoldiers and would throw). Mirrors InfiltrateMissionStep.ShouldContinue's
            // casualty abort, applied at the point the battle actually depletes the squad.
            if (!context.MissionSquads.Any(squad => squad.AbleSoldiers.Count > 0))
            {
                context.ForceWithdrewUnderFire = true;
                context.AddLog($"Day {context.DaysElapsed}: Force combat-ineffective; mission ended.");
                return;
            }
            if (context.ForceWithdrewUnderFire)
            {
                context.AddLog($"Day {context.DaysElapsed}: Force withdrew from the engagement under fire.");
                return;
            }
            if(returnStep == null)
            {
                return;
            }
            returnStep.ExecuteMissionStep(execution, marginOfSuccess, returnStep);
        }
    }
}
