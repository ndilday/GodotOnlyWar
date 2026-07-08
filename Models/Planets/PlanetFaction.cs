using OnlyWar.Models.Squads;
using System.Collections.Generic;

namespace OnlyWar.Models.Planets
{
    public class PlanetFaction
    {
        public Faction Faction { get; }
        public bool IsPublic { get; set; }
        public float PlayerReputation { get; set; }
        public int PlanetaryControl { get; set; }
        public Character Leader { get; set; }

        // This faction's situational awareness of each region on the planet — a single per-region
        // intelligence value that serves both offensively (how well it knows an enemy region it may
        // attack) and defensively (how well it sees its own ground, i.e. what used to be Detection).
        // Fed by listening posts (passive), patrols (recon of one's own region), and recon missions
        // (scouting an enemy region); decays each turn; consumed by strategic combat, stealth-check
        // difficulty, offensive strength estimates, garrison sizing, and estimator caution. Replaces
        // the old split of RegionFaction.Detection (own-territory) + RegionFaction.ObserverIntel
        // (enemy-territory belief). Persisted per (planet, faction, region); sparse — only regions
        // with non-zero awareness are stored.
        public readonly Dictionary<Region, float> RegionIntel;

        public PlanetFaction(Faction faction)
        {
            Faction = faction;
            IsPublic = true;
            PlayerReputation = 0;
            PlanetaryControl = 0;
            RegionIntel = new Dictionary<Region, float>();
        }

        // How well this faction believes it understands the region (0 = no awareness).
        public float GetRegionIntel(Region region) =>
            RegionIntel.TryGetValue(region, out float level) ? level : 0f;

        // Raises this faction's awareness of a region (a recon/patrol result, or a passive sensor
        // top-up). Ignores non-positive amounts so a failed sweep never erodes prior knowledge.
        public void AddRegionIntel(Region region, float amount)
        {
            if (amount <= 0) return;
            RegionIntel[region] = GetRegionIntel(region) + amount;
        }

        // Overwrites this faction's awareness of a region (scenario seeding, decay). A value of
        // zero or less removes the entry so the map stays sparse.
        public void SetRegionIntel(Region region, float level)
        {
            if (level <= 0) RegionIntel.Remove(region);
            else RegionIntel[region] = level;
        }
    }
}
