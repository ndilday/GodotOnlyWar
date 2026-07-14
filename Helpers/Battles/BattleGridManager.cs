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
        readonly private Dictionary<(int FirstId, int SecondId), float> _distanceCache;
        readonly private Dictionary<int, (float Distance, int SoldierId)> _nearestEnemyCache;
        readonly private Dictionary<int, IReadOnlyList<int>> _adjacentSoldierCache;
        readonly private Dictionary<(bool ShooterSide, int TargetId), bool> _engagementCache;
        readonly private Dictionary<int, IReadOnlyList<int>> _meleeScrumCache;

        public BattleGridManager()
        {
            _soldiers = [];
            _soldierPositionsMap = [];
            _soldierSideMap = [];
            _grid = new Grid();
            _distanceCache = [];
            _nearestEnemyCache = [];
            _adjacentSoldierCache = [];
            _engagementCache = [];
            _meleeScrumCache = [];
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
            InvalidateLayoutQueries();
        }

        public void MoveSoldier(BattleSoldier soldier, Tuple<int, int> newTopLeft, ushort newOrientation)
        {
            List<Tuple<int, int>> newLocation = GetSoldierFootprint(soldier, newTopLeft, newOrientation);
            EnsureMoveAvailable(soldier, newLocation);

            _grid.FreeCells(_soldierPositionsMap[soldier.Soldier.Id]);
            _grid.OccupyCells(newLocation, soldier.Soldier.Id);
            _soldierPositionsMap[soldier.Soldier.Id] = newLocation;
            InvalidateLayoutQueries();
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
            InvalidateLayoutQueries();
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
                InvalidateLayoutQueries();
            }
        }

        public bool IsAdjacentToEnemy(int soldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            bool soldierSide = _soldierSideMap[soldierId];
            return GetAdjacentSoldiers(soldierId)
                .Any(adjacentId => _soldierSideMap[adjacentId] != soldierSide);
        }

        public IReadOnlyList<int> GetAdjacentEnemies(int soldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            bool soldierTeam = _soldierSideMap[soldierId];
            return GetAdjacentSoldiers(soldierId)
                .Where(otherSoldierId => _soldierSideMap[otherSoldierId] != soldierTeam)
                .ToList();
        }

        /// <summary>
        /// Returns every figure whose footprint is within melee distance of the supplied figure,
        /// regardless of tactical side.
        /// </summary>
        public IReadOnlyList<int> GetAdjacentSoldiers(int soldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            if (_adjacentSoldierCache.TryGetValue(soldierId, out IReadOnlyList<int> cached))
            {
                return cached;
            }

            List<int> adjacentSoldiers = _grid.GetAdjacentObjects(soldierId)
                .Where(_soldiers.ContainsKey)
                .OrderBy(otherSoldierId => GetDistanceBetweenSoldiers(soldierId, otherSoldierId))
                .ThenBy(otherSoldierId => otherSoldierId)
                .ToList();
            _adjacentSoldierCache[soldierId] = adjacentSoldiers;
            return adjacentSoldiers;
        }

        /// <summary>
        /// Whether the target is in base contact with any member of the shooter's tactical side.
        /// The shooter counts when firing while personally engaged.
        /// </summary>
        public bool IsTargetEngagedWithShootersAllies(int shooterId, int targetId)
        {
            if (!_soldiers.ContainsKey(shooterId) || !_soldiers.ContainsKey(targetId))
            {
                throw new ArgumentException("Soldier not found");
            }

            bool shooterSide = _soldierSideMap[shooterId];
            var cacheKey = (shooterSide, targetId);
            if (!_engagementCache.TryGetValue(cacheKey, out bool isEngaged))
            {
                isEngaged = GetAdjacentSoldiers(targetId)
                    .Any(adjacentId => _soldierSideMap[adjacentId] == shooterSide);
                _engagementCache[cacheKey] = isEngaged;
            }
            return isEngaged;
        }

        /// <summary>
        /// Finds the connected melee scrum containing <paramref name="soldierId"/>. Connections
        /// only cross tactical sides, so a rank of merely adjacent allies does not enlarge the
        /// stray-shot pool. The supplied soldier is included in the result.
        /// </summary>
        public IReadOnlyList<int> GetMeleeScrumParticipants(int soldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            if (_meleeScrumCache.TryGetValue(soldierId, out IReadOnlyList<int> cached))
            {
                return cached;
            }

            HashSet<int> participants = [soldierId];
            Queue<int> frontier = new();
            frontier.Enqueue(soldierId);

            while (frontier.Count > 0)
            {
                int currentId = frontier.Dequeue();
                bool currentSide = _soldierSideMap[currentId];
                foreach (int adjacentId in GetAdjacentSoldiers(currentId))
                {
                    if (_soldierSideMap[adjacentId] == currentSide || !participants.Add(adjacentId))
                    {
                        continue;
                    }

                    frontier.Enqueue(adjacentId);
                }
            }

            IReadOnlyList<int> result = participants.OrderBy(id => id).ToList();
            foreach (int participantId in result)
            {
                _meleeScrumCache[participantId] = result;
            }
            return result;
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

            if (_nearestEnemyCache.TryGetValue(soldierId, out var cached))
            {
                closestSoldierId = cached.SoldierId;
                return cached.Distance;
            }

            bool soldierTeam = _soldierSideMap[soldierId];
            closestSoldierId = -1;
            float distance = (float)Math.Sqrt(float.MaxValue);
            foreach (KeyValuePair<int, IList<Tuple<int, int>>> kvp in _soldierPositionsMap)
            {
                if (_soldierSideMap[kvp.Key] != soldierTeam)
                {
                    float candidateDistance = GetDistanceBetweenSoldiers(soldierId, kvp.Key);
                    if (candidateDistance < distance)
                    {
                        distance = candidateDistance;
                        closestSoldierId = kvp.Key;
                    }
                }
            }

            _nearestEnemyCache[soldierId] = (distance, closestSoldierId);
            return distance;
        }

        public float GetDistanceBetweenSoldiers(int soldierId1, int soldierId2)
        {
            var cacheKey = soldierId1 <= soldierId2
                ? (soldierId1, soldierId2)
                : (soldierId2, soldierId1);
            if (_distanceCache.TryGetValue(cacheKey, out float cached))
            {
                return cached;
            }

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
            float distance = (float)Math.Sqrt(distanceSq);
            _distanceCache[cacheKey] = distance;
            return distance;
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
            long xDistance = pos1.Item1 - pos2.Item1;
            long yDistance = pos1.Item2 - pos2.Item2;
            return (xDistance * xDistance) + (yDistance * yDistance);
        }

        private void InvalidateLayoutQueries()
        {
            _distanceCache.Clear();
            _nearestEnemyCache.Clear();
            _adjacentSoldierCache.Clear();
            _engagementCache.Clear();
            _meleeScrumCache.Clear();
        }
    }
}
