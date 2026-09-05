using System.Collections.Generic;
using System;
using System.Linq;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    // Builds the closing facts appended to the end of a battle's player-facing log: casualties on
    // each side and who held the field. Pure and Godot-free (no Godot types) so it can be exercised
    // directly by xunit, same as NpcMissionReportBuilder - BattleTurnResolver is the only caller and
    // populates BattleHistory.ClosingSummary with the result once, at the end of the battle.
    public static class BattleSummaryBuilder
    {
        private const string DefaultFirstSideName = "The attacking force";
        private const string DefaultSecondSideName = "The defending force";

        public static List<string> Build(
            string firstSideFactionName,
            string secondSideFactionName,
            BattleHistory history)
        {
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (history.Turns.Count == 0)
            {
                throw new ArgumentException("Battle history must contain at least one turn.", nameof(history));
            }

            BattleStateSnapshot initial = history.Turns[0].State;
            BattleStateSnapshot final = history.Turns[^1].State;
            return Build(
                firstSideFactionName,
                secondSideFactionName,
                CountSurvivors(initial.AttackerSquads.Values),
                CountSurvivors(final.AttackerSquads.Values),
                CountSurvivors(initial.OpposingSquads.Values),
                CountSurvivors(final.OpposingSquads.Values),
                history.Turns[^1].TurnNumber,
                history.Outcome?.EndReason == BattleEndReason.TurnCap,
                history.Outcome);
        }

        public static List<string> Build(
            string firstSideFactionName,
            string secondSideFactionName,
            int firstSideStartingCount,
            int firstSideRemainingCount,
            int secondSideStartingCount,
            int secondSideRemainingCount,
            int turnsElapsed,
            bool hitTurnCap,
            BattleOutcome outcome = null)
        {
            string firstSideName = ValueOrFallback(firstSideFactionName, DefaultFirstSideName);
            string secondSideName = ValueOrFallback(secondSideFactionName, DefaultSecondSideName);
            int firstSideCasualties = System.Math.Max(0, firstSideStartingCount - firstSideRemainingCount);
            int secondSideCasualties = System.Math.Max(0, secondSideStartingCount - secondSideRemainingCount);

            List<string> lines = new List<string>
            {
                $"The battle ended after {turnsElapsed} turns.",
                $"{firstSideName} suffered {firstSideCasualties} casualties out of {firstSideStartingCount} combatants.",
                $"{secondSideName} suffered {secondSideCasualties} casualties out of {secondSideStartingCount} combatants.",
                BuildFieldHolderLine(firstSideName, secondSideName, firstSideRemainingCount,
                    secondSideRemainingCount, hitTurnCap, outcome)
            };

            return lines;
        }

        // Forced disengagement (turn cap) means neither side broke the other - both still hold
        // whatever ground they had, so the field is contested rather than won. Otherwise the field
        // goes to whichever side still has combatants able to stand on it; simultaneous
        // annihilation of both sides is its own distinct outcome.
        private static string BuildFieldHolderLine(
            string firstSideName,
            string secondSideName,
            int firstSideRemainingCount,
            int secondSideRemainingCount,
            bool hitTurnCap,
            BattleOutcome outcome)
        {
            if (outcome != null)
            {
                return BuildOutcomeLine(firstSideName, secondSideName, outcome);
            }

            if (hitTurnCap)
            {
                return "Both forces still held positions when the fighting broke off; the field was left contested.";
            }

            if (firstSideRemainingCount <= 0 && secondSideRemainingCount <= 0)
            {
                return "Neither side survived to hold the field.";
            }

            string holder = firstSideRemainingCount > 0 ? firstSideName : secondSideName;
            return $"{holder} held the field.";
        }

        private static string BuildOutcomeLine(
            string firstSideName,
            string secondSideName,
            BattleOutcome outcome)
        {
            string holder = outcome.SideHoldingField switch
            {
                BattleSide.Attacker => firstSideName,
                BattleSide.Opposing => secondSideName,
                _ => null
            };
            string departing = outcome.SideHoldingField switch
            {
                BattleSide.Attacker => secondSideName,
                BattleSide.Opposing => firstSideName,
                _ => null
            };

            return outcome.EndReason switch
            {
                BattleEndReason.Withdrawal when holder != null =>
                    $"{departing} withdrew; {holder} held the field.",
                BattleEndReason.Rout when holder != null =>
                    $"{departing} routed; {holder} held the field.",
                BattleEndReason.MutualDisengagement =>
                    "Both forces disengaged; the field was left contested.",
                BattleEndReason.TurnCap =>
                    "Both forces still held positions when the fighting broke off; the field was left contested.",
                BattleEndReason.Annihilation when holder != null => $"{holder} held the field.",
                BattleEndReason.Annihilation => "Neither side survived to hold the field.",
                _ when holder != null => $"{holder} held the field.",
                _ => "The field was left contested."
            };
        }

        private static string ValueOrFallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static int CountSurvivors(IEnumerable<BattleSquadSnapshot> squads) =>
            squads.Sum(squad => squad.Status == BattleSquadStatus.Eliminated ? 0 : squad.Soldiers.Count);
    }
}
