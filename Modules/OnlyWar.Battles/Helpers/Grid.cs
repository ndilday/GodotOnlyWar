using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers
{
    public class Grid
    {
        private readonly Dictionary<(int X, int Y), int> _cellObjectMap;
        private readonly Dictionary<int, IList<ValueTuple<int, int>>> _objectCellsMap;
        private readonly HashSet<ValueTuple<int, int>> _reservedCells;

        public Grid()
        {
            _cellObjectMap = [];
            _objectCellsMap = [];
            _reservedCells = [];
        }

        public void OccupyCells(IList<ValueTuple<int, int>> cells, int objectId)
        {
            // confirm all desired cells are free
            foreach (ValueTuple<int, int> cell in cells)
            {
                if (_cellObjectMap.ContainsKey((cell.Item1, cell.Item2)))
                {
                    throw new InvalidOperationException($"Cell {cell} is already occupied.");
                }
            }
            foreach (ValueTuple<int, int> cell in cells)
            {
                _cellObjectMap[(cell.Item1, cell.Item2)] = objectId;
            }
            _objectCellsMap[objectId] = cells;
        }

        public void OccupyCell(ValueTuple<int, int> cell, int objectId)
        {
            if (_cellObjectMap.ContainsKey((cell.Item1, cell.Item2)))
            {
                throw new InvalidOperationException($"Cell {cell} is already occupied.");
            }
            _cellObjectMap[(cell.Item1, cell.Item2)] = objectId;
            _objectCellsMap[objectId] = [cell];
        }

        public void FreeCells(IEnumerable<ValueTuple<int, int>> cells)
        {
            foreach (ValueTuple<int, int> cell in cells)
            {
                _cellObjectMap.Remove((cell.Item1, cell.Item2));
            }
        }

        public int? GetCellObject(ValueTuple<int, int> cell)
        {
            return GetCellObject(cell.Item1, cell.Item2);
        }

        public int? GetCellObject(int x, int y)
        {
            return _cellObjectMap.TryGetValue((x, y), out int objectId)
                ? objectId
                : null;
        }

        public IList<ValueTuple<int, int>> GetObjectCells(int objectId)
        {
            if (!_objectCellsMap.ContainsKey(objectId))
            {
                return null;
            }
            return _objectCellsMap[objectId];
        }

        /// <summary>
        /// Returns the objects occupying cardinally adjacent cells around an object's footprint.
        /// Battle melee adjacency uses a Euclidean threshold just over one grid unit, so diagonal
        /// cells are intentionally excluded.
        /// </summary>
        public IReadOnlyCollection<int> GetAdjacentObjects(int objectId)
        {
            if (!_objectCellsMap.TryGetValue(objectId, out IList<ValueTuple<int, int>> cells))
            {
                return Array.Empty<int>();
            }

            HashSet<int> adjacentObjects = [];
            foreach (ValueTuple<int, int> cell in cells)
            {
                AddOccupier(cell.Item1 - 1, cell.Item2);
                AddOccupier(cell.Item1 + 1, cell.Item2);
                AddOccupier(cell.Item1, cell.Item2 - 1);
                AddOccupier(cell.Item1, cell.Item2 + 1);
            }

            adjacentObjects.Remove(objectId);
            return adjacentObjects;

            void AddOccupier(int x, int y)
            {
                if (_cellObjectMap.TryGetValue((x, y), out int adjacentObjectId))
                {
                    adjacentObjects.Add(adjacentObjectId);
                }
            }
        }

        public bool IsCellReserved(ValueTuple<int, int> cell)
        {
            return _reservedCells.Contains(cell);
        }

        public void ReserveCell(ValueTuple<int, int> cell)
        {
            _reservedCells.Add(cell);
        }

        public void UnreserveCell(ValueTuple<int, int> cell)
        {
            _reservedCells.Remove(cell);
        }

        public void ClearReservedCells()
        {
            _reservedCells.Clear();
        }

        public HashSet<ValueTuple<int, int>> GetReservedCells()
        {
            return _reservedCells;
        }
    }

}
