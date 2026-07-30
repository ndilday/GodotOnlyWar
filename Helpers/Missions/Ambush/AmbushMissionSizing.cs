using OnlyWar.Helpers.StrategicCombat;
using OnlyWar.Models.Missions;
using System;

namespace OnlyWar.Helpers.Missions.Ambush
{
    /// <summary>
    /// Converts the intelligence-discovered ambush target into both its tactical force-generation
    /// budget and the player-facing number of full Tactical Marine squads needed to reach parity.
    /// </summary>
    public static class AmbushMissionSizing
    {
        public const int TacticalMarinesPerReferenceSquad = 10;

        public static long ReferenceSquadBattleValue =>
            TacticalMarinesPerReferenceSquad * StrategicCombatRules.TacticalMarineBattleValue;

        public static long RollTargetBattleValue(int missionSize, IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            double pdfTrooperEquivalent = Math.Pow(
                10,
                Math.Max(1, missionSize) + random.GetLinearDouble());
            long forceSize = pdfTrooperEquivalent >= long.MaxValue
                ? long.MaxValue
                : Math.Max(1L, (long)pdfTrooperEquivalent);

            return SaturatingMultiply(forceSize, StrategicCombatRules.PdfTrooperBattleValue);
        }

        // Older saves have only the order-of-magnitude mission band. Use the geometric midpoint
        // of that band as a stable migration value so those opportunities also gain a recommendation
        // and no longer reroll their target after being assigned.
        public static long EstimateLegacyTargetBattleValue(int missionSize)
        {
            double pdfTrooperEquivalent = Math.Pow(10, Math.Max(1, missionSize) + 0.5);
            long forceSize = pdfTrooperEquivalent >= long.MaxValue
                ? long.MaxValue
                : Math.Max(1L, (long)pdfTrooperEquivalent);

            return SaturatingMultiply(forceSize, StrategicCombatRules.PdfTrooperBattleValue);
        }

        public static long ResolveTargetBattleValue(Mission mission, IRNG random)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return mission.TargetBattleValue
                ?? RollTargetBattleValue(mission.MissionSize, random);
        }

        public static long RecommendedMinimumSquads(long targetBattleValue)
        {
            long referenceSquadBattleValue = ReferenceSquadBattleValue;
            long positiveTarget = Math.Max(1L, targetBattleValue);
            long wholeSquads = positiveTarget / referenceSquadBattleValue;
            return Math.Max(
                1L,
                wholeSquads + (positiveTarget % referenceSquadBattleValue == 0 ? 0 : 1));
        }

        public static string FormatRecommendedMinimumForce(Mission mission)
        {
            if (mission?.MissionType != MissionType.Ambush
                || mission.TargetBattleValue is not long targetBattleValue)
            {
                return null;
            }

            long squads = RecommendedMinimumSquads(targetBattleValue);
            string unit = squads == 1 ? "squad" : "squads";
            return $"Recommended Minimum Force: {squads:N0} {unit}";
        }

        private static long SaturatingMultiply(long left, long right)
        {
            if (left <= 0 || right <= 0) return 0;
            return left > long.MaxValue / right ? long.MaxValue : left * right;
        }
    }
}
