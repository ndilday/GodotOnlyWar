using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Planets
{
    public class Planet
    {
        public readonly int Id;
        public readonly string Name;
        public readonly Tuple<ushort, ushort> Position;
        public readonly PlanetTemplate Template;
        public readonly int Importance;
        public readonly int TaxLevel;
        public readonly int Size;
        public readonly Region[] Regions;
        public bool IsUnderAssault { get; set; }

        public List<TaskForce> TaskForces;
        public readonly Dictionary<int, PlanetFaction> PlanetFactionMap;
        public Faction ControllingFaction;

        public float Stability
        {
            get
            {
                return 0;
            }
        }
        
        // planetary population is in thousands
        public long Population
        {
            get
            {
                return Regions.Sum(r => r.Population);
            }
        }

        // I suspect I'm going to change my mind regularly on the scale for this value
        // for now, let's be simple, and let it be headcount
        public int PlanetaryDefenseForces
        {
            get
            {
                return Regions.Sum(r => r.PlanetaryDefenseForces);
            }
        }

        public Planet(int id, string name, Tuple<ushort, ushort> position, int size, 
            PlanetTemplate template, int importance, int taxLevel)
        {
            Id = id;
            Name = name;
            Position = position;
            Size = size;
            Template = template;
            Importance = importance;
            TaxLevel = taxLevel;
            TaskForces = [];
            PlanetFactionMap = [];
            Regions = new Region[16];
        }

    }
}