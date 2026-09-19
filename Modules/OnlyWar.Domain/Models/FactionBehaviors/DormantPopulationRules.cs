using OnlyWar.Domain;
using System;

namespace OnlyWar.Domain.FactionBehaviors
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
        // PublicGrowthMultiplier (2.0) and DormantGrowthMultiplier (0.10) are gone, along with the
        // GrowthEfficiency helpers that read them. Population growth no longer depends on whether a
        // presence is hiding: see PlanetDemographicsProcessor for why neither figure was reachable.
        public const double ExceptionalAssassinationMargin = 3.0;

        public static double UpdateConsolidation(double current, double zValue) =>
            System.Math.Clamp(current + zValue / WeeklyConsolidationSigmaDivisor
                + WeeklyConsolidationDrift, 0.0, 1.0);

        public static double UpdateConsolidation(FactionBehaviorRulesProfile profile,
            double current, double zValue) =>
            System.Math.Clamp(current + zValue / profile.WeeklyConsolidationSigmaDivisor
                + profile.WeeklyConsolidationDrift, 0.0, 1.0);

        public static double MobilizationFraction(double zValue) =>
            System.Math.Clamp(MobilizationMedian + MobilizationSigma * zValue,
                MobilizationMinimum, MobilizationMaximum);

        public static double MobilizationFraction(FactionBehaviorRulesProfile profile,
            double zValue) =>
            System.Math.Clamp(profile.MobilizationMedian + profile.MobilizationSigma * zValue,
                profile.MobilizationMinimum, profile.MobilizationMaximum);

    }
}
