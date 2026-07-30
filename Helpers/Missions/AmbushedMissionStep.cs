using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Battles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions
{
    internal class AmbushedMissionStep : IMissionStep
    {
        public string Description { get { return "Ambushed"; } }

        public MissionStepResult ExecuteMissionStep(MissionExecutionContext execution, float marginOfSuccess, IMissionStep resumeStep)
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
                context.AddLog($"Day {context.DaysElapsed}: No combat-capable forces remain for ambush.");
                return MissionStepResult.Complete;
            }

            // See MeetingEngagementMissionStep: the loadout is redistributed at the start of every
            // battle, so a fallen carrier's weapon is picked up rather than lost for the mission.
            foreach (BattleSquad squad in missionSquads)
            {
                squad.ReallocateEquipment();
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
            // Squad.Faction resolves through SquadTemplate.Faction, which is guarded rather than assumed
            // everywhere else it is read (Squad.CurrentRegion, BattleSquad.IsPlayerAligned). Guarding it
            // here too keeps a log string from being able to take down a whole turn.
            string opposingFaction = opposingSquads.First().Squad?.Faction?.Name ?? "an unidentified force";
            string log = $"Day {context.DaysElapsed}: Force was ambushed by {oppForSize} {opposingFaction}\n";
            context.AddLog(log);
            long opposingBattleValueBefore = AbleBattleValue(opposingSquads);
            // run the battle
            BattleTurnResolver resolver = new BattleTurnResolver(
                bgm,
                missionSquads,
                opposingSquads,
                context.Order.Mission.RegionFaction.Region,
                execution.Battle,
                context.CreateMissionBattleProfile(BattleRole.Ambushed),
                MissionContext.CreateOpposingBattleProfile(opposingSquads, BattleRole.Ambusher));
            bool battleDone = false;
            resolver.OnBattleComplete += (sender, e) => { battleDone = true; };
            while (!battleDone)
            {
                resolver.ProcessNextTurn();
            }
            context.RecordBattleOutcome(resolver.BattleHistory);
            context.AddBattleReport(resolver.BattleHistory);
            context.RecordDefenderLosses(
                opposingBattleValueBefore - AbleBattleValue(opposingSquads));
            // A force left combat-ineffective by the ambush ends its mission here rather than
            // recursing into steps that assume a manned squad (placement/checks index into
            // AbleSoldiers and would throw). Mirrors InfiltrateMissionStep.ShouldContinue's
            // casualty abort, applied at the point the battle actually depletes the squad.
            if (!context.MissionSquads.Any(squad => squad.AbleSoldiers.Count > 0))
            {
                context.ForceWithdrewUnderFire = true;
                context.AddLog($"Day {context.DaysElapsed}: Force combat-ineffective; mission ended.");
                return MissionStepResult.Complete;
            }
            if (context.ForceWithdrewUnderFire)
            {
                context.AddLog($"Day {context.DaysElapsed}: Force withdrew from the ambush under fire.");
                return MissionStepResult.Complete;
            }
            if (resumeStep == null)
            {
                return MissionStepResult.Complete;
            }
            return MissionStepResult.Continue(resumeStep, 0, this);
        }

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads) =>
            squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
    }
}
