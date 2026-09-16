using OnlyWar.Domain.Intelligence;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class FactionIntelligenceRulesTests
{
    [Theory]
    [InlineData(0f, 1_000L)]   // no significant figures: somewhere under a thousand
    [InlineData(1f, 900L)]
    [InlineData(2f, 870L)]
    [InlineData(3f, 869L)]
    [InlineData(6f, 869L)]
    public void CoarsenEstimate_GainsASignificantFigurePerPointOfAwareness(
        float awareness,
        long expected)
    {
        Assert.Equal(expected, FactionIntelligenceRules.CoarsenEstimate(869, awareness));
    }

    [Theory]
    [InlineData(101L)]
    [InlineData(320L)]
    [InlineData(869L)]
    [InlineData(999L)]
    public void CoarsenEstimate_NeverReadsBelowTheTruth(long trueValue)
    {
        // The upper bound is the whole point: an observer may badly overstate an enemy it has not
        // scouted, but it must never understate one, because that is the error that gets a force
        // killed. This is what lets the planner drop its separate caution multiplier.
        for (float awareness = 0f; awareness <= 4f; awareness += 0.5f)
        {
            Assert.True(
                FactionIntelligenceRules.CoarsenEstimate(trueValue, awareness) >= trueValue,
                $"{trueValue} understated at awareness {awareness}");
        }
    }

    [Fact]
    public void CoarsenEstimate_LosesPrecisionAsAwarenessDecays()
    {
        // Staleness is not a separate term. Region awareness decays every turn, so a belief nobody
        // refreshes is re-coarsened looser and looser on its own.
        Assert.Equal(870L, FactionIntelligenceRules.CoarsenEstimate(869, 2f));
        Assert.Equal(900L, FactionIntelligenceRules.CoarsenEstimate(870, 1.5f));
        Assert.Equal(1_000L, FactionIntelligenceRules.CoarsenEstimate(900, 0.9f));
    }

    [Fact]
    public void CoarsenEstimate_DistinguishesNothingFromNotMeasured()
    {
        Assert.Equal(0L, FactionIntelligenceRules.CoarsenEstimate(0, 0f));
        Assert.Null(FactionIntelligenceRules.CoarsenEstimate(-1, 0f));
    }
}
