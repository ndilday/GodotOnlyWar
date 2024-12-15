using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Planets
{
    public class Region
    {
        public readonly int Id;
        public readonly Planet Planet;
        public readonly PlanetFaction ControllingFaction;
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
                return RegionFactionMap.Sum(rfm => rfm.Value.PDFMembers);
            }
        }

        public Region(int id, Planet planet, int regionType)
        {
            Id = id;
            Planet = planet;
            RegionFactionMap = [];
        }


    }
}
