using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers
{
    public class Grid
    {
        private readonly Dictionary<Tuple<int, int>, int> _cellObjectMap;
        private readonly Dictionary<int, IList<Tuple<int, int>>> _objectCellsMap;
        private readonly HashSet<Tuple<int, int>> _reservedCells;

        public Grid()
        {
            _cellObjectMap = [];
            _objectCellsMap = [];
            _reservedCells = [];
        }

        public void OccupyCells(IList<Tuple<int, int>> cells, int objectId)
        {
            // confirm all desired cells are free
            foreach (Tuple<int, int> cell in cells)
            {
                if (_cellObjectMap.ContainsKey(cell))
                {
                    throw new InvalidOperationException($"Cell {cell} is already occupied.");
                }
            }
            foreach (Tuple<int, int> cell in cells)
            {
                _cellObjectMap[cell] = objectId;
            }
            _objectCellsMap[objectId] = cells;
        }

        public void OccupyCell(Tuple<int, int> cell, int objectId)
        {
            if (_cellObjectMap.ContainsKey(cell))
            {
                throw new InvalidOperationException($"Cell {cell} is already occupied.");
            }
            _cellObjectMap[cell] = objectId;
            _objectCellsMap[objectId] = new List<Tuple<int, int>> { cell };
        }

        public void FreeCells(IEnumerable<Tuple<int, int>> cells)
        {
            foreach (Tuple<int, int> cell in cells)
            {
                _cellObjectMap.Remove(cell);
            }
        }

        public int? GetCellObject(Tuple<int, int> cell)
        {
            return _cellObjectMap.ContainsKey(cell) ? _cellObjectMap[cell] : null;
        }

        public IList<Tuple<int, int>> GetObjectCells(int objectId)
        {
            if (!_objectCellsMap.ContainsKey(objectId))
            {
                return null;
            }
            return _objectCellsMap[objectId];
        }

        public bool IsCellReserved(Tuple<int, int> cell)
        {
            return _reservedCells.Contains(cell);
        }

        public void ReserveCell(Tuple<int, int> cell)
        {
            _reservedCells.Add(cell);
        }

        public void UnreserveCell(Tuple<int, int> cell)
        {
            _reservedCells.Remove(cell);
        }
    }

}
