using OnlyWar.Helpers.Battles;
using OnlyWar.Contracts.Battles;
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
        private readonly bool _defendersMayBurrow;
        private readonly EngagementRole _attackerBattleRole;

        public string Description { get { return "Meeting Engagement"; } }

        public MeetingEngagementMissionStep(
            bool defendersMayBurrow = true,
            EngagementRole attackerBattleRole = EngagementRole.Attacker)
        {
            _defendersMayBurrow = defendersMayBurrow;
            _attackerBattleRole = attackerBattleRole;
        }

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

            // set up meeting engagement between the mission force (attacker) and the defenders.
            // The attacker's prep-check margin slides the opening range between the two sides'
            // preferred engagement ranges: a decisive attacker fights at its own preference, a
            // repelled one is held out at the defender's. See MissionOpeningRange.
            ushort range = MissionOpeningRange.Interpolate(
                missionSquads, opposingSquads, marginOfSuccess, execution.Random);
            int oppForSize = opposingSquads.Sum(s => s.AbleSoldiers.Count);
            // See AmbushedMissionStep: Faction is guarded rather than assumed everywhere else it is read.
            string opposingFaction = opposingSquads.First().Faction?.Name ?? "an unidentified force";
            string log = $"Day {context.DaysElapsed}: Force accepted engagement with {oppForSize} {opposingFaction}\n";
            context.AddLog(log);
            // Measured before and after so a multi-day assault faces a garrison depleted by the fighting
            // it has already done, rather than a fresh full-strength one every morning.
            long opposingBattleValueBefore = AbleBattleValue(opposingSquads);
            EngagementResult engagement = execution.Engagements.Resolve(
                    new EngagementInput(
                        context.MissionParticipants,
                        context.OpposingParticipants,
                        context.Order.Mission.RegionFaction.Region,
                    range,
                    context.CreateMissionEngagementProfile(_attackerBattleRole),
                    MissionContext.CreateOpposingEngagementProfile(opposingSquads, EngagementRole.Defender),
                    EngagementPlacement.Meeting,
                    _defendersMayBurrow
                        ? EngagementBurrowSide.Both
                        : EngagementBurrowSide.First));
            context.RecordBattleOutcome(engagement);
            context.AddBattleReport(engagement);
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
