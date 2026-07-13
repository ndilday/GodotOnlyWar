using System.Collections.Generic;

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
            int firstSideStartingCount,
            int firstSideRemainingCount,
            int secondSideStartingCount,
            int secondSideRemainingCount,
            int turnsElapsed,
            bool hitTurnCap)
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
                BuildFieldHolderLine(firstSideName, secondSideName, firstSideRemainingCount, secondSideRemainingCount, hitTurnCap)
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
            bool hitTurnCap)
        {
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

        private static string ValueOrFallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
