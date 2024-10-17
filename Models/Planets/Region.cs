using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

namespace OnlyWar.Models.Planets
{
    public class Region
    {
        public readonly Planet Planet;
        public readonly PlanetFaction OwningFaction;
        // territory is diamond-shaped
        // 1
        // 2 3
        // 4 5 6
        // 7 8 9 10
        // 11 12 13
        // 14 15
        // 16
        public readonly Tuple<ushort, ushort> Position;
        public List<Squad> LandedSquads { get; set; }
    }
}
