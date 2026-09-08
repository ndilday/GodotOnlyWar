using System;

namespace OnlyWar.Runtime.Allocators;

/// <summary>
/// Session-owned persistent ID state. A campaign host creates one instance and keeps it for the
/// lifetime of that session; no process-wide counters or reset hooks are involved.
/// </summary>
public sealed class PersistentIdAllocator : IPersistentIdAllocator
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

    public PersistentIdAllocator(
        int nextSoldierId = 0,
        int nextRequestId = 0,
        int nextMissionId = 0,
        int nextOrderId = 0,
        int nextCharacterId = 0,
        int nextPlanetId = 0,
        int nextUnitId = 0,
        int nextSquadId = 0,
        int nextTaskForceId = 0)
    {
        _nextSoldierId = nextSoldierId;
        _nextCharacterId = nextCharacterId;
        _nextPlanetId = nextPlanetId;
        _nextUnitId = nextUnitId;
        _nextSquadId = nextSquadId;
        _nextTaskForceId = nextTaskForceId;
        _nextRequestId = nextRequestId;
        _nextMissionId = nextMissionId;
        _nextOrderId = nextOrderId;
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
            throw new InvalidOperationException("Persistent ID range is exhausted.");
        }

        return next++;
    }
}
