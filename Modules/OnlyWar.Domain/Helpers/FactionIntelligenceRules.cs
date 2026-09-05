using System;
using OnlyWar.Models;

namespace OnlyWar.Helpers
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
                ? Math.Clamp(evidence, 0f, MaxEvidence)
                : throw new ArgumentOutOfRangeException(nameof(evidence));

        public static float DecayEvidence(float evidence) =>
            ClampEvidence(evidence * WeeklyDecayMultiplier);
    }
}
