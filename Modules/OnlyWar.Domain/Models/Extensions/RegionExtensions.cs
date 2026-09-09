using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Domain.Extensions
{
    /// <summary>
    /// Intrinsic region queries: board geometry, adjacency, and questions answerable from the
    /// region graph alone. Detection and spotter selection depend on mission policy and stay with
    /// their owner in <c>RegionDetectionExtensions</c>.
    /// </summary>
    public static class RegionExtensions
    {
        public static RegionCoordinate GetCoordinatesFromRegionNumber(int regionNumber)
        {
            return regionNumber switch
            {
                0 => new RegionCoordinate(0, 0),
                1 => new RegionCoordinate(1, 0),
                2 => new RegionCoordinate(1, 1),
                3 => new RegionCoordinate(2, 0),
                4 => new RegionCoordinate(2, 1),
                5 => new RegionCoordinate(2, 2),
                6 => new RegionCoordinate(3, 0),
                7 => new RegionCoordinate(3, 1),
                8 => new RegionCoordinate(3, 2),
                9 => new RegionCoordinate(3, 3),
                10 => new RegionCoordinate(4, 1),
                11 => new RegionCoordinate(4, 2),
                12 => new RegionCoordinate(4, 3),
                13 => new RegionCoordinate(5, 2),
                14 => new RegionCoordinate(5, 3),
                15 => new RegionCoordinate(6, 3),
                _ => throw new ArgumentOutOfRangeException(nameof(regionNumber), regionNumber,
                    "Region number must be in the range 0-15."),
            };
        }

        // The enemy the player would see in this region. A region can hold more than one
        // non-player, non-default faction at once (e.g. a public Tyranid incursion sitting on
        // top of a still-hidden Genestealer Cult), so a plain FirstOrDefault can return the
        // hidden faction and make a visibly-invaded region read as empty. Prefer a public enemy;
        // fall back to a hidden one only when that is all the region has (so a hidden-only region
        // still reports correctly as undetected).
        public static RegionFaction GetVisibleEnemyRegionFaction(this Region region)
        {
            List<RegionFaction> enemies = region.RegionFactionMap.Values
                .Where(rf => !FactionRoles.IsImperial(rf.PlanetFaction.Faction))
                .ToList();
            return enemies.FirstOrDefault(rf => rf.IsPublic) ?? enemies.FirstOrDefault();
        }

        public static bool HasHiddenDefaultFaction(this Region region)
        {
            return region.RegionFactionMap.Values
                .Any(rf => rf.PlanetFaction.Faction.IsDefaultFaction && !rf.IsPublic);
        }

        public static long GetVisibleCivilianPopulation(this Region region)
        {
            return region.RegionFactionMap.Values
                .Where(rf => rf.IsPublic
                             && FactionRoles.IsImperial(rf.PlanetFaction.Faction))
                .Sum(rf => rf.Population);
        }

        public static List<Region> GetSelfAndAdjacentRegions(this Region region)
        {
            return new List<Region> { region }.Union(GetAdjacentRegions(region)).ToList();
        }

        // The regions are laid out as a flat-top hex board (see PlanetRegionMapView's
        // diamond layout). In coordinate space a region's row is X and its horizontal offset is
        // (2*Y - X), so the six hex neighbours are NOT the square 8-neighbourhood of (X, Y) but
        // the offsets below. Using a square neighbourhood here made the region-detail screen show
        // the wrong neighbours (e.g. Omicron (5,3) picking up Xi (5,2) instead of Iota (3,2)) and
        // fed bogus adjacency into fleet routing, biomass spread, and faction strategy.
        private static readonly (int dx, int dy)[] HexNeighborOffsets =
        {
            (-2, -1), // N
            (-1,  0), // NE
            ( 1,  1), // SE
            ( 2,  1), // S
            ( 1,  0), // SW
            (-1, -1), // NW
        };

        public static List<Region> GetAdjacentRegions(this Region region)
        {
            List<Region> adjacentRegions = new List<Region>();
            foreach ((int dx, int dy) in HexNeighborOffsets)
            {
                int x = region.Coordinates.X + dx;
                int y = region.Coordinates.Y + dy;
                Region neighbor = region.Planet.Regions
                    .FirstOrDefault(r => r != null && r.Coordinates.X == x && r.Coordinates.Y == y);
                if (neighbor != null)
                {
                    adjacentRegions.Add(neighbor);
                }
            }
            return adjacentRegions;
        }
    }
}
