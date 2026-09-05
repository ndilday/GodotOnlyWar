using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Extensions
{
    public static class PlanetExtensions
    {
        public static bool IsUnderAssault(this Planet planet)
        {
            // see what factions are represented in orbit
            if (planet == null) return false;
            var fleetFactions = planet.OrbitingTaskForceList.Select(tf => tf.Faction).Distinct();
            bool playerFleets = fleetFactions.Any(FactionRoles.IsImperial);
            bool enemyFleets = fleetFactions.Any(f => !FactionRoles.IsImperial(f));
            // if both friendly and enemy fleets present, it's an assault
            if(playerFleets && enemyFleets) return true;
            foreach (Region region in planet.Regions)
            {
                bool containsPublicEnemy = region.RegionFactionMap.Values.Any(rf => rf.IsPublic && !FactionRoles.IsImperial(rf.PlanetFaction.Faction));
                bool containsPlayer = region.RegionFactionMap.Values.Any(rf => FactionRoles.IsImperial(rf.PlanetFaction.Faction));
                // if both friendly and enemy factions present, it's an assault
                if (containsPublicEnemy && containsPlayer) return true;
                // if the region has a public enemy and friendly fleets in orbit, it's an assault
                if (containsPublicEnemy && playerFleets) return true;
                // if the region has a player faction and enemy fleets in orbit, it's an assault
                if (containsPlayer && enemyFleets) return true;
            }
            return false;
        }

        public static Faction GetControllingFaction(this Planet planet) => planet?.GetControllingFaction();

        public static bool IsContested(this Planet planet)
        {
            return planet?.GetControllingFaction() == null;
        }

        public static Region GetCapitalRegion(this Planet planet)
        {
            if (planet == null) return null;
            Region capital = planet.Regions.FirstOrDefault(region => region?.Id == planet.CapitalRegionId);
            if (capital != null) return capital;

            capital = planet.Regions
                .Where(region => region != null)
                .OrderByDescending(region => region.Population)
                .ThenBy(region => region.Id)
                .FirstOrDefault();
            if (capital != null)
            {
                planet.SetCapitalRegion(capital.Id);
            }
            return capital;
        }
    }
}
