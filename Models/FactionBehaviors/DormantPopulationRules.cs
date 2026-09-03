using OnlyWar.Helpers;
using System;

namespace OnlyWar.Models.FactionBehaviors
{
    /// <summary>Pure rules for consolidation, mobilization, and dormant growth.</summary>
    public static class DormantPopulationRules
    {
        public const double WeeklyConsolidationSigmaDivisor = 100.0;
        public const double WeeklyConsolidationDrift = 0.001;
        public const double MobilizationMedian = 0.60;
        public const double MobilizationSigma = 0.10;
        public const double MobilizationMinimum = 0.25;
        public const double MobilizationMaximum = 0.90;
        public const double PublicGrowthMultiplier = 2.0;
        public const double DormantGrowthMultiplier = 0.10;
        public const double ExceptionalAssassinationMargin = 3.0;

        public static double UpdateConsolidation(double current, double zValue) =>
            Math.Clamp(current + zValue / WeeklyConsolidationSigmaDivisor
                + WeeklyConsolidationDrift, 0.0, 1.0);

        public static double UpdateConsolidation(FactionBehaviorRulesProfile profile,
            double current, double zValue) =>
            Math.Clamp(current + zValue / profile.WeeklyConsolidationSigmaDivisor
                + profile.WeeklyConsolidationDrift, 0.0, 1.0);

        public static double MobilizationFraction(double zValue) =>
            Math.Clamp(MobilizationMedian + MobilizationSigma * zValue,
                MobilizationMinimum, MobilizationMaximum);

        public static double MobilizationFraction(FactionBehaviorRulesProfile profile,
            double zValue) =>
            Math.Clamp(profile.MobilizationMedian + profile.MobilizationSigma * zValue,
                profile.MobilizationMinimum, profile.MobilizationMaximum);

        public static double GrowthEfficiency(bool isPublic) =>
            isPublic ? PublicGrowthMultiplier : DormantGrowthMultiplier;

        public static double GrowthEfficiency(FactionBehaviorRulesProfile profile, bool isPublic) =>
            isPublic ? profile.PublicGrowthMultiplier : profile.DormantGrowthMultiplier;
    }
}
