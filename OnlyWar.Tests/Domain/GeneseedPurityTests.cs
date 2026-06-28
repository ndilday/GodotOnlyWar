using OnlyWar.Helpers;
using OnlyWar.Models;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class GeneseedPurityTests
{
    [Fact]
    public void AddRecoveredGeneseed_FoldsPurityAsCountWeightedAverage()
    {
        PlayerForce force = new(null, null, null);
        Assert.Equal(0, force.GeneseedStockpile);

        // First recovery onto an empty vault sets the aggregate to that gland's purity.
        force.AddRecoveredGeneseed(0.90f);
        Assert.Equal(1, force.GeneseedStockpile);
        Assert.Equal(0.90f, force.GeneseedPurity, 4);

        // Second recovery averages by count: (0.90 * 1 + 0.70) / 2 = 0.80.
        force.AddRecoveredGeneseed(0.70f);
        Assert.Equal(2, force.GeneseedStockpile);
        Assert.Equal(0.80f, force.GeneseedPurity, 4);

        // Third: (0.80 * 2 + 0.60) / 3 = 0.7333...
        force.AddRecoveredGeneseed(0.60f);
        Assert.Equal(3, force.GeneseedStockpile);
        Assert.Equal(0.7333f, force.GeneseedPurity, 3);
    }

    [Fact]
    public void RollRecoveredPurity_StaysWithinBaselineDriftAndBounds()
    {
        for (int i = 0; i < 1000; i++)
        {
            float purity = GeneseedRules.RollRecoveredPurity();
            Assert.InRange(purity, GeneseedRules.MinPurity, GeneseedRules.MaxPurity);
            Assert.InRange(purity,
                GeneseedRules.FoundingPurity - GeneseedRules.RecoveredPurityDrift,
                GeneseedRules.FoundingPurity);
        }
    }
}
