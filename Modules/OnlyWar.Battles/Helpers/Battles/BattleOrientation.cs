using System;
using System.Collections.Generic;

namespace OnlyWar.Battles
{
    /// <summary>
    /// Eight-way battle facing. Zero points toward increasing Y and headings advance in
    /// 45-degree steps. Grid footprints remain axis-aligned; only due-east/due-west facings
    /// swap a non-square figure's width and depth.
    /// </summary>
    public static class BattleOrientation
    {
        public const ushort HeadingCount = 8;

        public static bool IsFootprintRotated(ushort orientation)
        {
            ushort heading = (ushort)(orientation % HeadingCount);
            return heading == 2 || heading == 6;
        }

        /// <summary>
        /// The grid cells a soldier covers with its top-left at <paramref name="topLeft"/>. The
        /// top-left is itself a covered cell, and the footprint runs +x and -y from it. This is
        /// the one footprint convention: placement, movement and melee contact all use it.
        /// </summary>
        public static List<ValueTuple<int, int>> GetFootprint(
            BattleSoldier soldier,
            ValueTuple<int, int> topLeft,
            ushort orientation)
        {
            List<ValueTuple<int, int>> cells = [];
            int width = IsFootprintRotated(orientation)
                ? soldier.Soldier.Template.Species.Depth
                : soldier.Soldier.Template.Species.Width;
            int depth = IsFootprintRotated(orientation)
                ? soldier.Soldier.Template.Species.Width
                : soldier.Soldier.Template.Species.Depth;
            for (int w = 0; w < width; w++)
            {
                for (int d = 0; d < depth; d++)
                {
                    cells.Add(new ValueTuple<int, int>(
                        (short)(topLeft.Item1 + w), (short)(topLeft.Item2 - d)));
                }
            }
            return cells;
        }

        /// <summary>
        /// Melee contact between two placed soldiers: some cell of one is orthogonally beside
        /// some cell of the other. Diagonals do not count, as in Grid.GetAdjacentObjects.
        /// </summary>
        public static bool AreInContact(BattleSoldier first, BattleSoldier second)
        {
            if (first.TopLeft is not ValueTuple<int, int> firstTopLeft
                || second.TopLeft is not ValueTuple<int, int> secondTopLeft)
            {
                return false;
            }
            HashSet<ValueTuple<int, int>> secondCells =
                [.. GetFootprint(second, secondTopLeft, second.Orientation)];
            foreach (ValueTuple<int, int> cell in
                GetFootprint(first, firstTopLeft, first.Orientation))
            {
                if (secondCells.Contains((cell.Item1 - 1, cell.Item2))
                    || secondCells.Contains((cell.Item1 + 1, cell.Item2))
                    || secondCells.Contains((cell.Item1, cell.Item2 - 1))
                    || secondCells.Contains((cell.Item1, cell.Item2 + 1)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
