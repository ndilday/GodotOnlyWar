using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    public class BattleGridManager : ICloneable
    {
        readonly private Dictionary<int, BattleSoldier> _soldiers;
        readonly private Dictionary<int, IList<ValueTuple<int, int>>> _soldierPositionsMap;
        readonly private Dictionary<int, bool> _soldierSideMap;
        readonly private Grid _grid;
        readonly private Dictionary<int, int> _soldierPositionRevisions;
        readonly private Dictionary<int, int> _squadPositionRevisions;
        readonly private Dictionary<bool, int> _sidePositionRevisions;
        readonly private Dictionary<int, int> _soldierPlacementOrder;
        readonly private Dictionary<(int FirstId, int SecondId),
            (float Distance, int FirstRevision, int SecondRevision)> _distanceCache;
        readonly private Dictionary<int,
            (IReadOnlyList<(int SoldierId, float Distance)> Enemies,
             int SoldierRevision,
             int EnemySideRevision)> _enemyProximityCache;
        readonly private Dictionary<(int FirstSquadId, int SecondSquadId),
            (float Distance, int FirstRevision, int SecondRevision)> _squadDistanceCache;
        readonly private Dictionary<(int SquadId, int SoldierId),
            (float Distance, int SquadRevision, int SoldierRevision)> _squadToSoldierDistanceCache;
        readonly private Dictionary<int, IReadOnlyList<int>> _adjacentSoldierCache;
        readonly private Dictionary<(bool ShooterSide, int TargetId), bool> _engagementCache;
        readonly private Dictionary<int, IReadOnlyList<int>> _meleeScrumCache;
        private int _nextPositionRevision;
        private int _nextPlacementOrder;

        public BattleGridManager()
        {
            _soldiers = [];
            _soldierPositionsMap = [];
            _soldierSideMap = [];
            _grid = new Grid();
            _soldierPositionRevisions = [];
            _squadPositionRevisions = [];
            _sidePositionRevisions = [];
            _soldierPlacementOrder = [];
            _distanceCache = [];
            _enemyProximityCache = [];
            _squadDistanceCache = [];
            _squadToSoldierDistanceCache = [];
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
            foreach(ValueTuple<int, int> cell in _grid.GetReservedCells())
            {
                copy.ReserveSpace(cell);
            }
            return copy;
        }

        public void PlaceSoldier(BattleSoldier soldier, bool side, IList<ValueTuple<int, int>> cells)
        {
            if (_soldiers.ContainsKey(soldier.Soldier.Id))
            {
                throw new InvalidOperationException($"Soldier {soldier.Soldier.Id} is already placed.");
            }

            foreach (ValueTuple<int,int> cell in cells)
            {
                if (_grid.GetCellObject(cell) != null || _grid.IsCellReserved(cell))
                {
                    throw new InvalidOperationException($"Cannot place soldier {soldier.Soldier.Id} at {cell}. Cell is occupied or reserved.");
                }
            }

            _soldiers[soldier.Soldier.Id] = soldier;
            _soldierSideMap[soldier.Soldier.Id] = side;
            _soldierPlacementOrder[soldier.Soldier.Id] = _nextPlacementOrder++;
            _grid.OccupyCells(cells, soldier.Soldier.Id);
            _soldierPositionsMap[soldier.Soldier.Id] = cells;
            TouchPositionRevisions(soldier);
            InvalidateLayoutQueries();
        }

        public void MoveSoldier(BattleSoldier soldier, ValueTuple<int, int> newTopLeft, ushort newOrientation)
        {
            List<ValueTuple<int, int>> newLocation = GetSoldierFootprint(soldier, newTopLeft, newOrientation);
            EnsureMoveAvailable(soldier, newLocation);

            _grid.FreeCells(_soldierPositionsMap[soldier.Soldier.Id]);
            _grid.OccupyCells(newLocation, soldier.Soldier.Id);
            _soldierPositionsMap[soldier.Soldier.Id] = newLocation;
            TouchPositionRevisions(soldier);
            InvalidateLayoutQueries();
        }

        public bool TryMoveSoldier(BattleSoldier soldier, ValueTuple<int, int> newTopLeft, ushort newOrientation)
        {
            List<ValueTuple<int, int>> newLocation = GetSoldierFootprint(soldier, newTopLeft, newOrientation);
            if (!CanMoveTo(soldier, newLocation))
            {
                return false;
            }

            _grid.FreeCells(_soldierPositionsMap[soldier.Soldier.Id]);
            _grid.OccupyCells(newLocation, soldier.Soldier.Id);
            _soldierPositionsMap[soldier.Soldier.Id] = newLocation;
            TouchPositionRevisions(soldier);
            InvalidateLayoutQueries();
            return true;
        }

        private List<ValueTuple<int, int>> GetSoldierFootprint(BattleSoldier soldier, ValueTuple<int, int> topLeft, ushort orientation)
        {
            List<ValueTuple<int, int>> cells = [];
            int width;
            int depth;
            if (!BattleOrientation.IsFootprintRotated(orientation))
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
                    cells.Add(new ValueTuple<int, int>((short)(topLeft.Item1 + w), (short)(topLeft.Item2 - d)));
                }
            }
            return cells;
        }

        private bool CanMoveTo(BattleSoldier soldier, IEnumerable<ValueTuple<int, int>> cells)
        {
            foreach (ValueTuple<int, int> location in cells)
            {
                int? occupier = _grid.GetCellObject(location);
                if (occupier != null && occupier != soldier.Soldier.Id)
                {
                    return false;
                }
            }
            return true;
        }

        private void EnsureMoveAvailable(BattleSoldier soldier, IEnumerable<ValueTuple<int, int>> cells)
        {
            foreach (ValueTuple<int, int> location in cells)
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
                BattleSoldier soldier = _soldiers[soldierId];
                bool side = _soldierSideMap[soldierId];
                _grid.FreeCells(_soldierPositionsMap[soldierId]);
                _soldiers.Remove(soldierId);
                _soldierPositionsMap.Remove(soldierId);
                _soldierSideMap.Remove(soldierId);
                _soldierPositionRevisions.Remove(soldierId);
                _soldierPlacementOrder.Remove(soldierId);
                _enemyProximityCache.Remove(soldierId);
                TouchSidePositionRevision(side);
                TouchSquadPositionRevision(soldier.BattleSquad?.Id);
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
                .OrderBy(otherSoldierId => otherSoldierId)
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

            closestSoldierId = -1;
            float closestDistance = (float)Math.Sqrt(float.MaxValue);
            foreach ((int enemyId, float distance) in GetOrBuildEnemyDistances(soldierId))
            {
                if (distance < closestDistance)
                {
                    closestSoldierId = enemyId;
                    closestDistance = distance;
                }
            }
            return closestDistance;
        }

        /// <summary>
        /// Returns opposing soldiers ordered by distance, preserving grid insertion order for ties.
        /// Each soldier's row is rebuilt lazily when that soldier or anyone on the opposing side
        /// moves, avoiding battlefield-wide proximity maintenance after every movement.
        /// </summary>
        public IReadOnlyList<(int SoldierId, float Distance)> GetEnemiesByDistance(int soldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            return GetOrBuildEnemyDistances(soldierId)
                .OrderBy(enemy => enemy.Distance)
                .ThenBy(enemy => _soldierPlacementOrder[enemy.SoldierId])
                .ToArray();
        }

        /// <summary>
        /// Returns opposing soldiers and their distances without imposing an order. Hot-path
        /// callers should select only the minimum or small candidate set they actually need.
        /// </summary>
        internal IReadOnlyList<(int SoldierId, float Distance)> GetEnemyDistances(int soldierId)
        {
            if (!_soldiers.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            return GetOrBuildEnemyDistances(soldierId);
        }

        public float GetDistanceBetweenSoldiers(int soldierId1, int soldierId2)
        {
            return GetOrCalculateDistance(soldierId1, soldierId2);
        }

        private float GetOrCalculateDistance(int soldierId1, int soldierId2)
        {
            var cacheKey = soldierId1 <= soldierId2
                ? (soldierId1, soldierId2)
                : (soldierId2, soldierId1);
            int firstRevision = _soldierPositionRevisions[cacheKey.Item1];
            int secondRevision = _soldierPositionRevisions[cacheKey.Item2];
            if (_distanceCache.TryGetValue(cacheKey, out var cached)
                && cached.FirstRevision == firstRevision
                && cached.SecondRevision == secondRevision)
            {
                return cached.Distance;
            }

            IList<ValueTuple<int, int>> pos1 = _soldierPositionsMap[soldierId1];
            IList<ValueTuple<int, int>> pos2 = _soldierPositionsMap[soldierId2];
            float distanceSq = int.MaxValue;
            foreach (ValueTuple<int, int> tuple1 in pos1)
            {
                foreach (ValueTuple<int, int> tuple2 in pos2)
                {
                    float tempDistance = CalculateDistanceSq(tuple1, tuple2);
                    if (tempDistance < distanceSq)
                    {
                        distanceSq = tempDistance;
                    }
                }
            }
            float distance = (float)Math.Sqrt(distanceSq);
            _distanceCache[cacheKey] = (distance, firstRevision, secondRevision);
            return distance;
        }

        public float GetMinimumDistanceBetweenSquads(BattleSquad first, BattleSquad second)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            var cacheKey = first.Id <= second.Id
                ? (first.Id, second.Id)
                : (second.Id, first.Id);
            int firstRevision = GetSquadPositionRevision(cacheKey.Item1);
            int secondRevision = GetSquadPositionRevision(cacheKey.Item2);
            if (_squadDistanceCache.TryGetValue(cacheKey, out var cached)
                && cached.FirstRevision == firstRevision
                && cached.SecondRevision == secondRevision)
            {
                return cached.Distance;
            }

            float distance = float.MaxValue;
            foreach (BattleSoldier firstSoldier in first.AbleSoldiers)
            {
                if (!_soldierPositionsMap.ContainsKey(firstSoldier.Soldier.Id)) continue;
                foreach (BattleSoldier secondSoldier in second.AbleSoldiers)
                {
                    if (!_soldierPositionsMap.ContainsKey(secondSoldier.Soldier.Id)) continue;
                    float candidate = GetOrCalculateDistance(
                        firstSoldier.Soldier.Id,
                        secondSoldier.Soldier.Id);
                    if (candidate < distance)
                    {
                        distance = candidate;
                    }
                }
            }

            _squadDistanceCache[cacheKey] = (distance, firstRevision, secondRevision);
            return distance;
        }

        public float GetMinimumDistanceBetweenSquadAndSoldier(BattleSquad squad, int soldierId)
        {
            ArgumentNullException.ThrowIfNull(squad);
            if (!_soldierPositionsMap.ContainsKey(soldierId))
            {
                throw new ArgumentException("Soldier not found");
            }

            var cacheKey = (squad.Id, soldierId);
            int squadRevision = GetSquadPositionRevision(squad.Id);
            int soldierRevision = _soldierPositionRevisions[soldierId];
            if (_squadToSoldierDistanceCache.TryGetValue(cacheKey, out var cached)
                && cached.SquadRevision == squadRevision
                && cached.SoldierRevision == soldierRevision)
            {
                return cached.Distance;
            }

            float distance = float.MaxValue;
            foreach (BattleSoldier squadSoldier in squad.AbleSoldiers)
            {
                if (!_soldierPositionsMap.ContainsKey(squadSoldier.Soldier.Id)) continue;
                float candidate = GetOrCalculateDistance(squadSoldier.Soldier.Id, soldierId);
                if (candidate < distance)
                {
                    distance = candidate;
                }
            }

            _squadToSoldierDistanceCache[cacheKey] = (distance, squadRevision, soldierRevision);
            return distance;
        }

        public int? GetCellOccupant(int x, int y)
        {
            return _grid.GetCellObject(x, y);
        }

        public IList<ValueTuple<int, int>> GetSoldierPosition(int soldierId)
        {
            return _grid.GetObjectCells(soldierId);
        }

        public IReadOnlyDictionary<int, IList<ValueTuple<int, int>>> GetSoldierPositions()
        {
            return _soldierPositionsMap;
        }

        public ValueTuple<int, int> GetClosestOpenAdjacency(ValueTuple<int, int> startingPoint, ValueTuple<int, int> target)
        {
            ValueTuple<int, int> bestPosition = startingPoint;
            float bestDistance = float.MaxValue;
            float disSq;
            ValueTuple<int, int>[] testPositions = 
                {
                    new ValueTuple<int, int>(target.Item1, (short)(target.Item2 - 1)),
                    new ValueTuple<int, int>(target.Item1, (short)(target.Item2 + 1)),
                    new ValueTuple<int, int>((short)(target.Item1 - 1), target.Item2),
                    new ValueTuple<int, int>((short)(target.Item1 + 1), target.Item2)
                };
            foreach (ValueTuple<int, int> testPosition in testPositions)
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

        public void ReserveSpace(ValueTuple<int, int> location)
        {
            _grid.ReserveCell(location);
        }

        public void ClearReservations()
        {
            _grid.ClearReservedCells();
        }

        public bool IsSpaceAvailable(ValueTuple<int, int> location)
        {
            return !_grid.IsCellReserved(location) && _grid.GetCellObject(location) == null;
        }

        private float CalculateDistanceSq(ValueTuple<int, int> pos1, ValueTuple<int, int> pos2)
        {
            // for now, as a quick good-enough, just look at the difference in coordinates
            long xDistance = pos1.Item1 - pos2.Item1;
            long yDistance = pos1.Item2 - pos2.Item2;
            return (xDistance * xDistance) + (yDistance * yDistance);
        }

        private IReadOnlyList<(int SoldierId, float Distance)> GetOrBuildEnemyDistances(
            int soldierId)
        {
            int soldierRevision = _soldierPositionRevisions[soldierId];
            bool enemySide = !_soldierSideMap[soldierId];
            int enemySideRevision = _sidePositionRevisions.GetValueOrDefault(enemySide);
            if (_enemyProximityCache.TryGetValue(soldierId, out var cached)
                && cached.SoldierRevision == soldierRevision
                && cached.EnemySideRevision == enemySideRevision)
            {
                return cached.Enemies;
            }

            List<(int SoldierId, float Distance)> enemies = [];
            foreach (int otherSoldierId in _soldiers.Keys)
            {
                if (_soldierSideMap[otherSoldierId] == enemySide)
                {
                    enemies.Add((
                        otherSoldierId,
                        GetOrCalculateDistance(soldierId, otherSoldierId)));
                }
            }

            _enemyProximityCache[soldierId] = (
                enemies,
                soldierRevision,
                enemySideRevision);
            return enemies;
        }

        private void TouchPositionRevisions(BattleSoldier soldier)
        {
            _soldierPositionRevisions[soldier.Soldier.Id] = ++_nextPositionRevision;
            TouchSidePositionRevision(_soldierSideMap[soldier.Soldier.Id]);
            TouchSquadPositionRevision(soldier.BattleSquad?.Id);
        }

        private void TouchSidePositionRevision(bool side)
        {
            _sidePositionRevisions[side] = ++_nextPositionRevision;
        }

        private void TouchSquadPositionRevision(int? squadId)
        {
            if (squadId.HasValue)
            {
                _squadPositionRevisions[squadId.Value] = ++_nextPositionRevision;
            }
        }

        private int GetSquadPositionRevision(int squadId)
        {
            return _squadPositionRevisions.GetValueOrDefault(squadId);
        }

        private void InvalidateLayoutQueries()
        {
            _adjacentSoldierCache.Clear();
            _engagementCache.Clear();
            _meleeScrumCache.Clear();
        }
    }
}
