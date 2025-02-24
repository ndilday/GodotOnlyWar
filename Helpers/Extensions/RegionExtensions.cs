using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Extensions
{
    public static class RegionExtensions
    {
        public static Tuple<int, int> GetCoordinatesFromRegionNumber(int regionNumber)
        {
            switch (regionNumber)
            {
                case 0:
                    return new Tuple<int, int>(0, 0);
                case 1:
                    return new Tuple<int, int>(1, 0);
                case 2:
                    return new Tuple<int, int>(1, 1);
                case 3:
                    return new Tuple<int, int>(2, 0);
                case 4:
                    return new Tuple<int, int>(2, 1);
                case 5:
                    return new Tuple<int, int>(2, 2);
                case 6:
                    return new Tuple<int, int>(3, 0);
                case 7:
                    return new Tuple<int, int>(3, 1);
                case 8:
                    return new Tuple<int, int>(3, 2);
                case 9:
                    return new Tuple<int, int>(3, 3);
                case 10:
                    return new Tuple<int, int>(4, 1);
                case 11:
                    return new Tuple<int, int>(4, 2);
                case 12:
                    return new Tuple<int, int>(4, 3);
                case 13:
                    return new Tuple<int, int>(5, 2);
                case 14:
                    return new Tuple<int, int>(5, 3);
                case 15:
                    return new Tuple<int, int>(6, 3);
                default:
                    return null;
            }
        }

        public static List<Region> GetSelfAndAdjacentRegions(Region region)
        {
            return new List<Region> { region }.Union(GetAdjacentRegions(region)).ToList();
        }

        public static List<Region> GetAdjacentRegions(Region region)
        {
            List<Region> adjacentRegions = new List<Region>();
            foreach (Region r in region.Planet.Regions)
            {
                if ((r.Coordinates.Item1 == region.Coordinates.Item1 - 1 ||
                    r.Coordinates.Item1 == region.Coordinates.Item1 ||
                    r.Coordinates.Item1 == region.Coordinates.Item1 + 1) &&
                   (r.Coordinates.Item2 == region.Coordinates.Item2 - 1 ||
                    r.Coordinates.Item2 == region.Coordinates.Item2 ||
                    r.Coordinates.Item2 == region.Coordinates.Item2 + 1) &&
                   (r.Coordinates.Item1 != region.Coordinates.Item1 ||
                    r.Coordinates.Item2 != region.Coordinates.Item2))
                {
                    adjacentRegions.Add(r);
                }
            }
            return adjacentRegions;
        }
    }
}
