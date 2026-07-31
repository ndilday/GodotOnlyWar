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
                context.AddLog($"Day {context.DaysElapsed}: No combat-capable forces remain for engagement.");
                return MissionStepResult.Complete;
            }

            // Redistribute the squad's loadout before the fight: a weapon whose carrier fell in an
            // earlier engagement this mission is picked up by the best remaining shooter rather than
            // left on the ground for the rest of the week.
            foreach (BattleSquad squad in missionSquads)
            {
                squad.ReallocateEquipment();
            }

            // set up meeting engagement between the mission force (attacker) and the defenders.
            // The attacker's prep-check margin slides the opening range between the two sides'
            // preferred engagement ranges: a decisive attacker fights at its own preference, a
            // repelled one is held out at the defender's. See MissionOpeningRange.
            ushort range = MissionOpeningRange.Interpolate(
                missionSquads, opposingSquads, marginOfSuccess, execution.Random);
            // set up meeting engagement battle
            BattleGridManager bgm = new BattleGridManager();
            AnnihilationPlacer placer = new AnnihilationPlacer(bgm, range);
            var squadPostionMap = placer.PlaceSquads(missionSquads, opposingSquads);
            // burrow-capable squads (e.g. Raveners) erupt directly into melee instead
            // of advancing across the gap — see OnlyWar_TDD.md §6.6
            BurrowPlacer.PlaceBurrowers(bgm, missionSquads.Concat(opposingSquads));
            int oppForSize = opposingSquads.Sum(s => s.AbleSoldiers.Count);
            // See AmbushedMissionStep: Faction is guarded rather than assumed everywhere else it is read.
            string opposingFaction = opposingSquads.First().Squad?.Faction?.Name ?? "an unidentified force";
            string log = $"Day {context.DaysElapsed}: Force accepted engagement with {oppForSize} {opposingFaction}\n";
            context.AddLog(log);
            // Measured before and after so a multi-day assault faces a garrison depleted by the fighting
            // it has already done, rather than a fresh full-strength one every morning.
            long opposingBattleValueBefore = AbleBattleValue(opposingSquads);
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
            context.RecordDefenderLosses(
                opposingBattleValueBefore - AbleBattleValue(opposingSquads));
            // A force left combat-ineffective by the engagement ends its mission here rather than
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
                context.AddLog($"Day {context.DaysElapsed}: Force withdrew from the engagement under fire.");
                return MissionStepResult.Complete;
            }
            // Both early exits above return Complete deliberately: the resume target is NOT taken
            // when the force is spent. A caller that must run something after the engagement whatever
            // its outcome uses MissionStepResult.Then instead (see WithdrawIfAbleMissionStep).
            if (resumeStep == null)
            {
                return MissionStepResult.Complete;
            }
            return MissionStepResult.Continue(resumeStep, marginOfSuccess, resumeStep);
        }

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads) =>
            squads
                .SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);
    }
}
