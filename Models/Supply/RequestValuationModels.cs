using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Supply;

public sealed record ThroughputPremiumBand(long MaximumBattleValuePerWeek, decimal Multiplier);

public sealed record QualificationPremium(string GroupKey, string RequirementKey, decimal Multiplier);

public sealed record RequestValuationResult(
    long EffortBattleValueTime,
    long RequiredBattleValuePerWeek,
    decimal ThroughputMultiplier,
    decimal QualificationMultiplier,
    decimal HazardMultiplier,
    int RequisitionValue);

public sealed class RequestValuationRules
{
    private readonly ThroughputPremiumBand[] _throughputBands;

    public decimal RequisitionPerBattleValueTime { get; }
    public IReadOnlyList<ThroughputPremiumBand> ThroughputBands => _throughputBands;
    public int MinimumRequestValue { get; }
    public int MaximumRequestValue { get; }
    public decimal MaximumCombinedPremium { get; }

    public RequestValuationRules(
        decimal requisitionPerBattleValueTime,
        IEnumerable<ThroughputPremiumBand> throughputBands,
        int minimumRequestValue,
        int maximumRequestValue,
        decimal maximumCombinedPremium = 10m)
    {
        if (requisitionPerBattleValueTime < 0)
            throw new ArgumentOutOfRangeException(nameof(requisitionPerBattleValueTime));
        if (minimumRequestValue < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumRequestValue));
        if (maximumRequestValue < minimumRequestValue)
            throw new ArgumentOutOfRangeException(nameof(maximumRequestValue));
        if (maximumCombinedPremium <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCombinedPremium));

        _throughputBands = throughputBands?.OrderBy(band => band.MaximumBattleValuePerWeek).ToArray()
            ?? throw new ArgumentNullException(nameof(throughputBands));
        if (_throughputBands.Length == 0)
            throw new ArgumentException("At least one throughput premium band is required.", nameof(throughputBands));
        if (_throughputBands.Any(band => band.MaximumBattleValuePerWeek <= 0 || band.Multiplier <= 0))
            throw new ArgumentException("Throughput bands must have positive bounds and multipliers.", nameof(throughputBands));

        RequisitionPerBattleValueTime = requisitionPerBattleValueTime;
        MinimumRequestValue = minimumRequestValue;
        MaximumRequestValue = maximumRequestValue;
        MaximumCombinedPremium = maximumCombinedPremium;
    }
}

public sealed record GovernorWillingness(
    decimal DesperationMultiplier,
    decimal RelationshipMultiplier,
    decimal AuthorityMultiplier);

public sealed record GovernorOfferRules(
    int MinimumOffer,
    int MaximumOffer,
    decimal MinimumWillingnessMultiplier = 0.1m,
    decimal MaximumWillingnessMultiplier = 5m);
