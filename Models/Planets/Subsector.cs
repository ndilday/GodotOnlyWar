﻿using Godot;
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
        public readonly List<Planet> Planets;
        public readonly List<Vector2I> Cells;

        public Subsector(string name, List<Planet> planets, List<Vector2I> cells)
        {
            Planets = planets;
            Cells = cells;
            Name = name;
        }
    }
}
