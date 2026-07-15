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
        private readonly Dictionary<PlanetFaction, Dictionary<Region, float>> _gains = new();

        internal void Clear()
        {
            _gains.Clear();
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

        internal void Apply(Planet planet)
        {
            if (_gains.Count == 0) return;

            List<PlanetFaction> sharingFactions = planet.PlanetFactionMap.Values
                .Where(SharesPlayerVisibleIntel)
                .ToList();
            Dictionary<Region, float> pooledSharingGains = new();

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

            foreach (KeyValuePair<Region, float> pooledGain in pooledSharingGains)
            {
                foreach (PlanetFaction sharingFaction in sharingFactions)
                {
                    sharingFaction.AddRegionIntel(pooledGain.Key, pooledGain.Value);
                }
            }
        }

        private static bool SharesPlayerVisibleIntel(PlanetFaction planetFaction) =>
            planetFaction?.Faction.IsPlayerFaction == true || planetFaction?.Faction.IsDefaultFaction == true;
    }
}
