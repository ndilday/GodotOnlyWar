using System;

namespace OnlyWar.Abstractions
{
    /// <summary>
    /// Supplies IDs for a generated force without coupling it to persistent campaign counters.
    /// </summary>
    public interface IEntityIdAllocator
    {
        int GetNextId();
    }

}
