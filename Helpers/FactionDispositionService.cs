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

        /// <summary>
        /// Whether this faction fights on the Imperial side — the player's Chapter and the world's
        /// own defense forces. The single definition of a check that used to be open-coded as
        /// "IsPlayerFaction || IsDefaultFaction" in about twenty places.
        /// </summary>
        public static bool IsImperial(Faction faction) =>
            faction != null && (faction.IsPlayerFaction || faction.IsDefaultFaction);

        /// <summary>
        /// Whether two factions stand together as allies, and so pool the things a side holds in
        /// common — most importantly the defensive works in a region (RegionDefenses).
        /// </summary>
        /// <remarks>
        /// Until a real diplomacy system exists there is exactly one alliance in the game: the
        /// player's Chapter and the world's own defence forces. Everyone else stands alone.
        ///
        /// Note what this deliberately is NOT. It is not !AreEnemies, and it is not the
        /// Imperial/non-Imperial split: two non-Imperial factions are not currently modelled as
        /// fighting each other, but "not at war with" is a long way from "mans the same trench as",
        /// and conflating them would have a Tyranid swarm sheltering behind a Genestealer Cult's
        /// earthworks. Widen this only when there is a diplomacy system to widen it from.
        ///
        /// A faction is always allied with itself. That is identity rather than diplomacy, and
        /// callers that sweep a region for "everyone on this side" depend on it to include the
        /// faction they started from.
        /// </remarks>
        public static bool AreAllied(Faction first, Faction second)
        {
            if (first == null || second == null) return false;
            if (first.Id == second.Id) return true;

            return (first.IsPlayerFaction && second.IsDefaultFaction)
                || (first.IsDefaultFaction && second.IsPlayerFaction);
        }
    }
}
