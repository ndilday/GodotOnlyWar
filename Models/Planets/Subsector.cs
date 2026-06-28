using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Models.Planets
{
    public class Subsector
    {
        // if we wanted to be extra "safe", we could make these lists private, with public IReadOnlyList accessors
        public readonly string Name;
        public readonly ushort Id;
        public readonly List<Planet> Planets;
        public readonly List<Vector2I> Cells;

        // The subsector's seat of government: the highest-Importance Imperial-controlled world.
        // Derived (recomputed at build/load) by SectorBuilder.GenerateWarpNetwork, not persisted.
        // Null if the subsector has no Imperial world. See Design/OpeningScenario.md §2.3.
        public Planet GovernanceSeat { get; set; }

        public Subsector(string name, ushort id, List<Planet> planets, List<Vector2I> cells)
        {
            Planets = planets;
            Id = id;
            Cells = cells;
            Name = name;
        }
    }
}
