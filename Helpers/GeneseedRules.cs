using System;

namespace OnlyWar.Helpers
{
    // Centralized tunables for the chapter's gene-seed (PRD 4.8 / 4.12). Purity is tracked
    // as an aggregate-only quality of the sealed stockpile for now; the recruitment/initiate
    // pipeline that will *consume* it (and read its purity) is post-0.7 (PRD 4.9, open
    // question 6.3). Keeping the numbers here rather than as literals in the resolver/UI.
    public static class GeneseedRules
    {
        // A progenoid's first gland matures five years after implantation; before that its
        // gene-seed is not recoverable. Mirrors the maturity window the Apothecarium uses.
        public const int ProgenoidMaturityWeeks = 5 * 52;

        // The bounds purity is clamped to.
        public const float MinPurity = 0.0f;
        public const float MaxPurity = 1.0f;

        // A freshly founded chapter's gene-seed line is pristine.
        public const float FoundingPurity = MaxPurity;

        // How far a single recovered gland can fall below the founding baseline. Gene-seed
        // drifts as it is recovered and re-cultured, so individual glands vary and the
        // stockpile aggregate edges down over a long campaign rather than sitting flat.
        public const float RecoveredPurityDrift = 0.05f;

        // Rolls the purity of one recovered gland: at most the founding baseline, sometimes
        // a little lower, never above pristine.
        public static float RollRecoveredPurity()
        {
            double roll = RNG.GetDoubleInRange(FoundingPurity - RecoveredPurityDrift, FoundingPurity);
            return (float)Math.Clamp(roll, MinPurity, MaxPurity);
        }
    }
}
