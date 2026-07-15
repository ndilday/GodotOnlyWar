using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Fleets;
using OnlyWar.Helpers.Extensions;

namespace OnlyWar.Models.Planets
{
    // Rank of the Imperial governor seated on a planet. Derived (recomputed at build/load),
    // not persisted — see Design/OpeningScenario.md §2.3.
    public enum GovernanceTier { Planetary = 0, SubsectorCapital = 1, SectorCapital = 2 }

    public class Planet
    {
        public readonly int Id;
        public readonly string Name;
        public readonly Coordinate Position;
        public readonly PlanetTemplate Template;
        public readonly int Importance;
        public readonly int TaxLevel;
        public readonly int Size;
        public readonly Region[] Regions;

        // The permanent seat of planetary government. Selected once from the greatest original
        // regional population (Region.Id breaks ties) and persisted so later migration, conquest,
        // or depopulation cannot move the capital implicitly.
        public int CapitalRegionId { get; private set; }

        public List<TaskForce> OrbitingTaskForceList;
        public readonly Dictionary<int, PlanetFaction> PlanetFactionMap;

        // Governance designation, recomputed by SectorBuilder.GenerateWarpNetwork.
        public GovernanceTier GovernanceTier { get; set; } = GovernanceTier.Planetary;

        // The character governing this world: the leader of the controlling PlanetFaction.
        // Null when the controlling faction has no seated leader (e.g. enemy-held worlds).
        public Character Governor
        {
            get
            {
                Faction controllingFaction = this.GetControllingFaction();
                if (controllingFaction == null) return null;
                return PlanetFactionMap.TryGetValue(controllingFaction.Id, out PlanetFaction pf)
                    ? pf.Leader
                    : null;
            }
        }

        public float Stability
        {
            get
            {
                List<RegionFaction> imperialRegions = Regions
                    .Where(region => region != null)
                    .Select(region => region.RegionFactionMap.Values.FirstOrDefault(rf =>
                        rf.PlanetFaction.Faction.IsDefaultFaction && rf.Population > 0))
                    .Where(rf => rf != null)
                    .ToList();
                long population = imperialRegions.Sum(rf => rf.Population);
                if (population <= 0) return 0;
                return (float)(imperialRegions.Sum(rf => rf.Contentment * (double)rf.Population)
                    / population);
            }
        }
        
        // population is a raw headcount (summed across the planet's regions)
        public long Population
        {
            get
            {
                return Regions.Sum(r => r.Population);
            }
        }

        // I suspect I'm going to change my mind regularly on the scale for this value
        // for now, let's be simple, and let it be headcount
        public long PlanetaryDefenseForces
        {
            get
            {
                return Regions.Sum(r => r.PlanetaryDefenseForces);
            }
        }

        public Planet(int id, string name, Coordinate position, int size,
            PlanetTemplate template, int importance, int taxLevel, int capitalRegionId = -1)
        {
            Id = id;
            Name = name;
            Position = position;
            Size = size;
            Template = template;
            Importance = importance;
            TaxLevel = taxLevel;
            CapitalRegionId = capitalRegionId;
            OrbitingTaskForceList = [];
            PlanetFactionMap = [];
            Regions = new Region[16];
        }

        public void SetCapitalRegion(int regionId)
        {
            CapitalRegionId = regionId;
        }

    }
}
