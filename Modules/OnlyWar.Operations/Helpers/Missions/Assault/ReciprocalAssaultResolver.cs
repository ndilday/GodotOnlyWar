using OnlyWar.Battles.Abstractions;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Missions.Assault
{
    /// <summary>
    /// Resolves one day when two factions are actively assaulting one another in the same region.
    /// The assault forces meet in the field; neither uses the fortifications behind it. A force that
    /// withdraws but remains inside its mission-level loss tolerance reforms for the following day.
    /// </summary>
    internal static class ReciprocalAssaultResolver
    {
        internal static void ResolveDay(
            MissionStepDriver firstDriver,
            MissionStepDriver secondDriver)
        {
            MissionExecutionContext execution = firstDriver.Execution;
            MissionContext first = firstDriver.State;
            MissionContext second = secondDriver.State;
            List<OperationalMissionElement> firstForce = CombatCapable(first.MissionSquads);
            List<OperationalMissionElement> secondForce = CombatCapable(second.MissionSquads);

            first.DaysElapsed++;
            second.DaysElapsed++;
            int day = Math.Max(first.DaysElapsed, second.DaysElapsed);

            if (firstForce.Count == 0 || secondForce.Count == 0)
            {
                FinishNonviableForces(firstDriver, secondDriver);
                return;
            }

            string firstName = firstForce[0].Faction?.Name ?? "Unknown force";
            string secondName = secondForce[0].Faction?.Name ?? "Unknown force";
            string region = first.Order.Mission.RegionFaction.Region.Name;
            string log = $"Day {day}: {firstName} and {secondName} assault forces meet in {region}; neither side can use entrenchments.";
            first.AddLog(log);
            second.AddLog(log);

            long firstBefore = AbleBattleValue(firstForce);
            long secondBefore = AbleBattleValue(secondForce);

            ushort range = MissionOpeningRange.Interpolate(
                firstForce, secondForce, execution.Engagements, 0f, execution.Random);
            EngagementResult engagement = execution.Engagements.Resolve(
                    new EngagementInput(
                        first.MissionParticipants,
                        second.MissionParticipants,
                        first.Order.Mission.RegionFaction.Region.ToEngagementLocation(),
                    range,
                    first.CreateMissionEngagementProfile(EngagementRole.Attacker),
                    second.CreateMissionEngagementProfile(EngagementRole.Attacker),
                    EngagementPlacement.Meeting,
                    EngagementBurrowSide.Both,
                    ReallocateFirstSideEquipment: true,
                    ReallocateSecondSideEquipment: true));
            int firstDeaths = engagement.SecondSideEnemyDeaths;
            int secondDeaths = engagement.FirstSideEnemyDeaths;
            first.RecordReciprocalAssaultOutcome(engagement, EngagementSide.First, firstDeaths);
            second.RecordReciprocalAssaultOutcome(engagement, EngagementSide.Second, secondDeaths);
            first.AddBattleReport(engagement);
            second.AddBattleReport(engagement);

            // These losses belong to already-committed assault formations, not either region's
            // defensive military pool. Do not feed them into DefenderBattleValueDestroyed: NPC
            // planners already removed committed strength from its source, and player casualties
            // are retained directly on their soldiers.
            long firstLost = Math.Max(0L, firstBefore - AbleBattleValue(firstForce));
            long secondLost = Math.Max(0L, secondBefore - AbleBattleValue(secondForce));
            first.AddLog($"Day {day}: Meeting engagement losses: {firstName} {firstLost} BV; {secondName} {secondLost} BV.");
            second.AddLog($"Day {day}: Meeting engagement losses: {secondName} {secondLost} BV; {firstName} {firstLost} BV.");

            FinishNonviableForces(firstDriver, secondDriver);
        }

        private static void FinishNonviableForces(
            MissionStepDriver firstDriver,
            MissionStepDriver secondDriver)
        {
            FinishIfNonviable(firstDriver);
            FinishIfNonviable(secondDriver);
        }

        private static void FinishIfNonviable(MissionStepDriver driver)
        {
            MissionContext context = driver.State;
            if (CanContestTomorrow(context))
            {
                return;
            }

            context.ForceWithdrewUnderFire = true;
            context.ObjectiveAborted = true;
            context.ReciprocalAssaultDefeated = true;
            context.AddLog(
                $"Day {context.DaysElapsed}: Assault force can no longer contest the meeting engagement.");
            driver.Complete();
        }

        internal static bool CanContestTomorrow(MissionContext context) =>
            context != null
            && context.MissionSquads.Any(squad => squad.AbleMembers.Count > 0)
            && !context.MissionLossesExceedAggressionThreshold;

        private static List<OperationalMissionElement> CombatCapable(
            IEnumerable<OperationalMissionElement> squads) =>
            squads.Where(squad => squad.AbleMembers.Count > 0).ToList();

        private static long AbleBattleValue(IEnumerable<OperationalMissionElement> squads) =>
            squads.SelectMany(squad => squad.AbleMembers)
                .Sum(soldier => (long)(soldier.Template?.BattleValue ?? 0));

    }
}
