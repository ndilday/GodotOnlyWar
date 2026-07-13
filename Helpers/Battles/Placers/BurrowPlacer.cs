using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles.Placers
{
    /// <summary>
    /// Burrow-arrival placement. After the normal placer has set up both
    /// formations at engagement range, this pulls every burrow-capable squad up
    /// against an enemy squad so it erupts directly into melee on turn one,
    /// rather than charging in from the open. See
    /// Design/EvasionBurrowAndAmbush.md.
    ///
    /// Implemented as a relocation pass over squads already on the grid. Each
    /// burrow squad picks the nearest enemy squad as a whole and emerges around
    /// that squad's footprint: adjacent cells first, then expanding outward ring
    /// by ring when the perimeter fills up, so no burrower is left behind at its
    /// original spawn. A burrower that still cannot be placed within
    /// <see cref="MaxEruptionRadius"/> cells (pathologically crowded grid) stays
    /// where the normal placer put it (graceful degradation).
    /// </summary>
    public static class BurrowPlacer
    {
        // How far out from the target squad's footprint the eruption may spill
        // before giving up on a burrower. Rings grow ~8 cells per step, so this
        // accommodates several full squads erupting around one target.
        private const int MaxEruptionRadius = 6;

        public static void PlaceBurrowers(BattleGridManager grid, IEnumerable<BattleSquad> squads)
        {
            List<BattleSquad> allSquads = squads.ToList();
            foreach (BattleSquad squad in allSquads)
            {
                if (!squad.CanBurrow)
                {
                    continue;
                }
                BattleSquad target = FindNearestEnemySquad(grid, squad, allSquads);
                if (target == null)
                {
                    continue;
                }
                EruptAroundSquad(grid, squad, target);
            }
        }

        private static BattleSquad FindNearestEnemySquad(BattleGridManager grid, BattleSquad squad,
                                                         List<BattleSquad> allSquads)
        {
            List<int> squadIds = GetPlacedSoldierIds(grid, squad);
            if (squadIds.Count == 0)
            {
                return null;
            }
            bool side = grid.GetSoldierSide(squadIds[0]);

            BattleSquad best = null;
            float bestDistance = float.MaxValue;
            foreach (BattleSquad other in allSquads)
            {
                if (other == squad)
                {
                    continue;
                }
                List<int> otherIds = GetPlacedSoldierIds(grid, other);
                if (otherIds.Count == 0 || grid.GetSoldierSide(otherIds[0]) == side)
                {
                    continue;
                }
                float distance = squadIds
                    .SelectMany(id => otherIds.Select(otherId => grid.GetDistanceBetweenSoldiers(id, otherId)))
                    .Min();
                // tie-break on squad id so placement is deterministic
                if (distance < bestDistance ||
                    (distance == bestDistance && best != null && other.Id < best.Id))
                {
                    bestDistance = distance;
                    best = other;
                }
            }
            return best;
        }

        private static void EruptAroundSquad(BattleGridManager grid, BattleSquad burrowers, BattleSquad target)
        {
            List<List<Tuple<int, int>>> rings = BuildRings(grid, target);
            if (rings.Count == 0)
            {
                return;
            }
            HashSet<Tuple<int, int>> adjacentRing = new(rings[0]);

            foreach (BattleSoldier soldier in burrowers.AbleSoldiers)
            {
                IList<Tuple<int, int>> currentCells = grid.GetSoldierPosition(soldier.Soldier.Id);
                if (currentCells == null)
                {
                    continue;
                }
                // already touching the target squad — no need to shuffle
                if (currentCells.Any(adjacentRing.Contains))
                {
                    continue;
                }
                foreach (List<Tuple<int, int>> ring in rings)
                {
                    if (TryPlaceInRing(grid, soldier, ring))
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Concentric layers of cells around the target squad's combined footprint:
        /// rings[0] is every cell orthogonally adjacent to any member, rings[1] the
        /// next layer out, etc. Cells within each ring are ordered by row/column so
        /// placement is deterministic. Occupancy is not checked here — it changes as
        /// burrowers land, so it is tested at placement time.
        /// </summary>
        private static List<List<Tuple<int, int>>> BuildRings(BattleGridManager grid, BattleSquad target)
        {
            HashSet<Tuple<int, int>> visited = [];
            foreach (int soldierId in GetPlacedSoldierIds(grid, target))
            {
                foreach (Tuple<int, int> cell in grid.GetSoldierPosition(soldierId))
                {
                    visited.Add(cell);
                }
            }
            if (visited.Count == 0)
            {
                return [];
            }

            List<List<Tuple<int, int>>> rings = [];
            // snapshot: visited grows inside the loop below
            List<Tuple<int, int>> frontier = visited.ToList();
            for (int radius = 0; radius < MaxEruptionRadius; radius++)
            {
                HashSet<Tuple<int, int>> nextRing = [];
                foreach (Tuple<int, int> cell in frontier)
                {
                    foreach (Tuple<int, int> neighbor in GetOrthogonalNeighbors(cell))
                    {
                        if (visited.Add(neighbor))
                        {
                            nextRing.Add(neighbor);
                        }
                    }
                }
                List<Tuple<int, int>> orderedRing = nextRing
                    .OrderBy(cell => cell.Item1)
                    .ThenBy(cell => cell.Item2)
                    .ToList();
                rings.Add(orderedRing);
                frontier = orderedRing;
            }
            return rings;
        }

        private static bool TryPlaceInRing(BattleGridManager grid, BattleSoldier soldier,
                                           List<Tuple<int, int>> ring)
        {
            foreach (Tuple<int, int> cell in ring)
            {
                if (!grid.IsSpaceAvailable(cell))
                {
                    continue;
                }
                // TryMoveSoldier validates the soldier's full footprint at this
                // anchor (allowing its own current cells) and fails cleanly if a
                // larger creature would overlap something.
                if (grid.TryMoveSoldier(soldier, cell, soldier.Orientation))
                {
                    soldier.TopLeft = cell;
                    return true;
                }
            }
            return false;
        }

        private static List<int> GetPlacedSoldierIds(BattleGridManager grid, BattleSquad squad)
        {
            return squad.AbleSoldiers
                .Select(s => s.Soldier.Id)
                .Where(id => grid.GetSoldierPosition(id) != null)
                .ToList();
        }

        private static IEnumerable<Tuple<int, int>> GetOrthogonalNeighbors(Tuple<int, int> cell)
        {
            yield return new Tuple<int, int>(cell.Item1, (short)(cell.Item2 - 1));
            yield return new Tuple<int, int>(cell.Item1, (short)(cell.Item2 + 1));
            yield return new Tuple<int, int>((short)(cell.Item1 - 1), cell.Item2);
            yield return new Tuple<int, int>((short)(cell.Item1 + 1), cell.Item2);
        }
    }
}
