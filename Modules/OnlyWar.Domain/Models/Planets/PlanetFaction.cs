using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;

namespace OnlyWar.Models.Planets
{
    public class PlanetFaction
    {
        public Faction Faction { get; }
        public bool IsPublic { get; set; }
        public float PlayerReputation { get; set; }
        public int PlanetaryControl { get; set; }
        public Character Leader { get; set; }

        // Target-agnostic understanding of the region. It remains useful for watch, stealth and
        // recon quality, but it is not a precision mask over a target's real population.
        public readonly Dictionary<Region, float> RegionAwareness;

        private readonly Dictionary<FactionIntelKey, FactionIntelBelief> _targetIntel;
        public IReadOnlyDictionary<FactionIntelKey, FactionIntelBelief> TargetIntel { get; }

        public event EventHandler<FactionIntelChangedEventArgs> TargetIntelChanged;

        public PlanetFaction(Faction faction)
        {
            Faction = faction ?? throw new ArgumentNullException(nameof(faction));
            IsPublic = true;
            PlayerReputation = 0;
            PlanetaryControl = 0;
            RegionAwareness = new Dictionary<Region, float>();
            _targetIntel = new Dictionary<FactionIntelKey, FactionIntelBelief>();
            TargetIntel = new ReadOnlyDictionary<FactionIntelKey, FactionIntelBelief>(_targetIntel);
        }

        public float GetRegionAwareness(Region region) =>
            region != null && RegionAwareness.TryGetValue(region, out float level) ? level : 0f;

        public void AddRegionAwareness(Region region, float amount)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            if (!float.IsFinite(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
            SetRegionAwareness(region, Math.Max(0f, GetRegionAwareness(region) + amount));
        }

        public void SetRegionAwareness(Region region, float level)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            if (!float.IsFinite(level)) throw new ArgumentOutOfRangeException(nameof(level));
            if (level <= 0f) RegionAwareness.Remove(region);
            else RegionAwareness[region] = level;
        }

        public FactionIntelBelief GetTargetBelief(Region region, Faction targetFaction)
        {
            if (region == null || targetFaction == null) return null;
            return _targetIntel.GetValueOrDefault(new FactionIntelKey(region, targetFaction));
        }

        /// <summary>
        /// Applies one transient report. Allied fan-out is intentionally performed by
        /// <see cref="FactionIntelligenceService"/> so copied reports cannot recursively echo.
        /// </summary>
        public FactionIntelBelief ApplyObservation(IntelObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (!ReferenceEquals(observation.Observer, this))
            {
                throw new InvalidOperationException("An observation can only mutate its owning observer.");
            }
            if (observation.Region.Planet?.PlanetFactionMap.TryGetValue(Faction.Id, out PlanetFaction owner) == true
                && !ReferenceEquals(owner, this))
            {
                throw new InvalidOperationException("The observation observer is not attached to the region's planet.");
            }
            if (observation.BelievedTarget.Id == Faction.Id)
            {
                throw new InvalidOperationException("A faction cannot hold target intelligence about itself.");
            }

            FactionIntelKey key = new(observation.Region, observation.BelievedTarget);
            _targetIntel.TryGetValue(key, out FactionIntelBelief previous);
            float beforeEvidence = previous?.Evidence ?? 0f;
            IntelLevel beforeLevel = previous?.Level ?? IntelLevel.None;
            float evidence = FactionIntelligenceRules.ClampEvidence(
                beforeEvidence + observation.EvidenceDelta);
            if (evidence < FactionIntelligenceRules.RumorThreshold)
            {
                _targetIntel.Remove(key);
                RaiseIntelChanged(observation, previous, null, beforeLevel);
                return null;
            }

            long? estimatedPopulation = previous?.EstimatedPopulation;
            long? estimatedMilitaryStrength = previous?.EstimatedMilitaryStrength;
            if (observation.EvidenceDelta > 0f)
            {
                estimatedPopulation = BlendEstimate(
                    previous?.EstimatedPopulation,
                    observation.EstimatedPopulation,
                    beforeEvidence,
                    observation.EvidenceDelta);
                estimatedMilitaryStrength = BlendEstimate(
                    previous?.EstimatedMilitaryStrength,
                    observation.EstimatedMilitaryStrength,
                    beforeEvidence,
                    observation.EvidenceDelta);
            }

            int lastEvidenceWeek = previous?.LastEvidenceWeek ?? observation.EvidenceWeek;
            if (observation.EvidenceDelta > 0f)
            {
                lastEvidenceWeek = Math.Max(lastEvidenceWeek, observation.EvidenceWeek);
            }

            FactionIntelBelief current = new(
                observation.Region,
                observation.BelievedTarget,
                evidence,
                estimatedPopulation,
                estimatedMilitaryStrength,
                lastEvidenceWeek);
            _targetIntel[key] = current;
            RaiseIntelChanged(observation, previous, current, beforeLevel);
            return current;
        }

        public FactionIntelBelief SeedTargetBelief(
            Region region,
            Faction targetFaction,
            float evidence,
            long? estimatedPopulation,
            long? estimatedMilitaryStrength,
            int evidenceWeek,
            IntelObservationSource source = IntelObservationSource.Scenario)
        {
            return ApplyObservation(new IntelObservation(
                this,
                region,
                targetFaction,
                evidence,
                estimatedPopulation,
                estimatedMilitaryStrength,
                source,
                evidenceWeek));
        }

        public void DecayTargetBeliefs()
        {
            foreach (KeyValuePair<FactionIntelKey, FactionIntelBelief> entry in _targetIntel.ToList())
            {
                float evidence = FactionIntelligenceRules.DecayEvidence(entry.Value.Evidence);
                IntelObservation decay = new(
                    this,
                    entry.Value.Region,
                    entry.Value.TargetFaction,
                    evidence - entry.Value.Evidence,
                    entry.Value.EstimatedPopulation,
                    entry.Value.EstimatedMilitaryStrength,
                    IntelObservationSource.Decay,
                    entry.Value.LastEvidenceWeek);
                if (evidence < FactionIntelligenceRules.RumorThreshold)
                {
                    _targetIntel.Remove(entry.Key);
                    RaiseIntelChanged(
                        decay,
                        entry.Value,
                        null,
                        entry.Value.Level);
                    continue;
                }

                FactionIntelBelief current = new FactionIntelBelief(
                    entry.Value.Region,
                    entry.Value.TargetFaction,
                    evidence,
                    entry.Value.EstimatedPopulation,
                    entry.Value.EstimatedMilitaryStrength,
                    entry.Value.LastEvidenceWeek);
                _targetIntel[entry.Key] = current;
                RaiseIntelChanged(decay, entry.Value, current, entry.Value.Level);
            }
        }

        public bool HasIntelligenceFootprint =>
            RegionAwareness.Count > 0 || _targetIntel.Count > 0;

        private static long? BlendEstimate(
            long? existing,
            long? incoming,
            float existingEvidence,
            float incomingEvidence)
        {
            if (!incoming.HasValue) return existing;
            if (!existing.HasValue || existingEvidence <= 0f) return incoming;
            return (long)Math.Round(
                (existing.Value * existingEvidence + incoming.Value * incomingEvidence)
                    / (existingEvidence + incomingEvidence),
                MidpointRounding.AwayFromZero);
        }

        private void RaiseIntelChanged(
            IntelObservation observation,
            FactionIntelBelief previous,
            FactionIntelBelief current,
            IntelLevel previousLevel)
        {
            TargetIntelChanged?.Invoke(
                this,
                new FactionIntelChangedEventArgs(
                    observation,
                    previous,
                    current,
                    previousLevel,
                    current?.Level ?? IntelLevel.None));
        }
    }

    public sealed class FactionIntelChangedEventArgs : EventArgs
    {
        public IntelObservation Observation { get; }
        public FactionIntelBelief Previous { get; }
        public FactionIntelBelief Current { get; }
        public IntelLevel PreviousLevel { get; }
        public IntelLevel CurrentLevel { get; }

        internal FactionIntelChangedEventArgs(
            IntelObservation observation,
            FactionIntelBelief previous,
            FactionIntelBelief current,
            IntelLevel previousLevel,
            IntelLevel currentLevel)
        {
            Observation = observation;
            Previous = previous;
            Current = current;
            PreviousLevel = previousLevel;
            CurrentLevel = currentLevel;
        }
    }
}
