using OnlyWar.Builders;

namespace OnlyWar.Builders;

/// <summary>Legacy namespace façade over Runtime's tactical allocator.</summary>
public sealed class TacticalEntityIdAllocator : IEntityIdAllocator
{
    private readonly Runtime.Allocators.TacticalEntityIdAllocator _inner = new();
    public int GetNextId() => _inner.GetNextId();
}

