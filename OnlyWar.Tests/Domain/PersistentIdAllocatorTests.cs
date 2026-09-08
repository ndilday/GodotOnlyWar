using OnlyWar.Runtime.Allocators;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class PersistentIdAllocatorTests
{
    [Fact]
    public void EachCampaignAllocatorOwnsAnIndependentPersistentSequence()
    {
        PersistentIdAllocator first = new(10, 20, 30, 40);
        PersistentIdAllocator second = new(10, 20, 30, 40);

        Assert.Equal(10, first.GetNextSoldierId());
        Assert.Equal(10, second.GetNextSoldierId());
        Assert.Equal(20, first.GetNextRequestId());
        Assert.Equal(20, second.GetNextRequestId());
        Assert.Equal(30, first.GetNextMissionId());
        Assert.Equal(30, second.GetNextMissionId());
        Assert.Equal(40, first.GetNextOrderId());
        Assert.Equal(40, second.GetNextOrderId());
    }

    [Fact]
    public void TypedEntitySequencesAreIndependentAndDeterministic()
    {
        PersistentIdAllocator first = new(
            nextCharacterId: 5,
            nextPlanetId: 10,
            nextUnitId: 20,
            nextSquadId: 30,
            nextTaskForceId: 40);
        PersistentIdAllocator second = new(
            nextCharacterId: 5,
            nextPlanetId: 10,
            nextUnitId: 20,
            nextSquadId: 30,
            nextTaskForceId: 40);

        Assert.Equal(5, first.GetNextCharacterId());
        Assert.Equal(5, second.GetNextCharacterId());
        Assert.Equal(10, first.GetNextPlanetId());
        Assert.Equal(10, second.GetNextPlanetId());
        Assert.Equal(20, first.GetNextUnitId());
        Assert.Equal(20, second.GetNextUnitId());
        Assert.Equal(30, first.GetNextSquadId());
        Assert.Equal(30, second.GetNextSquadId());
        Assert.Equal(40, first.GetNextTaskForceId());
        Assert.Equal(40, second.GetNextTaskForceId());
    }
}
