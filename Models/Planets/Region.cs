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
        public readonly Tuple<int, int> Coordinates;
        public float IntelligenceLevel { get; set; }
        public List<SpecialMission> SpecialMissions { get; }
        // territory is diamond-shaped
        // 1
        // 2 3
        // 4 5 6
        // 7 8 9 10
        // 11 12 13
        // 14 15
        // 16
        public readonly Dictionary<int, RegionFaction> RegionFactionMap;

        // planetary population is in thousands
        public long Population
        {
            get
            {
                return RegionFactionMap.Sum(rfm => rfm.Value.Population);
            }
        }

        // I suspect I'm going to change my mind regularly on the scale for this value
        // for now, let's be simple, and let it be headcount
        public int PlanetaryDefenseForces
        {
            get
            {
                return RegionFactionMap.Sum(rfm => rfm.Value.Garrison);
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

        public Region(int id, Planet planet, int regionType, string name, Tuple<int, int> coordinates, float intelligenceLevel)
        {
            Id = id;
            Planet = planet;
            RegionFactionMap = [];
            Name = name;
            Coordinates = coordinates;
            IntelligenceLevel = intelligenceLevel;
            SpecialMissions = new List<SpecialMission>();
        }


    }
}
