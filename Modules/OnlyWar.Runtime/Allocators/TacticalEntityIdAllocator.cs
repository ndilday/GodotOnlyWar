using System;
using OnlyWar.Abstractions;

namespace OnlyWar.Runtime.Allocators;

/// <summary>Runtime-owned negative ID allocator for temporary tactical entities.</summary>
public sealed class TacticalEntityIdAllocator : IEntityIdAllocator
{
    private long _nextId = int.MinValue;

    public int GetNextId()
    {
        if (_nextId >= -1)
        {
            throw new InvalidOperationException("The tactical entity ID range is exhausted.");
        }

        return (int)_nextId++;
    }
}
