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
            bool playerFleets = fleetFactions.Any(f => f.IsPlayerFaction || f.IsDefaultFaction);
            bool enemyFleets = fleetFactions.Any(f => !f.IsPlayerFaction && !f.IsDefaultFaction);
            // if both friendly and enemy fleets present, it's an assault
            if(playerFleets && enemyFleets) return true;
            foreach (Region region in planet.Regions)
            {
                bool containsPublicEnemy = region.RegionFactionMap.Values.Any(rf => rf.IsPublic && !rf.PlanetFaction.Faction.IsPlayerFaction && !rf.PlanetFaction.Faction.IsDefaultFaction);
                bool containsPlayer = region.RegionFactionMap.Values.Any(rf => rf.PlanetFaction.Faction.IsPlayerFaction || rf.PlanetFaction.Faction.IsDefaultFaction);
                // if both friendly and enemy factions present, it's an assault
                if (containsPublicEnemy && containsPlayer) return true;
                // if the region has a public enemy and friendly fleets in orbit, it's an assault
                if (containsPublicEnemy && playerFleets) return true;
                // if the region has a player faction and enemy fleets in orbit, it's an assault
                if (containsPlayer && enemyFleets) return true;
            }
            return false;
        }

        public static Faction GetControllingFaction(this Planet planet)
        {
            if (planet == null) return null;

            Region capital = planet.Regions.FirstOrDefault(region => region?.Id == planet.CapitalRegionId);
            if (capital == null)
            {
                // Legacy saves predate CapitalRegionId. Establish a stable fallback on first use;
                // the next save persists it.
                capital = planet.Regions
                    .Where(region => region != null)
                    .OrderByDescending(region => region.Population)
                    .ThenBy(region => region.Id)
                    .FirstOrDefault();
                if (capital == null) return null;
                planet.SetCapitalRegion(capital.Id);
            }

            Faction capitalController = capital.ControllingFaction?.PlanetFaction?.Faction;
            if (capitalController == null) return null;

            var cleanControl = planet.Regions
                .Where(region => region != null)
                .Select(region => region.ControllingFaction?.PlanetFaction?.Faction)
                .Where(faction => faction != null)
                .GroupBy(faction => faction.Id)
                .Select(group => new { Faction = group.First(), Regions = group.Count() })
                .OrderByDescending(entry => entry.Regions)
                .ThenBy(entry => entry.Faction.Id)
                .ToList();

            int capitalControllerRegions = cleanControl
                .FirstOrDefault(entry => entry.Faction.Id == capitalController.Id)?.Regions ?? 0;
            bool hasUniquePlurality = cleanControl
                .Where(entry => entry.Faction.Id != capitalController.Id)
                .All(entry => capitalControllerRegions > entry.Regions);

            return hasUniquePlurality ? capitalController : null;
        }

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
