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
            // the controlling faction is the one holding the plurality of cleanly-controlled regions
            SortedList<int, int> factionRegionControlMap = new SortedList<int, int>();
            Dictionary<int, Faction> factionMap = new Dictionary<int, Faction>();
            foreach (Region region in planet.Regions)
            {
                Faction controllingFaction = region.ControllingFaction?.PlanetFaction.Faction;
                // A contested or vacated region has no single public controller (zero or several
                // public factions, so Region.ControllingFaction is null). Skip it rather than
                // letting the null crash the tally: this keeps the turn loop alive while factions
                // fight over regions, e.g. the Opening Scenario's Tyranid spread as it pushes into
                // Imperial regions over a campaign (Design/OpeningScenario.md §6).
                if (controllingFaction == null)
                {
                    continue;
                }
                if (factionRegionControlMap.ContainsKey(controllingFaction.Id))
                {
                    factionRegionControlMap[controllingFaction.Id]++;
                }
                else
                {
                    factionMap[controllingFaction.Id] = controllingFaction;
                    factionRegionControlMap[controllingFaction.Id] = 1;
                }
            }
            if (factionRegionControlMap.Count == 0)
            {
                // No region is cleanly controlled (every region is contested). Fall back to a
                // public planet faction — preferring the default (Imperial) one — so callers still
                // receive a non-null controller; only the most degenerate planets reach this.
                return planet.PlanetFactionMap.Values
                           .Where(pf => pf.IsPublic)
                           .OrderByDescending(pf => pf.Faction.IsDefaultFaction)
                           .Select(pf => pf.Faction)
                           .FirstOrDefault()
                       ?? planet.PlanetFactionMap.Values.First().Faction;
            }
            int key = factionRegionControlMap.OrderByDescending(kv => kv.Value).First().Key;
            return factionMap[key];
        }
    }
}
