using System;
using OnlyWar.Builders;

namespace OnlyWar.Runtime.Allocators;

/// <summary>
/// Explicit allocator for a construction run.  The caller chooses the starting point, so runtime
/// construction cannot silently share or reset a campaign-wide counter.
/// </summary>
public sealed class SequentialEntityIdAllocator : IEntityIdAllocator
{
    private int _next;

    public SequentialEntityIdAllocator(int firstId = 0)
    {
        _next = firstId;
    }

    public int GetNextId()
    {
        if (_next == int.MaxValue) throw new InvalidOperationException("Entity ID range is exhausted.");
        return _next++;
    }
}

