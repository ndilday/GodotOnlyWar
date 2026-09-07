using System;

namespace OnlyWar.Helpers.Database.GameState;

/// <summary>
/// Pure readers for values that older save formats did not persist. These are storage
/// compatibility rules, not live Operations policy.
/// </summary>
internal static class LegacySaveValueMappers
{
    internal static long EstimateLegacyAmbushTargetBattleValue(int missionSize)
    {
        double pdfTrooperEquivalent = Math.Pow(10, Math.Max(1, missionSize) + 0.5);
        long forceSize = pdfTrooperEquivalent >= long.MaxValue
            ? long.MaxValue
            : Math.Max(1L, (long)pdfTrooperEquivalent);
        const long pdfTrooperBattleValue = 5;
        return forceSize > long.MaxValue / pdfTrooperBattleValue
            ? long.MaxValue
            : forceSize * pdfTrooperBattleValue;
    }
}
