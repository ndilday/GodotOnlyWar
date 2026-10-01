using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OnlyWar.Battles
{
    public class BattleGridManager : ICloneable
    {
        private const int InitialSlotCapacity = 16;

        // Soldier IDs are translated once at the API boundary. All proximity work below uses
        // compact battle-local slots and a flat, symmetric distance matrix.
        private readonly Dictionary<int, int> _soldierSlots = [];
        private readonly Dictionary<int, List<int>> _squadSlots = [];
        private readonly ConcurrentDictionary<int, AbleSquadSlotCache> _ableSquadSlotCaches = [];
        private readonly Stack<int> _freeSlots = [];
        private readonly Grid _grid = new();
        private readonly ConcurrentDictionary<int, LayoutCacheEntry<IReadOnlyList<int>>>
            _adjacentSoldierCache = [];
        private readonly ConcurrentDictionary<(bool ShooterSide, int TargetId), LayoutCacheEntry<bool>>
            _engagementCache = [];
        private readonly ConcurrentDictionary<int, LayoutCacheEntry<IReadOnlyList<int>>>
            _meleeScrumCache = [];

        private BattleSoldier[] _slotSoldiers = new BattleSoldier[InitialSlotCapacity];
        private IList<ValueTuple<int, int>>[] _slotPositions =
            new IList<ValueTuple<int, int>>[InitialSlotCapacity];
        private int[] _slotSoldierIds = new int[InitialSlotCapacity];
        private int[] _slotSquadIds = new int[InitialSlotCapacity];
        private int[] _slotPlacementOrders = new int[InitialSlotCapacity];
        private bool[] _slotActive = new bool[InitialSlotCapacity];
        private bool[] _slotSides = new bool[InitialSlotCapacity];
        private bool[] _slotHasSquad = new bool[InitialSlotCapacity];
        private bool[] _slotRectangular = new bool[InitialSlotCapacity];
        private int[] _slotMinX = new int[InitialSlotCapacity];
        private int[] _slotMaxX = new int[InitialSlotCapacity];
        private int[] _slotMinY = new int[InitialSlotCapacity];
        private int[] _slotMaxY = new int[InitialSlotCapacity];
        private float[] _distances = new float[InitialSlotCapacity * InitialSlotCapacity];
        private int _slotCapacity = InitialSlotCapacity;
        private int _slotCount;
        private int _nextPlacementOrder;
        private long _layoutGeneration;

        internal long LayoutGeneration => Volatile.Read(ref _layoutGeneration);
        internal int CachedAdjacencyCount => _adjacentSoldierCache.Count;
        internal int CachedEngagementCount => _engagementCache.Count;
        internal int CachedMeleeScrumCount => _meleeScrumCache.Count;

        public object Clone()
        {
            var copy = new BattleGridManager();
            List<int> activeSlots = new(_soldierSlots.Count);
            for (int slot = 0; slot < _slotCount; slot++)
            {
                if (_slotActive[slot]) activeSlots.Add(slot);
            }
            activeSlots.Sort((first, second) =>
                _slotPlacementOrders[first].CompareTo(_slotPlacementOrders[second]));
            foreach (int slot in activeSlots)
            {
                copy.PlaceSoldier(_slotSoldiers[slot], _slotSides[slot], _slotPositions[slot]);
            }
            foreach (ValueTuple<int, int> cell in _grid.GetReservedCells())
            {
                copy.ReserveSpace(cell);
            }
            return copy;
        }

        public void PlaceSoldier(BattleSoldier soldier, bool side, IList<ValueTuple<int, int>> cells)
        {
            int soldierId = soldier.Soldier.Id;
            if (_soldierSlots.ContainsKey(soldierId))
            {
                throw new InvalidOperationException($"Soldier {soldierId} is already placed.");
            }
            foreach (ValueTuple<int, int> cell in cells)
            {
                if (_grid.GetCellObject(cell) != null || _grid.IsCellReserved(cell))
                {
                    throw new InvalidOperationException(
                        $"Cannot place soldier {soldierId} at {cell}. Cell is occupied or reserved.");
                }
            }

            int slot = AcquireSlot();
            _soldierSlots.Add(soldierId, slot);
            _slotSoldiers[slot] = soldier;
            _slotPositions[slot] = cells;
            _slotSoldierIds[slot] = soldierId;
            _slotHasSquad[slot] = soldier.BattleSquad != null;
            _slotSquadIds[slot] = soldier.BattleSquad?.Id ?? 0;
            _slotSides[slot] = side;
            _slotPlacementOrders[slot] = _nextPlacementOrder++;
            SetFootprint(slot, cells);
            _slotActive[slot] = true;
            if (_slotHasSquad[slot])
            {
                if (!_squadSlots.TryGetValue(_slotSquadIds[slot], out List<int> squadSlots))
                {
                    squadSlots = [];
                    _squadSlots.Add(_slotSquadIds[slot], squadSlots);
                }
                squadSlots.Add(slot);
                _ableSquadSlotCaches.TryRemove(_slotSquadIds[slot], out _);
            }
            _grid.OccupyCells(cells, soldierId);
            RecalculateDistanceRow(slot);
            InvalidateLayoutQueries();
        }

        public bool TryMoveSoldier(BattleSoldier soldier, ValueTuple<int, int> newTopLeft,
            ushort newOrientation)
        {
            List<ValueTuple<int, int>> newLocation =
                GetSoldierFootprint(soldier, newTopLeft, newOrientation);
            if (!CanMoveTo(soldier, newLocation)) return false;
            MoveSoldierToCells(soldier.Soldier.Id, newLocation);
            return true;
        }

        public bool IsMoveDestinationAvailable(
            BattleSoldier soldier,
            ValueTuple<int, int> newTopLeft,
            ushort newOrientation)
        {
            foreach (ValueTuple<int, int> location in
                GetSoldierFootprint(soldier, newTopLeft, newOrientation))
            {
                int? occupier = _grid.GetCellObject(location);
                if (_grid.IsCellReserved(location)
                    || (occupier != null && occupier != soldier.Soldier.Id))
                {
                    return false;
                }
            }
            return true;
        }

        public void ReserveMoveDestination(
            BattleSoldier soldier,
            ValueTuple<int, int> newTopLeft,
            ushort newOrientation)
        {
            foreach (ValueTuple<int, int> location in
                GetSoldierFootprint(soldier, newTopLeft, newOrientation))
            {
                _grid.ReserveCell(location);
            }
        }

        private void MoveSoldierToCells(int soldierId, IList<ValueTuple<int, int>> newLocation)
        {
            int slot = GetSlot(soldierId);
            _grid.FreeCells(_slotPositions[slot]);
            _grid.OccupyCells(newLocation, soldierId);
            _slotPositions[slot] = newLocation;
            SetFootprint(slot, newLocation);
            RecalculateDistanceRow(slot);
            InvalidateLayoutQueries();
        }

        private static List<ValueTuple<int, int>> GetSoldierFootprint(BattleSoldier soldier,
            ValueTuple<int, int> topLeft, ushort orientation) =>
            BattleOrientation.GetFootprint(soldier, topLeft, orientation);

        private bool CanMoveTo(BattleSoldier soldier, IEnumerable<ValueTuple<int, int>> cells)
        {
            foreach (ValueTuple<int, int> location in cells)
            {
                int? occupier = _grid.GetCellObject(location);
                if (occupier != null && occupier != soldier.Soldier.Id) return false;
            }
            return true;
        }

        public void RemoveSoldier(int soldierId)
        {
            if (!_soldierSlots.Remove(soldierId, out int slot)) return;
            _grid.FreeCells(_slotPositions[slot]);
            int squadId = _slotSquadIds[slot];
            if (_slotHasSquad[slot]
                && _squadSlots.TryGetValue(squadId, out List<int> squadSlots))
            {
                squadSlots.Remove(slot);
                if (squadSlots.Count == 0) _squadSlots.Remove(squadId);
                _ableSquadSlotCaches.TryRemove(squadId, out _);
            }
            _slotActive[slot] = false;
            _slotSoldiers[slot] = null;
            _slotPositions[slot] = null;
            _slotHasSquad[slot] = false;
            _slotSquadIds[slot] = 0;
            _freeSlots.Push(slot);
            InvalidateLayoutQueries();
        }

        public bool IsAdjacentToEnemy(int soldierId)
        {
            int slot = GetSlot(soldierId);
            bool side = _slotSides[slot];
            foreach (int adjacentId in GetAdjacentSoldiers(soldierId))
            {
                if (_slotSides[GetSlot(adjacentId)] != side) return true;
            }
            return false;
        }

        public IReadOnlyList<int> GetAdjacentEnemies(int soldierId)
        {
            int slot = GetSlot(soldierId);
            bool side = _slotSides[slot];
            List<int> enemies = [];
            foreach (int adjacentId in GetAdjacentSoldiers(soldierId))
            {
                if (_slotSides[GetSlot(adjacentId)] != side) enemies.Add(adjacentId);
            }
            return enemies;
        }

        public IReadOnlyList<int> GetAdjacentSoldiers(int soldierId)
        {
            GetSlot(soldierId);
            while (true)
            {
                long generation = Volatile.Read(ref _layoutGeneration);
                if (_adjacentSoldierCache.TryGetValue(
                    soldierId,
                    out LayoutCacheEntry<IReadOnlyList<int>> cached)
                    && cached.Generation == generation)
                {
                    return cached.Value;
                }

                IReadOnlyList<int> adjacent = _grid.GetAdjacentObjects(soldierId)
                    .Where(_soldierSlots.ContainsKey)
                    .OrderBy(adjacentId => adjacentId)
                    .ToArray();
                if (Volatile.Read(ref _layoutGeneration) != generation) continue;

                LayoutCacheEntry<IReadOnlyList<int>> replacement = new(generation, adjacent);
                LayoutCacheEntry<IReadOnlyList<int>> published = _adjacentSoldierCache.AddOrUpdate(
                    soldierId,
                    replacement,
                    (_, existing) => existing.Generation >= generation ? existing : replacement);
                if (published.Generation == generation) return published.Value;
            }
        }

        public bool IsTargetEngagedWithShootersAllies(int shooterId, int targetId)
        {
            bool shooterSide = _slotSides[GetSlot(shooterId)];
            GetSlot(targetId);
            var cacheKey = (shooterSide, targetId);
            while (true)
            {
                long generation = Volatile.Read(ref _layoutGeneration);
                if (_engagementCache.TryGetValue(
                    cacheKey,
                    out LayoutCacheEntry<bool> cached)
                    && cached.Generation == generation)
                {
                    return cached.Value;
                }

                bool engaged = false;
                foreach (int adjacentId in GetAdjacentSoldiers(targetId))
                {
                    if (_slotSides[GetSlot(adjacentId)] == shooterSide)
                    {
                        engaged = true;
                        break;
                    }
                }
                if (Volatile.Read(ref _layoutGeneration) != generation) continue;

                LayoutCacheEntry<bool> replacement = new(generation, engaged);
                LayoutCacheEntry<bool> published = _engagementCache.AddOrUpdate(
                    cacheKey,
                    replacement,
                    (_, existing) => existing.Generation >= generation ? existing : replacement);
                if (published.Generation == generation) return published.Value;
            }
        }

        public IReadOnlyList<int> GetMeleeScrumParticipants(int soldierId)
        {
            GetSlot(soldierId);
            while (true)
            {
                long generation = Volatile.Read(ref _layoutGeneration);
                if (_meleeScrumCache.TryGetValue(
                    soldierId,
                    out LayoutCacheEntry<IReadOnlyList<int>> cached)
                    && cached.Generation == generation)
                {
                    return cached.Value;
                }

                HashSet<int> participants = [soldierId];
                Queue<int> frontier = new();
                frontier.Enqueue(soldierId);
                while (frontier.Count > 0)
                {
                    int currentId = frontier.Dequeue();
                    bool currentSide = _slotSides[GetSlot(currentId)];
                    foreach (int adjacentId in GetAdjacentSoldiers(currentId))
                    {
                        if (_slotSides[GetSlot(adjacentId)] == currentSide
                            || !participants.Add(adjacentId)) continue;
                        frontier.Enqueue(adjacentId);
                    }
                }
                if (Volatile.Read(ref _layoutGeneration) != generation) continue;

                IReadOnlyList<int> result = participants.OrderBy(id => id).ToArray();
                LayoutCacheEntry<IReadOnlyList<int>> replacement = new(generation, result);
                foreach (int participantId in result)
                {
                    _meleeScrumCache.AddOrUpdate(
                        participantId,
                        replacement,
                        (_, existing) => existing.Generation >= generation
                            ? existing
                            : replacement);
                }
                if (Volatile.Read(ref _layoutGeneration) == generation) return result;
            }
        }

        public bool GetSoldierSide(int soldierId) => _slotSides[GetSlot(soldierId)];

        public bool IsSoldierPlaced(int soldierId) => _soldierSlots.ContainsKey(soldierId);

        public float GetNearestEnemy(int soldierId, out int closestSoldierId)
        {
            int sourceSlot = GetSlot(soldierId);
            bool side = _slotSides[sourceSlot];
            closestSoldierId = -1;
            float closestDistance = (float)Math.Sqrt(float.MaxValue);
            int closestPlacementOrder = int.MaxValue;
            int rowOffset = sourceSlot * _slotCapacity;
            for (int slot = 0; slot < _slotCount; slot++)
            {
                if (!_slotActive[slot] || _slotSides[slot] == side) continue;
                float distance = _distances[rowOffset + slot];
                int order = _slotPlacementOrders[slot];
                if (distance < closestDistance
                    || (distance == closestDistance && order < closestPlacementOrder))
                {
                    closestDistance = distance;
                    closestSoldierId = _slotSoldierIds[slot];
                    closestPlacementOrder = order;
                }
            }
            return closestDistance;
        }

        internal EnemyDistanceEnumerable GetEnemyDistances(int soldierId) =>
            new(this, GetSlot(soldierId));

        public float GetDistanceBetweenSoldiers(int soldierId1, int soldierId2)
        {
            int firstSlot = GetSlot(soldierId1);
            int secondSlot = GetSlot(soldierId2);
            return _distances[(firstSlot * _slotCapacity) + secondSlot];
        }

        public float GetMinimumDistanceBetweenSquads(BattleSquad first, BattleSquad second)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            IReadOnlyList<int> firstSlots = GetAbleSquadSlots(first);
            IReadOnlyList<int> secondSlots = GetAbleSquadSlots(second);
            if (firstSlots.Count == 0 || secondSlots.Count == 0)
            {
                return float.MaxValue;
            }
            float distance = float.MaxValue;
            int capacity = _slotCapacity;
            float[] distances = _distances;
            for (int firstIndex = 0; firstIndex < firstSlots.Count; firstIndex++)
            {
                int firstSlot = firstSlots[firstIndex];
                int rowOffset = firstSlot * capacity;
                for (int secondIndex = 0; secondIndex < secondSlots.Count; secondIndex++)
                {
                    int secondSlot = secondSlots[secondIndex];
                    float candidate = distances[rowOffset + secondSlot];
                    if (candidate < distance) distance = candidate;
                }
            }
            return distance;
        }

        public float GetMinimumDistanceBetweenSquadAndSoldier(BattleSquad squad, int soldierId)
        {
            ArgumentNullException.ThrowIfNull(squad);
            int targetSlot = GetSlot(soldierId);
            IReadOnlyList<int> memberSlots = GetAbleSquadSlots(squad);
            if (memberSlots.Count == 0)
            {
                return float.MaxValue;
            }
            float distance = float.MaxValue;
            int capacity = _slotCapacity;
            float[] distances = _distances;
            for (int index = 0; index < memberSlots.Count; index++)
            {
                int memberSlot = memberSlots[index];
                float candidate = distances[(memberSlot * capacity) + targetSlot];
                if (candidate < distance) distance = candidate;
            }
            return distance;
        }

        private IReadOnlyList<int> GetAbleSquadSlots(BattleSquad squad)
        {
            int generation = squad.AbleSoldiersGeneration;
            int sourceCount = squad.Soldiers.Count;
            if (_ableSquadSlotCaches.TryGetValue(squad.Id, out AbleSquadSlotCache cache)
                && cache.Generation == generation
                && cache.SourceCount == sourceCount)
            {
                return cache.Slots;
            }

            List<BattleSoldier> ableSoldiers = squad.AbleSoldiers;
            List<int> slots = new(ableSoldiers.Count);
            for (int index = 0; index < ableSoldiers.Count; index++)
            {
                int soldierId = ableSoldiers[index].Soldier.Id;
                if (_soldierSlots.TryGetValue(soldierId, out int slot))
                {
                    slots.Add(slot);
                }
            }
            AbleSquadSlotCache replacement = new(
                squad.AbleSoldiersGeneration,
                sourceCount,
                slots.ToArray());
            _ableSquadSlotCaches[squad.Id] = replacement;
            return replacement.Slots;
        }

        public int? GetCellOccupant(int x, int y) => _grid.GetCellObject(x, y);

        public IList<ValueTuple<int, int>> GetSoldierPosition(int soldierId)
        {
            return _slotPositions[GetSlot(soldierId)];
        }

        public IReadOnlyDictionary<int, IList<ValueTuple<int, int>>> GetSoldierPositions()
        {
            Dictionary<int, IList<ValueTuple<int, int>>> positions = new(_soldierSlots.Count);
            for (int slot = 0; slot < _slotCount; slot++)
            {
                if (_slotActive[slot]) positions.Add(_slotSoldierIds[slot], _slotPositions[slot]);
            }
            return positions;
        }

        /// <summary>
        /// The nearest top-left the charger can move to that puts its whole footprint in open,
        /// unreserved cells and in melee contact with the target, or null when there is none.
        ///
        /// <para>Both footprints count. Every cell orthogonally beside ANY target cell is a contact
        /// square (diagonals are not; see <see cref="Grid.GetAdjacentObjects"/>), so a 4x2 target
        /// offers twelve of them, not the four around its first cell. And a charger larger than
        /// 1x1 reaches a contact square with any of its own cells, so each contact square yields
        /// one candidate top-left per cell of the charger's footprint.</para>
        ///
        /// <para>A rectangular charger's footprint turns with its facing, and its facing on arrival
        /// depends on the direction of the move, so the caller supplies the orientation it would
        /// arrive with at each candidate. A candidate only counts in the footprint shape that
        /// orientation actually gives it.</para>
        /// </summary>
        public ValueTuple<int, int>? GetClosestOpenAdjacency(
            BattleSoldier charger,
            int targetId,
            Func<ValueTuple<int, int>, ushort> arrivalOrientation)
        {
            ValueTuple<int, int> start = charger.TopLeft.Value;
            IList<ValueTuple<int, int>> targetCells = GetSoldierPosition(targetId);
            HashSet<ValueTuple<int, int>> targetSet = [.. targetCells];
            List<ValueTuple<int, int>> contactSquares = [];
            HashSet<ValueTuple<int, int>> seenContactSquares = [];
            foreach (ValueTuple<int, int> cell in targetCells)
            {
                ValueTuple<int, int>[] neighbors =
                [
                    new(cell.Item1, cell.Item2 - 1),
                    new(cell.Item1, cell.Item2 + 1),
                    new(cell.Item1 - 1, cell.Item2),
                    new(cell.Item1 + 1, cell.Item2)
                ];
                foreach (ValueTuple<int, int> neighbor in neighbors)
                {
                    if (!targetSet.Contains(neighbor) && seenContactSquares.Add(neighbor))
                    {
                        contactSquares.Add(neighbor);
                    }
                }
            }

            int width = charger.Soldier.Template.Species.Width;
            int depth = charger.Soldier.Template.Species.Depth;
            bool isSquare = width == depth;
            (int Width, int Depth, bool Rotated)[] shapes = isSquare
                ? [(width, depth, false)]
                : [(width, depth, false), (depth, width, true)];

            ValueTuple<int, int>? best = null;
            float bestDistance = float.MaxValue;
            HashSet<ValueTuple<int, int>> triedTopLefts = [];
            foreach (ValueTuple<int, int> square in contactSquares)
            {
                foreach ((int shapeWidth, int shapeDepth, bool rotated) in shapes)
                {
                    // Footprint cells run +x and -y from the top-left (GetSoldierFootprint), so
                    // the top-left that puts footprint cell (i, j) on this square is (x-i, y+j).
                    for (int i = 0; i < shapeWidth; i++)
                    {
                        for (int j = 0; j < shapeDepth; j++)
                        {
                            ValueTuple<int, int> topLeft = new(square.Item1 - i, square.Item2 + j);
                            if (topLeft == start) continue;
                            float distance = CalculateDistanceSq(start, topLeft);
                            if (distance >= bestDistance) continue;
                            ushort orientation = arrivalOrientation(topLeft);
                            if (!isSquare
                                && BattleOrientation.IsFootprintRotated(orientation) != rotated)
                            {
                                continue;
                            }
                            if (!triedTopLefts.Add(topLeft)) continue;
                            if (!IsMoveDestinationAvailable(charger, topLeft, orientation)) continue;
                            bestDistance = distance;
                            best = topLeft;
                        }
                    }
                }
            }
            return best;
        }

        public void ReserveSpace(ValueTuple<int, int> location) => _grid.ReserveCell(location);
        public void ClearReservations() => _grid.ClearReservedCells();
        public bool IsSpaceAvailable(ValueTuple<int, int> location) =>
            !_grid.IsCellReserved(location) && _grid.GetCellObject(location) == null;

        private int AcquireSlot()
        {
            if (_freeSlots.Count > 0) return _freeSlots.Pop();
            if (_slotCount == _slotCapacity) GrowSlots();
            return _slotCount++;
        }

        private int GetSlot(int soldierId)
        {
            if (!_soldierSlots.TryGetValue(soldierId, out int slot))
            {
                throw new ArgumentException("Soldier not found");
            }
            return slot;
        }

        private void GrowSlots()
        {
            int oldCapacity = _slotCapacity;
            int newCapacity = oldCapacity * 2;
            Array.Resize(ref _slotSoldiers, newCapacity);
            Array.Resize(ref _slotPositions, newCapacity);
            Array.Resize(ref _slotSoldierIds, newCapacity);
            Array.Resize(ref _slotSquadIds, newCapacity);
            Array.Resize(ref _slotPlacementOrders, newCapacity);
            Array.Resize(ref _slotActive, newCapacity);
            Array.Resize(ref _slotSides, newCapacity);
            Array.Resize(ref _slotHasSquad, newCapacity);
            Array.Resize(ref _slotRectangular, newCapacity);
            Array.Resize(ref _slotMinX, newCapacity);
            Array.Resize(ref _slotMaxX, newCapacity);
            Array.Resize(ref _slotMinY, newCapacity);
            Array.Resize(ref _slotMaxY, newCapacity);
            float[] grownDistances = new float[newCapacity * newCapacity];
            for (int row = 0; row < _slotCount; row++)
            {
                Array.Copy(_distances, row * oldCapacity,
                    grownDistances, row * newCapacity, _slotCount);
            }
            _distances = grownDistances;
            _slotCapacity = newCapacity;
        }

        private void SetFootprint(int slot, IList<ValueTuple<int, int>> cells)
        {
            if (cells.Count == 0)
            {
                _slotRectangular[slot] = false;
                return;
            }
            int minX = cells[0].Item1;
            int maxX = minX;
            int minY = cells[0].Item2;
            int maxY = minY;
            for (int i = 1; i < cells.Count; i++)
            {
                ValueTuple<int, int> cell = cells[i];
                if (cell.Item1 < minX) minX = cell.Item1;
                if (cell.Item1 > maxX) maxX = cell.Item1;
                if (cell.Item2 < minY) minY = cell.Item2;
                if (cell.Item2 > maxY) maxY = cell.Item2;
            }
            _slotMinX[slot] = minX;
            _slotMaxX[slot] = maxX;
            _slotMinY[slot] = minY;
            _slotMaxY[slot] = maxY;
            long expectedCount = ((long)maxX - minX + 1) * ((long)maxY - minY + 1);
            if (expectedCount != cells.Count)
            {
                _slotRectangular[slot] = false;
                return;
            }
            HashSet<ValueTuple<int, int>> uniqueCells = new(cells);
            _slotRectangular[slot] = uniqueCells.Count == cells.Count;
        }

        private void RecalculateDistanceRow(int movedSlot)
        {
            int capacity = _slotCapacity;
            int movedRow = movedSlot * capacity;
            bool[] active = _slotActive;
            bool[] rectangular = _slotRectangular;
            float[] distances = _distances;
            bool movedIsRectangle = rectangular[movedSlot];
            int movedMinX = _slotMinX[movedSlot];
            int movedMaxX = _slotMaxX[movedSlot];
            int movedMinY = _slotMinY[movedSlot];
            int movedMaxY = _slotMaxY[movedSlot];
            for (int otherSlot = 0; otherSlot < _slotCount; otherSlot++)
            {
                if (!active[otherSlot]) continue;
                float distance;
                if (movedSlot == otherSlot)
                {
                    distance = 0;
                }
                else if (movedIsRectangle && rectangular[otherSlot])
                {
                    long xDistance = 0;
                    int otherMinX = _slotMinX[otherSlot];
                    int otherMaxX = _slotMaxX[otherSlot];
                    if (movedMaxX < otherMinX)
                    {
                        xDistance = (long)otherMinX - movedMaxX;
                    }
                    else if (otherMaxX < movedMinX)
                    {
                        xDistance = (long)movedMinX - otherMaxX;
                    }

                    long yDistance = 0;
                    int otherMinY = _slotMinY[otherSlot];
                    int otherMaxY = _slotMaxY[otherSlot];
                    if (movedMaxY < otherMinY)
                    {
                        yDistance = (long)otherMinY - movedMaxY;
                    }
                    else if (otherMaxY < movedMinY)
                    {
                        yDistance = (long)movedMinY - otherMaxY;
                    }
                    distance = (float)Math.Sqrt(
                        (xDistance * xDistance) + (yDistance * yDistance));
                }
                else
                {
                    distance = CalculateIrregularSlotDistance(movedSlot, otherSlot);
                }
                distances[movedRow + otherSlot] = distance;
                distances[(otherSlot * capacity) + movedSlot] = distance;
            }
        }

        private float CalculateIrregularSlotDistance(int firstSlot, int secondSlot)
        {
            IList<ValueTuple<int, int>> first = _slotPositions[firstSlot];
            IList<ValueTuple<int, int>> second = _slotPositions[secondSlot];
            float minimumSquared = int.MaxValue;
            for (int i = 0; i < first.Count; i++)
            {
                for (int j = 0; j < second.Count; j++)
                {
                    float candidate = CalculateDistanceSq(first[i], second[j]);
                    if (candidate < minimumSquared) minimumSquared = candidate;
                }
            }
            return (float)Math.Sqrt(minimumSquared);
        }

        private static float CalculateDistanceSq(ValueTuple<int, int> first,
            ValueTuple<int, int> second)
        {
            long xDistance = first.Item1 - second.Item1;
            long yDistance = first.Item2 - second.Item2;
            return (xDistance * xDistance) + (yDistance * yDistance);
        }

        private void InvalidateLayoutQueries()
        {
            Interlocked.Increment(ref _layoutGeneration);
        }

        private sealed record LayoutCacheEntry<T>(long Generation, T Value);

        private sealed record AbleSquadSlotCache(
            int Generation,
            int SourceCount,
            IReadOnlyList<int> Slots);

        internal readonly struct EnemyDistanceEnumerable
        {
            private readonly BattleGridManager _manager;
            private readonly int _sourceSlot;

            internal EnemyDistanceEnumerable(BattleGridManager manager, int sourceSlot)
            {
                _manager = manager;
                _sourceSlot = sourceSlot;
            }

            public Enumerator GetEnumerator() => new(_manager, _sourceSlot);

            internal struct Enumerator
            {
                private readonly BattleGridManager _manager;
                private readonly int _sourceSlot;
                private int _slot;

                internal Enumerator(BattleGridManager manager, int sourceSlot)
                {
                    _manager = manager;
                    _sourceSlot = sourceSlot;
                    _slot = -1;
                    Current = default;
                }

                public (int SoldierId, float Distance) Current { get; private set; }

                public bool MoveNext()
                {
                    bool sourceSide = _manager._slotSides[_sourceSlot];
                    while (++_slot < _manager._slotCount)
                    {
                        if (!_manager._slotActive[_slot]
                            || _manager._slotSides[_slot] == sourceSide) continue;
                        Current = (
                            _manager._slotSoldierIds[_slot],
                            _manager._distances[(_sourceSlot * _manager._slotCapacity) + _slot]);
                        return true;
                    }
                    return false;
                }
            }
        }
    }
}
