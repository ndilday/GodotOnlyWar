using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Single policy boundary for contextual faction relationships. Keeping the questions here
    /// prevents strategy, combat, and later diplomacy code from growing inconsistent name checks.
    /// </summary>
    public static class FactionDispositionService
    {
        public static bool AreEnemies(Faction first, Faction second, Planet planet = null)
        {
            if (first == null || second == null || first.Id == second.Id) return false;

            bool firstImperial = IsImperial(first);
            bool secondImperial = IsImperial(second);
            if (firstImperial && secondImperial) return false;

            Faction rebel = first.OffersExternalEnemyTruce ? first
                : second.OffersExternalEnemyTruce ? second
                : null;
            Faction otherNonImperial = rebel == first ? second : rebel == second ? first : null;
            if (rebel != null && otherNonImperial != null && !IsImperial(otherNonImperial))
            {
                return IsExternalEnemy(otherNonImperial, rebel);
            }

            Faction imperial = firstImperial ? first : secondImperial ? second : null;
            if (rebel != null && imperial != null && HasPublicExternalEnemy(planet, rebel))
            {
                return false;
            }

            // Preserve current alliance semantics among non-Imperial factions until explicit
            // dispositions are added. The contextual exceptions above can evolve independently.
            return firstImperial != secondImperial;
        }

        public static bool DefendsHostAgainst(RegionFaction hiddenFaction, Faction attacker)
        {
            if (hiddenFaction?.PlanetFaction?.Faction == null || attacker == null) return false;
            if (hiddenFaction.IsPublic || !hiddenFaction.PlanetFaction.Faction.DefendsHostWhileHidden)
            {
                return false;
            }

            return IsExternalEnemy(attacker, hiddenFaction.PlanetFaction.Faction);
        }

        public static bool IsExternalEnemy(Faction faction, Faction humanHostAlignedFaction = null)
        {
            if (faction == null || IsImperial(faction)) return false;
            if (faction.OffersExternalEnemyTruce) return false;
            return humanHostAlignedFaction == null || faction.Id != humanHostAlignedFaction.Id;
        }

        private static bool HasPublicExternalEnemy(Planet planet, Faction rebel)
        {
            if (planet?.Regions == null) return false;
            return planet.Regions
                .Where(region => region != null)
                .SelectMany(region => region.RegionFactionMap.Values)
                .Any(regionFaction => regionFaction.IsPublic
                    && regionFaction.PlanetFaction.Faction.Id != rebel.Id
                    && IsExternalEnemy(regionFaction.PlanetFaction.Faction, rebel));
        }

        private static bool IsImperial(Faction faction) =>
            faction.IsPlayerFaction || faction.IsDefaultFaction;
    }
}
