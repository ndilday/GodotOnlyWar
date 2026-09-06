using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Supply;

/// <summary>
/// Prices a player-readable commitment using Battle-Value-Time as an internal accounting unit.
/// </summary>
public static class RequestValueCalculator
{
    public static RequestValuationResult Calculate(
        ForceCommitmentPackage commitment,
        RequestValuationRules rules,
        IEnumerable<QualificationPremium> qualificationPremiums,
        decimal hazardMultiplier)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(rules);
        if (hazardMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(hazardMultiplier));

        long effort = SaturatingMultiply(
            SaturatingMultiply(commitment.ReferenceBattleValuePerPackage, commitment.PackageCount),
            commitment.ServiceWeeks);
        long throughput = DivideRoundingUp(effort, commitment.CompletionDeadlineWeeks);
        decimal throughputMultiplier = SelectThroughputMultiplier(throughput, rules.ThroughputBands);
        decimal qualificationMultiplier = CalculateQualificationMultiplier(qualificationPremiums);
        decimal combinedPremium = SaturatingMultiply(
            SaturatingMultiply(throughputMultiplier, qualificationMultiplier),
            hazardMultiplier);
        combinedPremium = Math.Min(combinedPremium, rules.MaximumCombinedPremium);

        decimal rawValue = SaturatingMultiply(
            SaturatingMultiply(effort, rules.RequisitionPerBattleValueTime),
            combinedPremium);
        int requisitionValue = RoundAndClamp(
            rawValue,
            rules.MinimumRequestValue,
            rules.MaximumRequestValue);

        return new RequestValuationResult(
            effort,
            throughput,
            throughputMultiplier,
            qualificationMultiplier,
            hazardMultiplier,
            requisitionValue);
    }

    private static decimal SelectThroughputMultiplier(
        long requiredBattleValuePerWeek,
        IReadOnlyList<ThroughputPremiumBand> bands)
    {
        foreach (ThroughputPremiumBand band in bands)
        {
            if (requiredBattleValuePerWeek <= band.MaximumBattleValuePerWeek)
                return band.Multiplier;
        }

        return bands[^1].Multiplier;
    }

    private static decimal CalculateQualificationMultiplier(
        IEnumerable<QualificationPremium> qualificationPremiums)
    {
        if (qualificationPremiums == null)
            return 1m;

        decimal result = 1m;
        foreach (IGrouping<string, QualificationPremium> group in qualificationPremiums
                     .Where(premium => premium != null && premium.Multiplier > 0)
                     .GroupBy(premium => premium.GroupKey ?? "", StringComparer.OrdinalIgnoreCase))
        {
            result = SaturatingMultiply(result, group.Max(premium => premium.Multiplier));
        }

        return result;
    }

    private static long DivideRoundingUp(long value, int divisor)
    {
        if (value == long.MaxValue)
            return long.MaxValue;

        return value / divisor + (value % divisor == 0 ? 0 : 1);
    }

    private static long SaturatingMultiply(long left, int right)
    {
        if (left > long.MaxValue / right)
            return long.MaxValue;
        return left * right;
    }

    private static decimal SaturatingMultiply(long left, decimal right)
    {
        try
        {
            return left * right;
        }
        catch (OverflowException)
        {
            return decimal.MaxValue;
        }
    }

    private static decimal SaturatingMultiply(decimal left, decimal right)
    {
        try
        {
            return left * right;
        }
        catch (OverflowException)
        {
            return decimal.MaxValue;
        }
    }

    internal static int RoundAndClamp(decimal value, int minimum, int maximum)
    {
        if (value <= minimum)
            return minimum;
        if (value >= maximum)
            return maximum;

        decimal rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, minimum, maximum);
    }
}
