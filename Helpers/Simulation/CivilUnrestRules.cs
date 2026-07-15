using System;

namespace OnlyWar.Helpers.Simulation;

/// <summary>
/// Pure calculations for regional contentment and secular unrest. This class deliberately
/// has no dependency on the turn processor or persisted models, so simulation orchestration
/// can choose how and when to apply the calculated transfers.
/// </summary>
public static class CivilUnrestRules
{
    public const double MinimumContentment = 0.0;
    public const double MaximumContentment = 100.0;

    public const double BaseContentment = 70.0;
    public const double MaximumTaxPenalty = 25.0;
    public const double CompetenceRange = 20.0;
    public const double SeverityPenaltyStart = 0.4;
    public const double MaximumSeverityPenalty = 10.0;

    public const double WeeklyContentmentDriftRate = 0.03;
    public const double FullSecurityPdfShare = 0.03;
    public const double MaximumSecurityBenefit = 10.0;
    public const double OvercrowdingPenaltyStart = 0.9;
    public const double OvercrowdingPenaltyPerCapacityRatio = 50.0;
    public const double MaximumOvercrowdingPenalty = 15.0;

    public const double UnrestContentmentThreshold = 55.0;
    public const double MaximumTargetUnrestShare = 0.30;
    public const double UnrestShareExponent = 1.5;
    public const double WeeklyAllegianceGapClosingRate = 0.05;
    public const double MinimumArmedCivilianFraction = 0.10;
    public const double AdditionalArmedCivilianFraction = 0.40;
    public const double PdfInfiltrationWeight = 0.70;

    public const double PublicRevoltStrengthRatio = 2.0;
    public const double ReturnToHidingStrengthRatio = 0.5;
    public const double WeeklyMigrationRate = 0.05;

    /// <summary>
    /// Calculates the long-run structural contentment before local security and crowding.
    /// Tax burden, competence, and severity are normalized to the inclusive range 0..1.
    /// Severity only imposes a structural penalty above <see cref="SeverityPenaltyStart"/>.
    /// </summary>
    public static double CalculateStructuralBaseline(
        double normalizedTaxBurden,
        double normalizedCompetence,
        double normalizedSeverity)
    {
        double tax = Clamp01(normalizedTaxBurden);
        double competence = Clamp01(normalizedCompetence);
        double severity = Clamp01(normalizedSeverity);
        double severityRange = 1.0 - SeverityPenaltyStart;
        double highSeverity = Math.Max(0.0, severity - SeverityPenaltyStart) / severityRange;

        return ClampContentment(
            BaseContentment
            - MaximumTaxPenalty * tax
            + CompetenceRange * (competence - 0.5)
            - MaximumSeverityPenalty * highSeverity * highSeverity);
    }

    /// <summary>
    /// Calculates the benefit provided by loyal PDF personnel. Security saturates when the
    /// loyal PDF reaches three percent of the loyal civilian population; additional troops do
    /// not make the population more content.
    /// </summary>
    public static double CalculateSecurityBenefit(double loyalPdf, double loyalPopulation)
    {
        if (loyalPdf <= 0.0 || loyalPopulation <= 0.0)
            return 0.0;

        double fullCoverage = loyalPopulation * FullSecurityPdfShare;
        return MaximumSecurityBenefit * Math.Clamp(loyalPdf / fullCoverage, 0.0, 1.0);
    }

    public static double CalculateOvercrowdingPenalty(double population, double capacity)
    {
        if (population <= 0.0 || capacity <= 0.0)
            return 0.0;

        double excessRatio = Math.Max(0.0, population / capacity - OvercrowdingPenaltyStart);
        return Math.Min(MaximumOvercrowdingPenalty, OvercrowdingPenaltyPerCapacityRatio * excessRatio);
    }

    public static double CalculateContentmentTarget(
        double structuralBaseline,
        double loyalPdf,
        double loyalPopulation,
        double totalPopulation,
        double capacity)
    {
        return ClampContentment(
            structuralBaseline
            + CalculateSecurityBenefit(loyalPdf, loyalPopulation)
            - CalculateOvercrowdingPenalty(totalPopulation, capacity));
    }

    public static double DriftContentment(double currentContentment, double targetContentment)
    {
        double current = ClampContentment(currentContentment);
        double target = ClampContentment(targetContentment);
        return ClampContentment(current + WeeklyContentmentDriftRate * (target - current));
    }

    public static double CalculateTargetUnrestShare(double contentment)
    {
        double deficit = CalculateUnrestDeficit(contentment);
        return MaximumTargetUnrestShare * Math.Pow(deficit, UnrestShareExponent);
    }

    public static double CalculateTargetArmedCivilianFraction(double contentment)
    {
        double deficit = CalculateUnrestDeficit(contentment);
        return MinimumArmedCivilianFraction + AdditionalArmedCivilianFraction * deficit;
    }

    /// <summary>
    /// Returns next week's unrest-aligned population share after closing five percent of the
    /// gap in either direction. Shares are clamped to 0..1, while the target is normally supplied
    /// by <see cref="CalculateTargetUnrestShare"/>.
    /// </summary>
    public static double DriftUnrestShare(double currentShare, double targetShare)
    {
        double current = Clamp01(currentShare);
        double target = Clamp01(targetShare);
        return Clamp01(current + WeeklyAllegianceGapClosingRate * (target - current));
    }

    /// <summary>
    /// Probability that a newly recruited revolutionary comes from the loyal PDF rather than
    /// the civilian population. PDF members have 0.7 of a civilian's susceptibility.
    /// </summary>
    public static double CalculatePdfRecruitSelectionChance(double loyalPdf, double loyalCivilians)
    {
        double weightedPdf = Math.Max(0.0, loyalPdf) * PdfInfiltrationWeight;
        double civilians = Math.Max(0.0, loyalCivilians);
        double totalWeight = weightedPdf + civilians;
        return totalWeight <= 0.0 ? 0.0 : weightedPdf / totalWeight;
    }

    public static bool ShouldGoPublic(
        double rebelMilitaryStrength,
        double loyalAndAlliedLocalStrength,
        bool hasPublicExternalEnemy)
    {
        if (hasPublicExternalEnemy || rebelMilitaryStrength <= 0.0)
            return false;

        double loyalStrength = Math.Max(0.0, loyalAndAlliedLocalStrength);
        return rebelMilitaryStrength >= PublicRevoltStrengthRatio * loyalStrength;
    }

    public static bool ShouldReturnToHiding(
        double rebelMilitaryStrength,
        double loyalAndAlliedLocalStrength)
    {
        double rebelStrength = Math.Max(0.0, rebelMilitaryStrength);
        double loyalStrength = Math.Max(0.0, loyalAndAlliedLocalStrength);
        return rebelStrength < ReturnToHidingStrengthRatio * loyalStrength;
    }

    /// <summary>
    /// Calculates the population available to move from a hidden cell toward a public revolt
    /// during one weekly migration step. Routing and integer rounding belong to orchestration.
    /// </summary>
    public static double CalculateWeeklyMigration(double eligibleHiddenUnrestPopulation) =>
        Math.Max(0.0, eligibleHiddenUnrestPopulation) * WeeklyMigrationRate;

    private static double CalculateUnrestDeficit(double contentment) =>
        Math.Clamp((UnrestContentmentThreshold - ClampContentment(contentment)) / UnrestContentmentThreshold, 0.0, 1.0);

    private static double ClampContentment(double value) =>
        Math.Clamp(value, MinimumContentment, MaximumContentment);

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
