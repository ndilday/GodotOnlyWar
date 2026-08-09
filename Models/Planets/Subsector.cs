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
        // Derived from the governance seat at warp-network build/load time. A subsector with no
        // Imperial-controlled world keeps a stable fallback name until it gains a capital.
        public string Name { get; private set; }
        public readonly ushort Id;
        public readonly List<Planet> Planets;
        public readonly List<Vector2I> Cells;

        // The subsector's seat of government: the highest-Importance Imperial-controlled world.
        // Derived (recomputed at build/load) by SectorBuilder.GenerateWarpNetwork, not persisted.
        // Null if the subsector has no Imperial world. See Design/Reference/OpeningScenario.md
        public Planet GovernanceSeat { get; set; }

        public Subsector(string name, ushort id, List<Planet> planets, List<Vector2I> cells)
        {
            Planets = planets;
            Id = id;
            Cells = cells;
            Name = name;
        }

        public void SetGovernanceSeat(Planet seat)
        {
            GovernanceSeat = seat;
            Name = seat == null
                ? $"Subsector {Id}"
                : $"{seat.Name} Subsector";
        }
    }
}
