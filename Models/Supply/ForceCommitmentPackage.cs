using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Supply;

/// <summary>
/// A player-readable force commitment. Battle value is retained only as the hidden
/// reference strength used to price the package and is not intended for presentation.
/// </summary>
public sealed class ForceCommitmentPackage
{
    private readonly string[] _qualificationTags;

    public string Key { get; }
    public string DisplayName { get; }
    public string DisplayUnitName { get; }
    public int PackageCount { get; }
    public int ServiceWeeks { get; }
    public int CompletionDeadlineWeeks { get; }
    public int MaximumEffectivePackageCount { get; }
    public long ReferenceBattleValuePerPackage { get; }
    public IReadOnlyList<string> QualificationTags => _qualificationTags;

    public ForceCommitmentPackage(
        string key,
        string displayName,
        string displayUnitName,
        int packageCount,
        int serviceWeeks,
        int completionDeadlineWeeks,
        long referenceBattleValuePerPackage,
        IEnumerable<string> qualificationTags = null,
        int maximumEffectivePackageCount = 0)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A commitment package key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A commitment package display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(displayUnitName))
            throw new ArgumentException("A commitment package display unit is required.", nameof(displayUnitName));
        if (packageCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(packageCount));
        if (serviceWeeks <= 0)
            throw new ArgumentOutOfRangeException(nameof(serviceWeeks));
        if (completionDeadlineWeeks <= 0)
            throw new ArgumentOutOfRangeException(nameof(completionDeadlineWeeks));
        if (referenceBattleValuePerPackage <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceBattleValuePerPackage));
        if (maximumEffectivePackageCount == 0) maximumEffectivePackageCount = packageCount;
        if (maximumEffectivePackageCount < packageCount)
            throw new ArgumentOutOfRangeException(nameof(maximumEffectivePackageCount));

        Key = key;
        DisplayName = displayName;
        DisplayUnitName = displayUnitName;
        PackageCount = packageCount;
        ServiceWeeks = serviceWeeks;
        CompletionDeadlineWeeks = completionDeadlineWeeks;
        ReferenceBattleValuePerPackage = referenceBattleValuePerPackage;
        MaximumEffectivePackageCount = maximumEffectivePackageCount;
        _qualificationTags = qualificationTags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
    }
}
