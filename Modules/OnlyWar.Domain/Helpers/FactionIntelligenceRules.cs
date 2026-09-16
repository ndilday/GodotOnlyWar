using System;
using OnlyWar.Domain;

namespace OnlyWar.Domain.Intelligence
{
    public static class FactionIntelligenceRules
    {
        public const float RumorThreshold = 0.25f;
        public const float SuspectedThreshold = 1f;
        public const float ConfirmedThreshold = 3f;
        public const float LocatedThreshold = 6f;
        public const float MaxEvidence = 12f;
        public const float WeeklyDecayMultiplier = 0.75f;

        public static IntelLevel GetLevel(float evidence)
        {
            if (!float.IsFinite(evidence) || evidence < RumorThreshold) return IntelLevel.None;
            if (evidence < SuspectedThreshold) return IntelLevel.Rumor;
            if (evidence < ConfirmedThreshold) return IntelLevel.Suspected;
            if (evidence < LocatedThreshold) return IntelLevel.Confirmed;
            return IntelLevel.Located;
        }

        public static float ClampEvidence(float evidence) =>
            float.IsFinite(evidence)
                ? System.Math.Clamp(evidence, 0f, MaxEvidence)
                : throw new ArgumentOutOfRangeException(nameof(evidence));

        public static float DecayEvidence(float evidence) =>
            ClampEvidence(evidence * WeeklyDecayMultiplier);

        /// <summary>
        /// Coarsens an observed headcount to the precision the observer's awareness supports, always
        /// rounding UP: no significant figures at all when it knows nothing of the ground, one more
        /// for each point of awareness, and the exact number once its precision covers the value.
        /// </summary>
        /// <remarks>
        /// Rounding up is what makes this an upper bound rather than a guess, and that is the whole
        /// design: an observer never believes the enemy is weaker than it is, so pessimism comes out
        /// of the measurement itself instead of a separate hedge multiplied on afterwards. A garrison
        /// of 869 reads as 1,000 to a faction that has never looked, 900 after one point of
        /// awareness, 870 after two, and exactly 869 after three.
        ///
        /// Two earlier versions were worse. Flooring to an ABSOLUTE power of ten annihilated
        /// anything smaller than the divisor, so every unscouted region read as holding one soldier.
        /// Rounding to nearest fixed that but let the estimate fall below the truth, which meant
        /// caution had to be reintroduced as a hedge - and a hedge that decayed on a completely
        /// different curve from the measurement it was correcting.
        ///
        /// The overstatement is not uniform: it depends where the value sits inside its decade. At
        /// zero digits a force of 999 reads as 1,000 and a force of 101 also reads as 1,000. That is
        /// deliberate - it is knowledge of the ceiling of a decade, not a uniform fog - and the
        /// decision path never consults it while blind anyway, because CautiousDefenderEstimate
        /// falls back on the population prior until the region has been scouted.
        /// </remarks>
        public static long? CoarsenEstimate(long value, float awareness)
        {
            if (value < 0) return null;
            if (value == 0) return 0;
            if (!float.IsFinite(awareness)) awareness = 0f;

            int significantDigits = (int)System.Math.Floor(System.Math.Max(0f, awareness));

            // At zero significant figures the only representable values are the powers of ten, so
            // this is "the next power of ten at or above". Computed by multiplication rather than
            // from a digit count: 1,000 is already representable and must stay 1,000 rather than
            // jumping a whole decade to 10,000.
            if (significantDigits == 0)
            {
                long power = 1L;
                while (power < value) power *= 10L;
                return power;
            }

            // Digit count by division rather than Math.Log10, which is not exact at powers of ten.
            int digitCount = 0;
            for (long remaining = value; remaining > 0; remaining /= 10L) digitCount++;
            if (significantDigits >= digitCount) return value;

            long scale = 1L;
            for (int i = 0; i < digitCount - significantDigits; i++) scale *= 10L;
            return (value + scale - 1L) / scale * scale;
        }
    }
}
