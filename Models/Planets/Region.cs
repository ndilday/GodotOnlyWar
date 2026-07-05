using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Planets
{
    public class Region
    {
        public readonly int Id;
        public readonly Planet Planet;
        public readonly string Name;
        public readonly RegionCoordinate Coordinates;
        public float IntelligenceLevel { get; set; }
        // The total population (in thousands) the region's land can sustain across all
        // factions. Organic growth slows as the region's combined population approaches
        // this value. A value of 0 (or less) is treated as uncapped. Tyranid Consumption
        // temporarily degrades this below MaximumCarryingCapacity (PRD §4.24).
        public long CarryingCapacity { get; set; }
        // The region's natural (undegraded) carrying capacity — the ceiling CarryingCapacity
        // recovers toward after biomass consumption. Equal to CarryingCapacity at generation.
        public long MaximumCarryingCapacity { get; set; }
        public List<Mission> SpecialMissions { get; }
        // territory is diamond-shaped
        // 1
        // 2 3
        // 4 5 6
        // 7 8 9 10
        // 11 12 13
        // 14 15
        // 16
        public readonly Dictionary<int, RegionFaction> RegionFactionMap;

        // population is a raw headcount (summed across this region's factions)
        public long Population
        {
            get
            {
                return RegionFactionMap.Sum(rfm => rfm.Value.Population);
            }
        }

        // The population that competes for the land's carrying capacity: every faction except
        // biomass-consumers (Tyranids), which neither draw on nor are limited by capacity but
        // devour it instead (PRD §4.24). This — not the full Population — feeds the growth
        // crowding factor, so a region swarming with Tyranids does not artificially starve its
        // remaining inhabitants; they die from the land being consumed, not from Tyranid headcount.
        public long NonConsumerPopulation
        {
            get
            {
                return RegionFactionMap.Values
                    .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption)
                    .Sum(rf => rf.Population);
            }
        }

        // I suspect I'm going to change my mind regularly on the scale for this value
        // for now, let's be simple, and let it be headcount
        public long PlanetaryDefenseForces
        {
            get
            {
                // PDF is only the garrison maintained by the default (Imperial) faction;
                // other factions' Garrison values represent their own defending forces.
                return RegionFactionMap.Values
                    .Where(rf => rf.PlanetFaction.Faction.IsDefaultFaction)
                    .Sum(rf => rf.Garrison);
            }
        }

        // really basic, for now: if there's only one public faction, it's the controlling faction
        public RegionFaction ControllingFaction
        {
            get
            {
                if(RegionFactionMap.Where(rf => rf.Value.IsPublic).Count() == 1)
                {
                    return RegionFactionMap.Where(rf => rf.Value.IsPublic).First().Value;
                }
                else
                {
                    return null;
                }
            }
        }

        public Region(int id, Planet planet, int regionType, string name, RegionCoordinate coordinates, float intelligenceLevel, long carryingCapacity = 0, long maximumCarryingCapacity = -1)
        {
            Id = id;
            Planet = planet;
            RegionFactionMap = [];
            Name = name;
            Coordinates = coordinates;
            IntelligenceLevel = intelligenceLevel;
            CarryingCapacity = carryingCapacity;
            // A negative sentinel means "initialize to the natural ceiling" — the common case at
            // generation, where the region has not yet been degraded. The load path passes the
            // persisted maximum explicitly.
            MaximumCarryingCapacity = maximumCarryingCapacity < 0 ? carryingCapacity : maximumCarryingCapacity;
            SpecialMissions = new List<Mission>();
        }


    }
}
