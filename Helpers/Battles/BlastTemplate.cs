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
        public static Tuple<int, int> ResolveImpactCell(
            BattleGridManager grid,
            int shooterId,
            int targetId,
            float margin,
            double directionRoll)
        {
            ArgumentNullException.ThrowIfNull(grid);

            Tuple<int, int> aimCell = FindNearestCell(
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
            return new Tuple<int, int>(impactX, impactY);
        }

        /// <summary>
        /// Every soldier with ANY footprint cell within <paramref name="areaRadius"/> of
        /// the impact cell, ordered by soldier id. Includes the thrower when caught.
        /// </summary>
        public static IReadOnlyList<BlastVictim> GetVictims(
            BattleGridManager grid,
            Tuple<int, int> impactCell,
            float areaRadius)
        {
            ArgumentNullException.ThrowIfNull(grid);
            ArgumentNullException.ThrowIfNull(impactCell);

            if (areaRadius <= 0)
            {
                return [];
            }

            List<BlastVictim> victims = [];
            foreach (KeyValuePair<int, IList<Tuple<int, int>>> entry in grid.GetSoldierPositions())
            {
                float nearestDistance = float.MaxValue;
                foreach (Tuple<int, int> cell in entry.Value)
                {
                    float deltaX = cell.Item1 - impactCell.Item1;
                    float deltaY = cell.Item2 - impactCell.Item2;
                    float distance = (float)Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                    }
                }

                if (nearestDistance <= areaRadius)
                {
                    victims.Add(new BlastVictim(entry.Key, nearestDistance));
                }
            }

            return victims.OrderBy(victim => victim.SoldierId).ToList();
        }

        private static Tuple<int, int> FindNearestCell(
            IEnumerable<Tuple<int, int>> shooterCells,
            IEnumerable<Tuple<int, int>> targetCells)
        {
            Tuple<int, int> bestTarget = null;
            long bestDistanceSquared = long.MaxValue;

            foreach (Tuple<int, int> shooterCell in shooterCells.OrderBy(cell => cell.Item1).ThenBy(cell => cell.Item2))
            {
                foreach (Tuple<int, int> targetCell in targetCells.OrderBy(cell => cell.Item1).ThenBy(cell => cell.Item2))
                {
                    long deltaX = targetCell.Item1 - shooterCell.Item1;
                    long deltaY = targetCell.Item2 - shooterCell.Item2;
                    long distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceSquared < bestDistanceSquared)
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

            return bestTarget;
        }
    }
}
