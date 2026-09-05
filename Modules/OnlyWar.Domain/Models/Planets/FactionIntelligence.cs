using System;
using OnlyWar.Helpers;
using OnlyWar.Models;

namespace OnlyWar.Models.Planets
{
    public readonly record struct FactionIntelKey
    {
        public int RegionId { get; }
        public int TargetFactionId { get; }

        public FactionIntelKey(Region region, Faction targetFaction)
        {
            RegionId = region?.Id ?? throw new ArgumentNullException(nameof(region));
            TargetFactionId = targetFaction?.Id ?? throw new ArgumentNullException(nameof(targetFaction));
        }

        public FactionIntelKey(int regionId, int targetFactionId)
        {
            if (regionId < 0) throw new ArgumentOutOfRangeException(nameof(regionId));
            if (targetFactionId < 0) throw new ArgumentOutOfRangeException(nameof(targetFactionId));
            RegionId = regionId;
            TargetFactionId = targetFactionId;
        }
    }

    /// <summary>
    /// A belief owned by its observer's PlanetFaction. Estimates are snapshots of the observer's
    /// assessment; this object never reads a RegionFaction or otherwise consults ground truth.
    /// </summary>
    public sealed class FactionIntelBelief
    {
        public Region Region { get; }
        public Faction TargetFaction { get; }
        public float Evidence { get; }
        public IntelLevel Level { get; }
        public long? EstimatedPopulation { get; }
        public long? EstimatedMilitaryStrength { get; }
        public int LastEvidenceWeek { get; }

        public FactionIntelBelief(
            Region region,
            Faction targetFaction,
            float evidence,
            long? estimatedPopulation,
            long? estimatedMilitaryStrength,
            int lastEvidenceWeek)
        {
            Region = region ?? throw new ArgumentNullException(nameof(region));
            TargetFaction = targetFaction ?? throw new ArgumentNullException(nameof(targetFaction));
            if (!float.IsFinite(evidence) || evidence < 0f || evidence > FactionIntelligenceRules.MaxEvidence)
            {
                throw new ArgumentOutOfRangeException(nameof(evidence));
            }
            if (estimatedPopulation is < 0)
                throw new ArgumentOutOfRangeException(nameof(estimatedPopulation));
            if (estimatedMilitaryStrength is < 0)
                throw new ArgumentOutOfRangeException(nameof(estimatedMilitaryStrength));
            if (lastEvidenceWeek < 0)
                throw new ArgumentOutOfRangeException(nameof(lastEvidenceWeek));

            Evidence = evidence;
            Level = FactionIntelligenceRules.GetLevel(evidence);
            if (Level == IntelLevel.None)
            {
                throw new ArgumentException("A materialized belief must have at least Rumor evidence.", nameof(evidence));
            }
            EstimatedPopulation = estimatedPopulation;
            EstimatedMilitaryStrength = estimatedMilitaryStrength;
            LastEvidenceWeek = lastEvidenceWeek;
        }

        internal FactionIntelBelief With(
            float evidence,
            long? estimatedPopulation,
            long? estimatedMilitaryStrength,
            int lastEvidenceWeek) =>
            new(
                Region,
                TargetFaction,
                evidence,
                estimatedPopulation,
                estimatedMilitaryStrength,
                lastEvidenceWeek);
    }

    /// <summary>
    /// A transient report. It is the only runtime input accepted by the target-belief mutation
    /// boundary; the observation itself is never persisted.
    /// </summary>
    public sealed record IntelObservation
    {
        public PlanetFaction Observer { get; }
        public Region Region { get; }
        public Faction BelievedTarget { get; }
        public float EvidenceDelta { get; }
        public long? EstimatedPopulation { get; }
        public long? EstimatedMilitaryStrength { get; }
        public IntelObservationSource Source { get; }
        public int EvidenceWeek { get; }

        public IntelObservation(
            PlanetFaction observer,
            Region region,
            Faction believedTarget,
            float evidenceDelta,
            long? estimatedPopulation,
            long? estimatedMilitaryStrength,
            IntelObservationSource source,
            int evidenceWeek)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            if (region == null) throw new ArgumentNullException(nameof(region));
            if (believedTarget == null) throw new ArgumentNullException(nameof(believedTarget));
            if (!float.IsFinite(evidenceDelta) || evidenceDelta == 0f)
                throw new ArgumentOutOfRangeException(nameof(evidenceDelta));
            if (estimatedPopulation is < 0)
                throw new ArgumentOutOfRangeException(nameof(estimatedPopulation));
            if (estimatedMilitaryStrength is < 0)
                throw new ArgumentOutOfRangeException(nameof(estimatedMilitaryStrength));
            if (evidenceWeek < 0)
                throw new ArgumentOutOfRangeException(nameof(evidenceWeek));

            Observer = observer;
            Region = region;
            BelievedTarget = believedTarget;
            EvidenceDelta = evidenceDelta;
            EstimatedPopulation = estimatedPopulation;
            EstimatedMilitaryStrength = estimatedMilitaryStrength;
            Source = source;
            EvidenceWeek = evidenceWeek;
        }
    }
}
