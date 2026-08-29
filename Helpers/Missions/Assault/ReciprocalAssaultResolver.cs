using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models.Battles;
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
            List<BattleSquad> firstForce = CombatCapable(first.MissionSquads);
            List<BattleSquad> secondForce = CombatCapable(second.MissionSquads);

            first.DaysElapsed++;
            second.DaysElapsed++;
            int day = Math.Max(first.DaysElapsed, second.DaysElapsed);

            if (firstForce.Count == 0 || secondForce.Count == 0)
            {
                FinishNonviableForces(firstDriver, secondDriver);
                return;
            }

            foreach (BattleSquad squad in firstForce.Concat(secondForce))
            {
                squad.ReallocateEquipment();
            }

            string firstName = firstForce[0].Faction?.Name ?? "Unknown force";
            string secondName = secondForce[0].Faction?.Name ?? "Unknown force";
            string region = first.Order.Mission.RegionFaction.Region.Name;
            string log = $"Day {day}: {firstName} and {secondName} assault forces meet in {region}; neither side can use entrenchments.";
            first.AddLog(log);
            second.AddLog(log);

            long firstBefore = AbleBattleValue(firstForce);
            long secondBefore = AbleBattleValue(secondForce);
            HashSet<int> firstSoldierIds = SoldierIds(firstForce);
            HashSet<int> secondSoldierIds = SoldierIds(secondForce);

            ushort range = MissionOpeningRange.Interpolate(
                firstForce, secondForce, 0f, execution.Random);
            BattleGridManager grid = new();
            AnnihilationPlacer placer = new(grid, range);
            placer.PlaceSquads(firstForce, secondForce);
            BurrowPlacer.PlaceBurrowers(grid, firstForce.Concat(secondForce));

            BattleTurnResolver resolver = new(
                grid,
                firstForce,
                secondForce,
                first.Order.Mission.RegionFaction.Region,
                execution.Battle,
                first.CreateMissionBattleProfile(BattleRole.Attacker),
                second.CreateMissionBattleProfile(BattleRole.Attacker));
            bool battleDone = false;
            resolver.OnBattleComplete += (_, _) => battleDone = true;
            while (!battleDone)
            {
                resolver.ProcessNextTurn();
            }

            BattleHistory history = resolver.BattleHistory;
            int firstDeaths = history.KilledSoldierIds.Count(firstSoldierIds.Contains);
            int secondDeaths = history.KilledSoldierIds.Count(secondSoldierIds.Contains);
            first.RecordReciprocalAssaultOutcome(history, BattleSide.Attacker, secondDeaths);
            second.RecordReciprocalAssaultOutcome(history, BattleSide.Opposing, firstDeaths);
            first.AddBattleReport(history);
            second.AddBattleReport(history);

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
            && context.MissionSquads.Any(squad => squad.AbleSoldiers.Count > 0)
            && !context.MissionLossesExceedAggressionThreshold;

        private static List<BattleSquad> CombatCapable(IEnumerable<BattleSquad> squads) =>
            squads.Where(squad => squad.AbleSoldiers.Count > 0).ToList();

        private static long AbleBattleValue(IEnumerable<BattleSquad> squads) =>
            squads.SelectMany(squad => squad.AbleSoldiers)
                .Sum(soldier => (long)soldier.Soldier.Template.BattleValue);

        private static HashSet<int> SoldierIds(IEnumerable<BattleSquad> squads) =>
            squads.SelectMany(squad => squad.Soldiers)
                .Select(soldier => soldier.Soldier.Id)
                .ToHashSet();
    }
}
