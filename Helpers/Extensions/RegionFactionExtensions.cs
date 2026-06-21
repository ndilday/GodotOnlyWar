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
