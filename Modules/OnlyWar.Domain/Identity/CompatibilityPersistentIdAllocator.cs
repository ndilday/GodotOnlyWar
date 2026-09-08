using System;
using OnlyWar.Abstractions;

namespace OnlyWar.Models;

/// <summary>
/// Source-compatibility allocator for the old convenience constructors. It is deliberately
/// created per compatibility call and never shared by production composition roots; new campaign
/// code must receive the session-owned <see cref="IPersistentIdAllocator"/> instead.
/// </summary>
internal sealed class CompatibilityPersistentIdAllocator : IPersistentIdAllocator
{
    private int _nextSoldierId;
    private int _nextCharacterId;
    private int _nextPlanetId;
    private int _nextUnitId;
    private int _nextSquadId;
    private int _nextTaskForceId;
    private int _nextRequestId;
    private int _nextMissionId;
    private int _nextOrderId;

    public CompatibilityPersistentIdAllocator(
        int nextSquadId = 0,
        int nextTaskForceId = 0)
    {
        _nextSquadId = nextSquadId;
        _nextTaskForceId = nextTaskForceId;
    }

    public int GetNextId() => GetNextSoldierId();
    public int GetNextSoldierId() => TakeNext(ref _nextSoldierId);
    public int GetNextCharacterId() => TakeNext(ref _nextCharacterId);
    public int GetNextPlanetId() => TakeNext(ref _nextPlanetId);
    public int GetNextUnitId() => TakeNext(ref _nextUnitId);
    public int GetNextSquadId() => TakeNext(ref _nextSquadId);
    public int GetNextTaskForceId() => TakeNext(ref _nextTaskForceId);
    public int GetNextRequestId() => TakeNext(ref _nextRequestId);
    public int GetNextMissionId() => TakeNext(ref _nextMissionId);
    public int GetNextOrderId() => TakeNext(ref _nextOrderId);

    private static int TakeNext(ref int next)
    {
        if (next == int.MaxValue)
        {
            throw new InvalidOperationException("Compatibility ID range is exhausted.");
        }

        return next++;
    }
}
