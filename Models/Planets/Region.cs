using OnlyWar.Models.Missions;
using OnlyWar.Helpers;
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
        // The total population the region's land can sustain across all factions.
        // Organic growth slows as the region's combined population approaches this value.
        // A value of 0 (or less) is treated as uncapped. Tyranid Consumption temporarily
        // degrades this below MaximumCarryingCapacity (PRD §4.24).
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
                // The official PDF roster includes the public default-faction garrison plus
                // covert members of hidden factions that continue serving in the host defense.
                // ArmedCivilians is deliberately excluded: revolutionary militia are not PDF.
                return RegionFactionMap.Values
                    .Where(rf =>
                        (rf.IsPublic && rf.PlanetFaction.Faction.IsDefaultFaction)
                        || (!rf.IsPublic && rf.PlanetFaction.Faction.HasBehavior(
                            FactionBehavior.DefendsHostWhileHidden)))
                    .Sum(rf => rf.Garrison);
            }
        }

        // A region is controlled when all public factions belong to the same allied bloc. The
        // player's Chapter and the world's default Imperial faction are separate presences in the
        // region map, but they share control of the ground rather than contesting it. Keep the
        // default faction as the representative when both are present so landing Chapter forces
        // does not visually transfer civilian control away from the world.
        public RegionFaction ControllingFaction
        {
            get
            {
                List<RegionFaction> publicFactions = RegionFactionMap.Values
                    .Where(regionFaction => regionFaction.IsPublic)
                    .ToList();
                if (publicFactions.Count == 0) return null;

                Faction controlFaction = publicFactions[0].PlanetFaction.Faction;
                if (publicFactions.Skip(1).Any(regionFaction =>
                    !FactionRelationshipService.AreAllied(
                        controlFaction,
                        regionFaction.PlanetFaction.Faction,
                        Planet)))
                {
                    return null;
                }

                return publicFactions.FirstOrDefault(regionFaction =>
                    regionFaction.PlanetFaction.Faction.IsDefaultFaction)
                    ?? publicFactions[0];
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
