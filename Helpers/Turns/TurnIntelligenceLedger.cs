using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Turn-scoped intelligence inputs. Awareness and target observations are applied only during
    /// the planetary intelligence phase, after the preceding simulation phases have settled.
    /// </summary>
    internal sealed class TurnIntelligenceLedger
    {
        internal const float ReconEvidenceSoftCap = 6f;

        private readonly Dictionary<PlanetFaction, Dictionary<Region, float>> _gains = new();
        private readonly Dictionary<PlanetFaction, Dictionary<Region, ReconEvidence>> _reconEvidence = new();
        private readonly List<IntelObservation> _observations = new();

        internal int MaterializedAwarenessRows { get; private set; }
        internal int MaterializedBeliefRows { get; private set; }
        internal int ObservationsApplied { get; private set; }
        internal int AlliedCopies { get; private set; }

        private sealed class ReconEvidence
        {
            internal float Positive { get; private set; }
            internal float Negative { get; private set; }

            internal void Add(float evidence)
            {
                if (evidence > 0f) Positive += evidence;
                else if (evidence < 0f) Negative += -evidence;
            }

            internal void Add(ReconEvidence other)
            {
                Positive += other.Positive;
                Negative += other.Negative;
            }
        }

        internal void Clear()
        {
            _gains.Clear();
            _reconEvidence.Clear();
            _observations.Clear();
            MaterializedAwarenessRows = 0;
            MaterializedBeliefRows = 0;
            ObservationsApplied = 0;
            AlliedCopies = 0;
        }

        internal void RecordGain(PlanetFaction planetFaction, Region region, float gain)
        {
            if (planetFaction == null || region == null || gain <= 0f) return;
            if (!_gains.TryGetValue(planetFaction, out Dictionary<Region, float> factionGains))
            {
                factionGains = new Dictionary<Region, float>();
                _gains[planetFaction] = factionGains;
            }
            factionGains[region] = factionGains.TryGetValue(region, out float existing)
                ? existing + gain
                : gain;
        }

        internal void RecordReconEvidence(PlanetFaction planetFaction, Region region, float evidence)
        {
            if (planetFaction == null || region == null || evidence == 0f) return;
            if (!_reconEvidence.TryGetValue(
                planetFaction,
                out Dictionary<Region, ReconEvidence> factionEvidence))
            {
                factionEvidence = new Dictionary<Region, ReconEvidence>();
                _reconEvidence[planetFaction] = factionEvidence;
            }
            if (!factionEvidence.TryGetValue(region, out ReconEvidence regionEvidence))
            {
                regionEvidence = new ReconEvidence();
                factionEvidence[region] = regionEvidence;
            }
            regionEvidence.Add(evidence);
        }

        internal void RecordObservation(IntelObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            _observations.Add(observation);
        }

        internal bool HasPendingEntries(PlanetFaction planetFaction, Planet planet)
        {
            if (planetFaction == null || planet == null) return false;
            return (_gains.TryGetValue(planetFaction, out Dictionary<Region, float> gains)
                    && gains.Keys.Any(region => ReferenceEquals(region.Planet, planet)))
                || (_reconEvidence.TryGetValue(
                        planetFaction,
                        out Dictionary<Region, ReconEvidence> evidence)
                    && evidence.Keys.Any(region => ReferenceEquals(region.Planet, planet)))
                || _observations.Any(observation =>
                    ReferenceEquals(observation.Observer, planetFaction)
                    && ReferenceEquals(observation.Region.Planet, planet));
        }

        internal void Apply(Planet planet)
        {
            if (planet == null) throw new ArgumentNullException(nameof(planet));
            if (_gains.Count == 0 && _reconEvidence.Count == 0 && _observations.Count == 0) return;

            foreach (KeyValuePair<PlanetFaction, Dictionary<Region, float>> factionEntry in
                _gains.OrderBy(entry => entry.Key.Faction.Id).ToList())
            {
                bool attached = planet.PlanetFactionMap.TryGetValue(
                    factionEntry.Key.Faction.Id,
                    out PlanetFaction present)
                    && ReferenceEquals(present, factionEntry.Key);
                if (attached)
                {
                    foreach (KeyValuePair<Region, float> gain in factionEntry.Value
                        .Where(entry => ReferenceEquals(entry.Key.Planet, planet))
                        .OrderBy(entry => entry.Key.Id))
                    {
                        FactionIntelligenceService.ApplyAwarenessGain(
                            planet,
                            factionEntry.Key,
                            gain.Key,
                            gain.Value);
                    }
                }
            }

            // Combine recon evidence per recipient before applying the diminishing-returns curve.
            // A report can reach only the original observer and its current direct allies; copied
            // reports are not reintroduced as sources, so sharing is one-pass.
            foreach (Region region in planet.Regions
                .Where(region => region != null)
                .OrderBy(region => region.Id))
            {
                List<KeyValuePair<PlanetFaction, ReconEvidence>> sources = _reconEvidence
                    .Where(entry => entry.Value.ContainsKey(region))
                    .OrderBy(entry => entry.Key.Faction.Id)
                    .Select(entry => new KeyValuePair<PlanetFaction, ReconEvidence>(
                        entry.Key,
                        entry.Value[region]))
                    .ToList();
                foreach (PlanetFaction source in sources.Select(entry => entry.Key))
                {
                    foreach (Faction ally in planet.RelationshipLedger.KnownFactions.Values
                        .Where(faction => faction.Id != source.Faction.Id
                            && FactionRelationshipService.AreAllied(
                                source.Faction,
                                faction,
                                planet))
                        .OrderBy(faction => faction.Id)
                        .ToList())
                    {
                        if (!planet.PlanetFactionMap.ContainsKey(ally.Id))
                        {
                            PlanetFaction materialized = new PlanetFaction(ally)
                            {
                                IsPublic = false
                            };
                            planet.PlanetFactionMap[ally.Id] = materialized;
                            planet.NotifyPlanetFactionAdded(materialized);
                        }
                    }
                }
            foreach (PlanetFaction recipient in planet.PlanetFactionMap.Values
                    .OrderBy(planetFaction => planetFaction.Faction.Id))
                {
                    ReconEvidence aggregate = new();
                    foreach (KeyValuePair<PlanetFaction, ReconEvidence> source in sources)
                    {
                        if (source.Key != recipient
                            && !FactionRelationshipService.AreAllied(
                                source.Key.Faction,
                                recipient.Faction,
                                planet))
                        {
                            continue;
                        }
                        aggregate.Add(source.Value);
                    }
                    if (aggregate.Positive > 0f || aggregate.Negative > 0f)
                    {
                        recipient.AddRegionAwareness(
                            region,
                            CalculateReconAdjustment(aggregate.Positive, aggregate.Negative));
                    }
                }
            }

            foreach (IntelObservation observation in _observations
                .Where(observation => ReferenceEquals(observation.Region.Planet, planet))
                .OrderBy(observation => observation.Observer.Faction.Id)
                .ThenBy(observation => observation.Region.Id)
                .ThenBy(observation => observation.BelievedTarget.Id))
            {
                IReadOnlyList<FactionIntelBelief> applied =
                    FactionIntelligenceService.ApplyObservation(planet, observation);
                ObservationsApplied++;
                MaterializedBeliefRows += applied.Count;
                AlliedCopies += Math.Max(0, applied.Count - 1);
            }

            MaterializedAwarenessRows = planet.PlanetFactionMap.Values
                .Sum(planetFaction => planetFaction.RegionAwareness.Count);
            _gains.Clear();
            _reconEvidence.Clear();
            _observations.RemoveAll(observation => ReferenceEquals(observation.Region.Planet, planet));
        }

        internal static float CalculateReconAdjustment(float positiveEvidence, float negativeEvidence) =>
            DiminishEvidence(Math.Max(0f, positiveEvidence))
            - DiminishEvidence(Math.Max(0f, negativeEvidence));

        internal static float DiminishEvidence(float evidence) =>
            ReconEvidenceSoftCap
            * (1f - (float)Math.Exp(-Math.Max(0f, evidence) / ReconEvidenceSoftCap));
    }
}
