using System;

namespace OnlyWar.Models.Planets
{
    public class WarpLane
    {
        public readonly int Id;
        public readonly int Quality;
        public readonly Tuple<Planet, Planet> Path;

        public WarpLane(int id, int quality, Tuple<Planet, Planet> path)
        {
            Id = id;
            Quality = quality;
            Path = path;
        }
    }
}
