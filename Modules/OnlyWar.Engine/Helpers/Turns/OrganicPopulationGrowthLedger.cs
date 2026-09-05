using System.Collections.Generic;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Records demographic growth separately from deaths, migration, conversion, and consumption
    /// so recruitment can use the population produced naturally during the current campaign week.
    /// </summary>
    internal sealed class OrganicPopulationGrowthLedger
    {
        private readonly Dictionary<(int PlanetId, int FactionId), long> _growth = [];

        internal void Clear() => _growth.Clear();

        internal long Get(int planetId, int factionId)
        {
            return _growth.TryGetValue((planetId, factionId), out long growth) ? growth : 0;
        }

        internal void Record(int planetId, int factionId, long growth)
        {
            (int PlanetId, int FactionId) key = (planetId, factionId);
            _growth[key] = Get(key.PlanetId, key.FactionId) + growth;
        }
    }
}
