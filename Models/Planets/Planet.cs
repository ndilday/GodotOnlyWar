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
                return PlanetFactionMap.TryGetValue(controllingFaction.Id, out PlanetFaction pf)
                    ? pf.Leader
                    : null;
            }
        }

        public float Stability
        {
            get
            {
                return 0;
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
            PlanetTemplate template, int importance, int taxLevel)
        {
            Id = id;
            Name = name;
            Position = position;
            Size = size;
            Template = template;
            Importance = importance;
            TaxLevel = taxLevel;
            OrbitingTaskForceList = [];
            PlanetFactionMap = [];
            Regions = new Region[16];
        }

    }
}