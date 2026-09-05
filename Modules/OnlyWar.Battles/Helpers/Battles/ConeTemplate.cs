using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Resolves the figures covered by a cone whose direction is set by an aimed-at figure.
    /// </summary>
    public static class ConeTemplate
    {
        private const double NozzleHalfWidth = 0.5;

        public static IReadOnlyList<int> GetVictimIds(
            BattleGridManager grid,
            int shooterId,
            int targetId,
            float maximumRange,
            float areaRadius)
        {
            ArgumentNullException.ThrowIfNull(grid);

            if (maximumRange <= 0)
            {
                return [];
            }

            (ValueTuple<int, int> origin, ValueTuple<int, int> aimPoint) = FindNearestCellPair(
                grid.GetSoldierPosition(shooterId),
                grid.GetSoldierPosition(targetId));

            double aimX = aimPoint.Item1 - origin.Item1;
            double aimY = aimPoint.Item2 - origin.Item2;
            double aimLength = Math.Sqrt((aimX * aimX) + (aimY * aimY));
            if (aimLength <= 0)
            {
                return [];
            }

            double directionX = aimX / aimLength;
            double directionY = aimY / aimLength;
            return grid.GetSoldierPositions()
                .Where(entry => entry.Key != shooterId
                    && entry.Value.Any(cell => IsCellInside(
                        cell,
                        origin,
                        directionX,
                        directionY,
                        maximumRange,
                        areaRadius)))
                .Select(entry => entry.Key)
                .OrderBy(id => id)
                .ToList();
        }

        private static bool IsCellInside(
            ValueTuple<int, int> cell,
            ValueTuple<int, int> origin,
            double directionX,
            double directionY,
            double maximumRange,
            double areaRadius)
        {
            double relativeX = cell.Item1 - origin.Item1;
            double relativeY = cell.Item2 - origin.Item2;
            double axialDistance = (relativeX * directionX) + (relativeY * directionY);
            if (axialDistance <= 0 || axialDistance > maximumRange)
            {
                return false;
            }

            double lateralDistance = Math.Abs((relativeX * directionY) - (relativeY * directionX));
            double allowedHalfWidth = NozzleHalfWidth
                + ((areaRadius - NozzleHalfWidth) * axialDistance / maximumRange);
            return lateralDistance <= allowedHalfWidth;
        }

        private static (ValueTuple<int, int> First, ValueTuple<int, int> Second) FindNearestCellPair(
            IEnumerable<ValueTuple<int, int>> firstCells,
            IEnumerable<ValueTuple<int, int>> secondCells)
        {
            ValueTuple<int, int>? bestFirst = null;
            ValueTuple<int, int>? bestSecond = null;
            long bestDistanceSquared = long.MaxValue;

            foreach (ValueTuple<int, int> first in firstCells.OrderBy(cell => cell.Item1).ThenBy(cell => cell.Item2))
            {
                foreach (ValueTuple<int, int> second in secondCells.OrderBy(cell => cell.Item1).ThenBy(cell => cell.Item2))
                {
                    long deltaX = second.Item1 - first.Item1;
                    long deltaY = second.Item2 - first.Item2;
                    long distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestFirst = first;
                        bestSecond = second;
                        bestDistanceSquared = distanceSquared;
                    }
                }
            }

            if (bestFirst == null || bestSecond == null)
            {
                throw new ArgumentException("Shooter and target must each occupy at least one grid cell.");
            }

            return (bestFirst.Value, bestSecond.Value);
        }
    }
}
