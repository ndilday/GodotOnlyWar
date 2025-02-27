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
        // Garrison represents PDF forces for the default faction, and forces actively defending for other non-player factions
        public int Garrison { get; set; }
        public bool IsPublic { get; set; }
        // Entrenchment provides bonsues against attacks
        public int Entrenchment { get; set; }
        // Detection provides bonuses to detecting enemy forces in the region
        public int Detection { get; set; }
        // AntiAir provides bonuses against air atacks and air assaults
        public int AntiAir { get; set; }
        // Organization determins how much of the enemy force can be effectively deployed
        public int Organization { get; set; }

        public RegionFaction(PlanetFaction planetFaction, Region region)
        {
            LandedSquads = new List<Squad>();
            PlanetFaction = planetFaction;
            Region = region;
            IsPublic = planetFaction.IsPublic;
            Organization = -1;
        }
    }
}
