namespace OnlyWar.Abstractions
{
    /// <summary>
    /// Supplies the persistent IDs owned by one campaign session.
    /// </summary>
    public interface IPersistentIdAllocator : IEntityIdAllocator
    {
        int GetNextSoldierId();
        int GetNextRequestId();
        int GetNextMissionId();
        int GetNextOrderId();
    }
}
