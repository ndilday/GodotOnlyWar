using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Helpers.Extensions
{
    public static class RegionFactionExtensions
    {
        // This faction's situational awareness of its OWN region — the defensive face of the unified
        // per-(faction, region) intel value (replaces the old Detection stat). Fed by listening posts,
        // patrols, and recon; consumed by strategic combat and stealth-check difficulty. A patrol now
        // raises this directly (recon of one's own ground), so an actively-patrolled region is harder
        // to infiltrate as an emergent consequence rather than via a bolted-on penalty.
        public static float GetOwnRegionIntel(this RegionFaction regionFaction) =>
            regionFaction.PlanetFaction.GetRegionIntel(regionFaction.Region);

        // How well an arbitrary faction understands this region (0 if it has no presence/awareness).
        // The offensive face of the same value: what an attacker believes about a region it may hit.
        public static float GetFactionRegionIntel(this Region region, Faction faction) =>
            region.GetFactionRegionIntel(faction.Id);

        public static float GetFactionRegionIntel(this Region region, int factionId) =>
            region.Planet.PlanetFactionMap.TryGetValue(factionId, out PlanetFaction planetFaction)
                ? planetFaction.GetRegionIntel(region)
                : 0f;

        public static string GetPopulationDescription(this RegionFaction regionFaction)
        {
            if (regionFaction != null && regionFaction.IsPublic)
            {
                if (regionFaction.Region.IntelligenceLevel <= 0)
                {
                    return "Unknown";
                }
                else if (regionFaction.Region.IntelligenceLevel >= 6)
                {
                    return regionFaction.Population.ToString();
                }
                else
                {
                    int divisor = (int)Math.Pow(10, 6 - (int)regionFaction.Region.IntelligenceLevel);
                    long popCount = regionFaction.Population / divisor * divisor;
                    if (popCount > 0)
                    {
                        return popCount.ToString();
                    }
                    else if (regionFaction.Population > 0)
                    {
                        return "Low";
                    }
                }
            }
            return "None";
        }

        // Fuzzy, fog-of-war-friendly description of a defensive value (Entrenchment,
        // Detection, Anti-Air). Shared by the planet-tactical and region screens so enemy
        // defenses read consistently and never expose the raw integer to the player.
        public static string GetDefenseLevelDescription(int level)
        {
            switch (level)
            {
                case 0:
                    return "None";
                case 1:
                case 2:
                    return "Minimal";
                case 3:
                case 4:
                    return "Mediocre";
                case 5:
                case 6:
                    return "Moderate";
                case 7:
                case 8:
                    return "Heavy";
                default:
                    return "Massive";
            }
        }
    }
}
