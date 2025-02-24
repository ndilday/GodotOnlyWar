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
        // Entrenchment provides bonsues against attacks
        public float Entrenchment { get; set; }
        // Detection provides bonuses to detecting enemy forces in the region
        public float Detection { get; set; }
        // AntiAir provides bonuses against air atacks and air assaults
        public float AntiAir { get; set; }
        // I'm not sure what organization will do yet, but it's what assassination should reduce
        // perhaps it'll factor into size of enemy forces faced in battle
        public float Organization { get; set; }

        public RegionFaction(PlanetFaction planetFaction, Region region)
        {
            LandedSquads = new List<Squad>();
            PlanetFaction = planetFaction;
            Region = region;
            IsPublic = planetFaction.IsPublic;
        }
    }
}
