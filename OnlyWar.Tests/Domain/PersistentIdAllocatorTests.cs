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
}
