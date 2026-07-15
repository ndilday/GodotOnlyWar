using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Extensions
{
    public static class RegionExtensions
    {
        // The non-player, non-default factions that could plausibly detect an intruder in this
        // region: any faction with a force fielded here (MilitaryStrength) or its own awareness of
        // the ground (RegionIntel). A region can hold more than one at once (e.g. a public Tyranid
        // incursion sitting on a still-hidden cult), so detection must aggregate across all of them.
        // Both the aggregated stealth difficulty (ReconStealthMissionStep) and the spotter roll
        // (SelectSpotter) read this same set so the difficulty and the interceptor always agree on
        // "the enemies present" (Design/MultiFactionRegions.md WI-3).
        public static List<RegionFaction> GetDetectingEnemyFactions(this Region region)
        {
            return region.RegionFactionMap.Values
                .Where(rf => !rf.PlanetFaction.Faction.IsPlayerFaction
                             && !rf.PlanetFaction.Faction.IsDefaultFaction
                             && (rf.MilitaryStrength > 0 || rf.GetOwnRegionIntel() > 0))
                .ToList();
        }

        // Chooses which enemy faction detects an intruder (Design/MultiFactionRegions.md WI-3). When
        // some enemy has awareness of the region, the spotter is drawn in proportion to that
        // awareness — caught by surveillance, so a faction with no eyes here gets weight 0 and cannot
        // be the spotter. When no one has any awareness, the intruder walks into a patrol and the
        // spotter is drawn in proportion to deployed strength instead. Returns null only when no
        // enemy faction is present at all (the caller then falls back to the mission's target).
        public static RegionFaction SelectSpotter(this Region region, IRNG random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            List<RegionFaction> enemies = region.GetDetectingEnemyFactions();
            if (enemies.Count == 0) return null;

            double totalIntel = enemies.Sum(rf => (double)rf.GetOwnRegionIntel());
            if (totalIntel > 0)
            {
                return WeightedPick(enemies, rf => rf.GetOwnRegionIntel(), totalIntel, random);
            }

            // No surveillance anywhere in the region: the intruder blunders into whoever is out
            // patrolling, weighted by the troops each faction actually has fielded.
            double totalStrength = enemies.Sum(rf => (double)rf.GetDeployedStrength());
            if (totalStrength <= 0) return enemies[0];
            return WeightedPick(enemies, rf => rf.GetDeployedStrength(), totalStrength, random);
        }

        // Roulette-wheel pick over a non-empty list using the shared RNG, given a per-item weight and
        // its precomputed positive total. Falls through to the last item to absorb float rounding.
        private static RegionFaction WeightedPick(
            List<RegionFaction> factions,
            Func<RegionFaction, double> weight,
            double totalWeight,
            IRNG random)
        {
            double roll = random.GetLinearDouble() * totalWeight;
            double cumulative = 0;
            foreach (RegionFaction rf in factions)
            {
                cumulative += weight(rf);
                if (roll < cumulative) return rf;
            }
            return factions[factions.Count - 1];
        }

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
                .Where(rf => !rf.PlanetFaction.Faction.IsPlayerFaction
                             && !rf.PlanetFaction.Faction.IsDefaultFaction)
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
                             && (rf.PlanetFaction.Faction.IsDefaultFaction
                                 || rf.PlanetFaction.Faction.IsPlayerFaction))
                .Sum(rf => rf.Population);
        }

        public static List<Region> GetSelfAndAdjacentRegions(this Region region)
        {
            return new List<Region> { region }.Union(GetAdjacentRegions(region)).ToList();
        }

        // The regions are laid out as a flat-top hex board (see PlanetTacticalScreenView's
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
