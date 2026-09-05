using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Resolves the impact point and victims of a circular blast (grenade) template.
    /// Unlike <see cref="ConeTemplate"/>, the thrower is NOT excluded — danger-close
    /// throws are legal and self-inflicted casualties are possible by design.
    /// </summary>
    public static class BlastTemplate
    {
        /// <summary>Cells of scatter per point of check-failure margin. Balance knob.</summary>
        public const float ScatterDistancePerPoint = 1.0f;

        /// <summary>A soldier caught in a blast, with the distance from the impact
        /// center to their nearest caught cell (drives damage falloff).</summary>
        public readonly record struct BlastVictim(int SoldierId, float DistanceFromImpact);

        /// <summary>
        /// Resolves where the blast lands. The aim cell is the target's footprint cell
        /// nearest the shooter. A non-negative margin lands on the aim cell; a failure
        /// deviates |margin| × <see cref="ScatterDistancePerPoint"/> cells in the
        /// direction <paramref name="directionRoll"/> × 2π, rounded to a grid cell.
        /// The grid is sparse and unbounded, so no clamping is required.
        /// </summary>
        public static ValueTuple<int, int> ResolveImpactCell(
            BattleGridManager grid,
            int shooterId,
            int targetId,
            float margin,
            double directionRoll)
        {
            ArgumentNullException.ThrowIfNull(grid);

            ValueTuple<int, int> aimCell = FindNearestCell(
                grid.GetSoldierPosition(shooterId),
                grid.GetSoldierPosition(targetId));
            if (margin >= 0)
            {
                return aimCell;
            }

            double scatterDistance = -margin * ScatterDistancePerPoint;
            double angle = directionRoll * 2.0 * Math.PI;
            int impactX = (int)Math.Round(aimCell.Item1 + (scatterDistance * Math.Cos(angle)));
            int impactY = (int)Math.Round(aimCell.Item2 + (scatterDistance * Math.Sin(angle)));
            return new ValueTuple<int, int>(impactX, impactY);
        }

        /// <summary>
        /// Every soldier with ANY footprint cell within <paramref name="areaRadius"/> of
        /// the impact cell, ordered by soldier id. Includes the thrower when caught.
        /// Queries the grid's cell-occupancy map over the blast disc, so cost scales
        /// with the template area rather than the number of soldiers on the field.
        /// </summary>
        public static IReadOnlyList<BlastVictim> GetVictims(
            BattleGridManager grid,
            ValueTuple<int, int> impactCell,
            float areaRadius)
        {
            ArgumentNullException.ThrowIfNull(grid);
            ArgumentNullException.ThrowIfNull(impactCell);

            if (areaRadius <= 0)
            {
                return [];
            }

            DiscOffset[] disc = GetDiscOffsets(areaRadius);
            Dictionary<int, float> nearestBySoldier = [];
            foreach (DiscOffset offset in disc)
            {
                int? occupant = grid.GetCellOccupant(
                    impactCell.Item1 + offset.DeltaX,
                    impactCell.Item2 + offset.DeltaY);
                if (occupant.HasValue)
                {
                    // Offsets are sorted by distance, so the first cell seen for a
                    // soldier is their nearest caught cell.
                    nearestBySoldier.TryAdd(occupant.Value, offset.Distance);
                }
            }

            return nearestBySoldier
                .OrderBy(entry => entry.Key)
                .Select(entry => new BlastVictim(entry.Key, entry.Value))
                .ToList();
        }

        private readonly record struct DiscOffset(int DeltaX, int DeltaY, float Distance);

        private static readonly Dictionary<float, DiscOffset[]> _discOffsetCache = [];

        /// <summary>All integer cell offsets within <paramref name="areaRadius"/> of the
        /// origin, sorted nearest-first. Memoized per radius (weapon data uses a handful).</summary>
        private static DiscOffset[] GetDiscOffsets(float areaRadius)
        {
            lock (_discOffsetCache)
            {
                if (_discOffsetCache.TryGetValue(areaRadius, out DiscOffset[] cached))
                {
                    return cached;
                }

                int cellRadius = (int)Math.Floor(areaRadius);
                float radiusSquared = areaRadius * areaRadius;
                List<DiscOffset> offsets = [];
                for (int deltaX = -cellRadius; deltaX <= cellRadius; deltaX++)
                {
                    for (int deltaY = -cellRadius; deltaY <= cellRadius; deltaY++)
                    {
                        float distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                        if (distanceSquared <= radiusSquared)
                        {
                            offsets.Add(new DiscOffset(deltaX, deltaY, (float)Math.Sqrt(distanceSquared)));
                        }
                    }
                }

                DiscOffset[] result = offsets.OrderBy(offset => offset.Distance).ToArray();
                _discOffsetCache[areaRadius] = result;
                return result;
            }
        }

        private static ValueTuple<int, int> FindNearestCell(
            IEnumerable<ValueTuple<int, int>> shooterCells,
            IEnumerable<ValueTuple<int, int>> targetCells)
        {
            ValueTuple<int, int>? bestTarget = null;
            long bestDistanceSquared = long.MaxValue;

            // Ties break to the lexicographically smallest target cell so the result is
            // deterministic without sorting the (tiny) footprints on every call.
            foreach (ValueTuple<int, int> shooterCell in shooterCells)
            {
                foreach (ValueTuple<int, int> targetCell in targetCells)
                {
                    long deltaX = targetCell.Item1 - shooterCell.Item1;
                    long deltaY = targetCell.Item2 - shooterCell.Item2;
                    long distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceSquared < bestDistanceSquared
                        || (distanceSquared == bestDistanceSquared && IsLexicographicallySmaller(targetCell, bestTarget)))
                    {
                        bestTarget = targetCell;
                        bestDistanceSquared = distanceSquared;
                    }
                }
            }

            if (bestTarget == null)
            {
                throw new ArgumentException("Shooter and target must each occupy at least one grid cell.");
            }

            return bestTarget.Value;
        }

        private static bool IsLexicographicallySmaller(ValueTuple<int, int>? candidate, ValueTuple<int, int>? incumbent)
        {
            return candidate?.Item1 < incumbent?.Item1
                || (candidate?.Item1 == incumbent?.Item1 && candidate?.Item2 < incumbent?.Item2);
        }
    }
}
