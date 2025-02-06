using System.Collections.Generic;
using Godot;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Planets
{
    public class RegionFaction
    {
        public readonly PlanetFaction PlanetFaction;
        public readonly Region Region;
        public readonly List<Squad> LandedSquads;
        public long Population { get; set; }
        public int PDFMembers { get; set; }
        public bool IsPublic { get; set; }

        public RegionFaction(PlanetFaction planetFaction, Region region)
        {
            LandedSquads = new List<Squad>();
            PlanetFaction = planetFaction;
            Region = region;
            IsPublic = planetFaction.IsPublic;
        }
    }
}
