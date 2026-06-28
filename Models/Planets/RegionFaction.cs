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
        public long Garrison { get; set; }
        public bool IsPublic { get; set; }
        // Entrenchment provides bonsues against attacks
        public int Entrenchment { get; set; }
        // Detection provides bonuses to detecting enemy forces in the region
        public int Detection { get; set; }
        // AntiAir provides bonuses against air atacks and air assaults
        public int AntiAir { get; set; }
        // Organization determins how much of the enemy force can be effectively deployed
        public int Organization { get; set; }

        // Multiplier (default 1.0) applied to this faction's organic population growth in the
        // turn loop. A general primitive, not scenario-specific: the Opening Scenario sets it
        // < 1.0 on stamped Tyranid regions to throttle them below the default curve, and the
        // post-0.7 Ork/revolt tuning will reuse the same lever (Design/OpeningScenario.md §2.2).
        public float GrowthMultiplier { get; set; } = 1.0f;

        // Transient, within-turn diversion state. Set by a diversion mission's pre-planning
        // resolution and consumed by FactionStrategyController when it generates orders that
        // same turn, then cleared. Never persists across a turn, so it is not saved/loaded.
        //
        // PerceivedThreatBonus: extra apparent enemy threat (in troop-equivalents) that a feint
        // projects onto this region, inflating the garrison its controller feels it must hold.
        public float PerceivedThreatBonus { get; set; }
        // ProvocationLevel: how strongly enemies are baited into attacking the force standing in
        // this region (i.e. the feinting force's own region), drawing a counterattack.
        public float ProvocationLevel { get; set; }

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
