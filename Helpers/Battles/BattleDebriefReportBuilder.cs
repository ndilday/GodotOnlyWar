using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    public static class BattleDebriefReportBuilder
    {
        public static BattleDebriefReport Build(BattleHistory history)
        {
            if (history == null || history.Turns.Count == 0)
            {
                return new BattleDebriefReport(0, 0, []);
            }

            BattleStateSnapshot initialState = history.Turns[0].State;
            List<BattleSquadSnapshot> initialSquads = initialState.AttackerSquads.Values
                .Concat(initialState.OpposingSquads.Values)
                .ToList();
            Dictionary<int, BattleSquadSnapshot> squadBySoldierId = initialSquads
                .SelectMany(squad => squad.Soldiers.Select(soldier => (soldier.Id, Squad: squad)))
                .GroupBy(entry => entry.Id)
                .ToDictionary(group => group.Key, group => group.First().Squad);
            Dictionary<int, BattleSoldierSnapshot> soldierById = initialSquads
                .SelectMany(squad => squad.Soldiers)
                .GroupBy(soldier => soldier.Id)
                .ToDictionary(group => group.Key, group => group.First());

            int playerDeaths = history.KilledSoldierIds.Count(id =>
                squadBySoldierId.TryGetValue(id, out BattleSquadSnapshot squad) && squad.IsPlayerAligned);
            int opposingDeaths = history.KilledSoldierIds.Count(id =>
                squadBySoldierId.TryGetValue(id, out BattleSquadSnapshot squad) && !squad.IsPlayerAligned);

            int playerIncapacitated = history.IncapacitatedSoldierIds.Count(id =>
                squadBySoldierId.TryGetValue(id, out BattleSquadSnapshot squad) && squad.IsPlayerAligned);

            List<BattleCasualtyEntry> casualties = history.DamagedSoldierIds
                .Concat(history.KilledSoldierIds)
                .Concat(history.IncapacitatedSoldierIds)
                .Distinct()
                .Where(id => soldierById.TryGetValue(id, out BattleSoldierSnapshot soldier)
                    && soldier.Soldier is PlayerSoldier)
                .Select(id => BuildCasualty(
                    soldierById[id],
                    squadBySoldierId[id],
                    history.KilledSoldierIds.Contains(id),
                    history.IncapacitatedSoldierIds.Contains(id)))
                .OrderBy(entry => entry.Disposition)
                .ThenBy(entry => entry.Company)
                .ThenBy(entry => entry.Squad)
                .ThenBy(entry => entry.Rank)
                .ThenBy(entry => entry.Name)
                .ToList();

            return new BattleDebriefReport(
                playerDeaths, opposingDeaths, casualties, playerIncapacitated);
        }

        /// <summary>
        /// The one-line casualty banner shown above a battle in the debrief. Lives here so the
        /// mission log line and the Godot debrief view cannot drift apart. Incapacitated brothers
        /// are named only when there are any -- the common case is still dead-vs-dead.
        /// </summary>
        public static string BuildSummaryLine(BattleDebriefReport report)
        {
            if (report == null) return "";
            string friendly = report.PlayerIncapacitated > 0
                ? $"Friendly dead: {report.PlayerDeaths}    Friendly incapacitated: {report.PlayerIncapacitated}"
                : $"Friendly dead: {report.PlayerDeaths}";
            return $"{friendly}    Opposing dead: {report.OpposingDeaths}";
        }

        private static BattleCasualtyEntry BuildCasualty(
            BattleSoldierSnapshot soldierSnapshot,
            BattleSquadSnapshot squadSnapshot,
            bool isDead,
            bool isIncapacitated)
        {
            ISoldier soldier = soldierSnapshot.Soldier;
            bool replacementRequired = soldier.Body.HitLocations.Any(location => location.IsReplacementEligible);
            int recoveryWeeks = soldier.Body.HitLocations
                .Select(location => (int)location.Wounds.RecoveryTimeLeft())
                .DefaultIfEmpty(0)
                .Max();
            // Death first, then "he went down" -- an incapacitated brother is usually also awaiting
            // a replacement limb, and the roster should lead with the more serious fact.
            BattleCasualtyDisposition disposition = isDead
                ? BattleCasualtyDisposition.Dead
                : isIncapacitated
                    ? BattleCasualtyDisposition.Incapacitated
                    : replacementRequired
                        ? BattleCasualtyDisposition.ReplacementRequired
                        : BattleCasualtyDisposition.Recovering;

            return new BattleCasualtyEntry(
                soldier.Id,
                soldier.Name ?? "Unknown",
                soldier.Template?.Name ?? "Battle-Brother",
                squadSnapshot?.Name ?? soldierSnapshot.SquadName ?? "Unassigned",
                squadSnapshot?.Squad?.ParentUnit?.Name ?? "No Company",
                disposition,
                recoveryWeeks);
        }
    }
}
