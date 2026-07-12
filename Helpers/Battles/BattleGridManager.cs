using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    public class BattleGridManager : ICloneable
    {
        readonly private Dictionary<int, BattleSoldier> _soldiers;
        readonly private Dictionary<int, IList<Tuple<int, int>>> _soldierPositionsMap;
        readonly private Dictionary<int, bool> _soldierSideMap;
        readonly private Grid _grid;

        public BattleGridManager()
        {
            _soldiers = [];
            _soldierPositionsMap = [];
            _soldierSideMap = [];
            _grid = new Grid();
        }

        public object Clone()
        {
            var copy = new BattleGridManager();
            foreach (BattleSoldier soldier in _soldiers.Values)
            {
                copy.PlaceSoldier(soldier, _soldierSideMap[soldier.Soldier.Id], _soldierPositionsMap[soldier.Soldier.Id]);
            }
            foreach(Tuple<int, int> cell in _grid.GetReservedCells())
            {
                copy.ReserveSpace(cell);
            }
            return copy;
        }

        public void PlaceSoldier(BattleSoldier soldier, bool side, IList<Tuple<int, int>> cells)
        {
            if (_soldiers.ContainsKey(soldier.Soldier.Id))
            {
                throw new InvalidOperationException($"Soldier {soldier.Soldier.Id} is already placed.");
            }

            foreach (Tuple<int,int> cell in cells)
            {
                if (_grid.GetCellObject(cell) != null || _grid.IsCellReserved(cell))
                {
                    throw new InvalidOperationException($"Cannot place soldier {soldier.Soldier.Id} at {cell}. Cell is occupied or reserved.");
                }
            }

            _soldiers[soldier.Soldier.Id] = soldier;
            _soldierSideMap[soldier.Soldier.Id] = side;
            _grid.OccupyCells(cells, soldier.Soldier.Id);
            _soldierPositionsMap[soldier.Soldier.Id] = cells;
        }

        public void MoveSoldier(BattleSoldier soldier, Tuple<int, int> newTopLeft, ushort newOrientation)
        {
            List<Tuple<int, int>> newLocation = GetSoldierFootprint(soldier, newTopLeft, newOrientation);
            EnsureMoveAvailable(soldier, newLocation);

            _grid.FreeCells(_soldierPositionsMap[soldier.Soldier.Id]);
            _grid.OccupyCells(newLocation, soldier.Soldier.Id);
            _soldierPositionsMap[soldier.Soldier.Id] = newLocation;
        }

        public bool TryMoveSoldier(BattleSoldier soldier, Tuple<int, int> newTopLeft, ushort newOrientation)
        {
            List<Tuple<int, int>> newLocation = GetSoldierFootprint(soldier, newTopLeft, newOrientation);
            if (!CanMoveTo(soldier, newLocation))
            {
                return false;
            }

            _grid.FreeCells(_soldierPositionsMap[soldier.Soldier.Id]);
            _grid.OccupyCells(newLocation, soldier.Soldier.Id);
            _soldierPositionsMap[soldier.Soldier.Id] = newLocation;
            return true;
        }

        private List<Tuple<int, int>> GetSoldierFootprint(BattleSoldier soldier, Tuple<int, int> topLeft, ushort orientation)
        {
            List<Tuple<int, int>> cells = [];
            int width;
            int depth;
            if (orientation % 2 == 0)
            {
                width = soldier.Soldier.Template.Species.Width;
                depth = soldier.Soldier.Template.Species.Depth;
            }
            else
            {
                width = soldier.Soldier.Template.Species.Depth;
                depth = soldier.Soldier.Template.Species.Width;
            }

            for (int w = 0; w < width; w++)
            {
                for (int d = 0; d < depth; d++)
                {
                    cells.Add(new Tuple<int, int>((short)(topLeft.Item1 + w), (short)(topLeft.Item2 - d)));
                }
            }
            return cells;
        }

        private bool CanMoveTo(BattleSoldier soldier, IEnumerable<Tuple<int, int>> cells)
        {
            foreach (Tuple<int, int> location in cells)
            {
                int? occupier = _grid.GetCellObject(location);
                if (occupier != null && occupier != soldier.Soldier.Id)
                {
                    return false;
                }
            }
            return true;
        }

        private void EnsureMoveAvailable(BattleSoldier soldier, IEnumerable<Tuple<int, int>> cells)
        {
            foreach (Tuple<int, int> location in cells)
            {
                int? occupier = _grid.GetCellObject(location);
                if (occupier != null && occupier != soldier.Soldier.Id)
                {
                    throw new InvalidOperationException($"Soldier {soldier.Soldier.Id} cannot move to {location.Item1},{location.Item2}; already occupied by Soldier {occupier}");
                }
            }
        }

        public void RemoveSoldier(int soldierId)
        {
            if (_soldiers.ContainsKey(soldierId))
            {
                _grid.FreeCells(_soldierPositionsMap[soldierId]);
                _soldiers.Remove(soldierId);
                _soldierPositionsMap.Remove(soldierId);
                _soldierSideMap.Remove(soldierId);
            }
        }

        public bool IsAdjacentToEnemy(int soldierId)
        {
            float distance = GetNearestEnemy(soldierId, out int closestSoldierId);
            return closestSoldierId != -1 && distance <= 1.001f;
        }

        public IReadOnlyList<int> GetAdjacentEnemies(int soldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            bool soldierTeam = _soldierSideMap[soldierId];
            List<Tuple<int, float>> adjacentEnemies = [];
            foreach (int otherSoldierId in _soldierPositionsMap.Keys)
            {
                if (_soldierSideMap[otherSoldierId] == soldierTeam)
                {
                    continue;
                }

                float distance = GetDistanceBetweenSoldiers(soldierId, otherSoldierId);
                if (distance <= 1.001f)
                {
                    adjacentEnemies.Add(new Tuple<int, float>(otherSoldierId, distance));
                }
            }

            return adjacentEnemies
                .OrderBy(tuple => tuple.Item2)
                .ThenBy(tuple => tuple.Item1)
                .Select(tuple => tuple.Item1)
                .ToList();
        }

        public bool GetSoldierSide(int soldierId)
        {
            if (!_soldierSideMap.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }
            return _soldierSideMap[soldierId];
        }

        public float GetNearestEnemy(int soldierId, out int closestSoldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }
            //var targetSet = _playerSoldierIds.Contains(id) ? _opposingSoldierIds : _playerSoldierIds;
            IList<Tuple<int, int>> startLocations = _grid.GetObjectCells(soldierId);
            bool soldierTeam = _soldierSideMap[soldierId];
            closestSoldierId = -1;
            float distanceSq = float.MaxValue;
            foreach (KeyValuePair<int, IList<Tuple<int, int>>> kvp in _soldierPositionsMap)
            {
                if (_soldierSideMap[kvp.Key] != soldierTeam)
                {
                    foreach (Tuple<int, int> tuple in kvp.Value)
                    {
                        foreach (Tuple<int, int> soldierTuple in startLocations)
                        {
                            float tempDistance = CalculateDistanceSq(soldierTuple, tuple);
                            if (tempDistance < distanceSq)
                            {
                                distanceSq = tempDistance;
                                closestSoldierId = kvp.Key;
                            }
                        }
                    }
                }
            }
            return (float)Math.Sqrt(distanceSq);
        }

        public float GetDistanceBetweenSoldiers(int soldierId1, int soldierId2)
        {
            IList<Tuple<int, int>> pos1 = _soldierPositionsMap[soldierId1];
            IList<Tuple<int, int>> pos2 = _soldierPositionsMap[soldierId2];
            float distanceSq = int.MaxValue;
            foreach (Tuple<int, int> tuple1 in pos1)
            {
                foreach (Tuple<int, int> tuple2 in pos2)
                {
                    float tempDistance = CalculateDistanceSq(tuple1, tuple2);
                    if (tempDistance < distanceSq)
                    {
                        distanceSq = tempDistance;
                    }
                }
            }
            return (float)Math.Sqrt(distanceSq);
        }

        public IList<Tuple<int, int>> GetSoldierPosition(int soldierId)
        {
            return _grid.GetObjectCells(soldierId);
        }

        public IReadOnlyDictionary<int, IList<Tuple<int, int>>> GetSoldierPositions()
        {
            return _soldierPositionsMap;
        }

        public Tuple<int, int> GetClosestOpenAdjacency(Tuple<int, int> startingPoint, Tuple<int, int> target)
        {
            Tuple<int, int> bestPosition = null;
            float bestDistance = float.MaxValue;
            float disSq;
            Tuple<int, int>[] testPositions = 
                {
                    new Tuple<int, int>(target.Item1, (short)(target.Item2 - 1)),
                    new Tuple<int, int>(target.Item1, (short)(target.Item2 + 1)),
                    new Tuple<int, int>((short)(target.Item1 - 1), target.Item2),
                    new Tuple<int, int>((short)(target.Item1 + 1), target.Item2)
                };
            foreach (Tuple<int, int> testPosition in testPositions)
            {
                if (_grid.GetCellObject(testPosition) == null && !_grid.IsCellReserved(testPosition))
                {
                    disSq = CalculateDistanceSq(startingPoint, testPosition);
                    if (disSq < bestDistance)
                    {
                        bestDistance = disSq;
                        bestPosition = testPosition;
                    }
                }
            }
            return bestPosition;
        }

        public void ReserveSpace(Tuple<int, int> location)
        {
            _grid.ReserveCell(location);
        }

        public void ClearReservations()
        {
            _grid.ClearReservedCells();
        }

        public bool IsSpaceAvailable(Tuple<int, int> location)
        {
            return !_grid.IsCellReserved(location) && _grid.GetCellObject(location) == null;
        }

        private float CalculateDistanceSq(Tuple<int, int> pos1, Tuple<int, int> pos2)
        {
            // for now, as a quick good-enough, just look at the difference in coordinates
            return (float)(Math.Pow(pos1.Item1 - pos2.Item1, 2) + Math.Pow(pos1.Item2 - pos2.Item2, 2));
        }
    }
}
