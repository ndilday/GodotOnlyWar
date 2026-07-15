using System;

namespace OnlyWar.Builders
{
    /// <summary>
    /// Supplies IDs for a generated force without coupling it to persistent campaign counters.
    /// </summary>
    public interface IEntityIdAllocator
    {
        int GetNextId();
    }

    /// <summary>
    /// Allocates mission-local entity IDs from the negative range. The value -1 remains reserved
    /// for existing battle code that uses it as a "not found" sentinel.
    /// </summary>
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
}
