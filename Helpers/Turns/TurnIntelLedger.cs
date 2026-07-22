using System;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Collects situational-awareness gains produced throughout a turn and applies them at the
    /// planetary intel phase. Keeping the gains in one ledger preserves the existing behavior in
    /// which mission recon, listening posts, and patrols are accumulated before allied Imperial
    /// and player factions receive the pooled result.
    /// </summary>
    internal sealed class TurnIntelLedger
    {
        // Six points already marks full population intelligence in the player UI. Treat that as
        // the maximum useful amount of fresh recon evidence that can be assimilated in one region
        // during one week, approached smoothly rather than imposed as a hard cutoff.
        internal const float ReconEvidenceSoftCap = 6f;

        private readonly Dictionary<PlanetFaction, Dictionary<Region, float>> _gains = new();
        private readonly Dictionary<PlanetFaction, Dictionary<Region, ReconEvidence>> _reconEvidence = new();

        private sealed class ReconEvidence
        {
            internal float Positive { get; set; }
            internal float Negative { get; set; }

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

        internal void Apply(Planet planet)
        {
            if (_gains.Count == 0 && _reconEvidence.Count == 0) return;

            List<PlanetFaction> sharingFactions = planet.PlanetFactionMap.Values
                .Where(SharesPlayerVisibleIntel)
                .ToList();
            Dictionary<Region, float> pooledSharingGains = new();
            Dictionary<Region, ReconEvidence> pooledSharingRecon = new();

            foreach (KeyValuePair<PlanetFaction, Dictionary<Region, float>> factionEntry in _gains.ToList())
            {
                PlanetFaction planetFaction = factionEntry.Key;
                bool presentOnPlanet =
                    planet.PlanetFactionMap.TryGetValue(planetFaction.Faction.Id, out PlanetFaction presentFaction)
                    && ReferenceEquals(presentFaction, planetFaction);

                foreach (KeyValuePair<Region, float> gainEntry in factionEntry.Value.ToList())
                {
                    if (gainEntry.Key.Planet != planet) continue;

                    if (presentOnPlanet)
                    {
                        if (SharesPlayerVisibleIntel(planetFaction))
                        {
                            pooledSharingGains[gainEntry.Key] =
                                pooledSharingGains.TryGetValue(gainEntry.Key, out float existing)
                                    ? existing + gainEntry.Value
                                    : gainEntry.Value;
                        }
                        else
                        {
                            planetFaction.AddRegionIntel(gainEntry.Key, gainEntry.Value);
                        }
                    }

                    factionEntry.Value.Remove(gainEntry.Key);
                }

                if (factionEntry.Value.Count == 0)
                {
                    _gains.Remove(planetFaction);
                }
            }

            foreach (KeyValuePair<PlanetFaction, Dictionary<Region, ReconEvidence>> factionEntry
                in _reconEvidence.ToList())
            {
                PlanetFaction planetFaction = factionEntry.Key;
                bool presentOnPlanet =
                    planet.PlanetFactionMap.TryGetValue(planetFaction.Faction.Id, out PlanetFaction presentFaction)
                    && ReferenceEquals(presentFaction, planetFaction);

                foreach (KeyValuePair<Region, ReconEvidence> evidenceEntry in factionEntry.Value.ToList())
                {
                    if (evidenceEntry.Key.Planet != planet) continue;

                    if (presentOnPlanet)
                    {
                        if (SharesPlayerVisibleIntel(planetFaction))
                        {
                            if (!pooledSharingRecon.TryGetValue(
                                evidenceEntry.Key,
                                out ReconEvidence pooledEvidence))
                            {
                                pooledEvidence = new ReconEvidence();
                                pooledSharingRecon[evidenceEntry.Key] = pooledEvidence;
                            }
                            pooledEvidence.Add(evidenceEntry.Value);
                        }
                        else
                        {
                            planetFaction.AddRegionIntel(
                                evidenceEntry.Key,
                                CalculateReconAdjustment(evidenceEntry.Value.Positive, evidenceEntry.Value.Negative));
                        }
                    }

                    factionEntry.Value.Remove(evidenceEntry.Key);
                }

                if (factionEntry.Value.Count == 0)
                {
                    _reconEvidence.Remove(planetFaction);
                }
            }

            foreach (KeyValuePair<Region, float> pooledGain in pooledSharingGains)
            {
                foreach (PlanetFaction sharingFaction in sharingFactions)
                {
                    sharingFaction.AddRegionIntel(pooledGain.Key, pooledGain.Value);
                }
            }


            foreach (KeyValuePair<Region, ReconEvidence> pooledEvidence in pooledSharingRecon)
            {
                float adjustment = CalculateReconAdjustment(
                    pooledEvidence.Value.Positive,
                    pooledEvidence.Value.Negative);
                foreach (PlanetFaction sharingFaction in sharingFactions)
                {
                    sharingFaction.AddRegionIntel(pooledEvidence.Key, adjustment);
                }
            }
        }

        internal static float CalculateReconAdjustment(float positiveEvidence, float negativeEvidence) =>
            DiminishEvidence(Math.Max(0f, positiveEvidence))
            - DiminishEvidence(Math.Max(0f, negativeEvidence));

        internal static float DiminishEvidence(float evidence) =>
            ReconEvidenceSoftCap
            * (1f - (float)Math.Exp(-Math.Max(0f, evidence) / ReconEvidenceSoftCap));

        private static bool SharesPlayerVisibleIntel(PlanetFaction planetFaction) =>
            planetFaction?.Faction.IsPlayerFaction == true || planetFaction?.Faction.IsDefaultFaction == true;
    }
}
