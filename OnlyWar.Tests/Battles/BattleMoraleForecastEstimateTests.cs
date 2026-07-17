using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The RNG-free closed-form morale estimators (Design/Active/MoraleAndRout.md §8.2) that let the
/// withdrawal forecast price command collapse deterministically: per-soldier fail probability
/// Phi((stress - resolve)/sigma), meaned to a fail fraction and aggregated through the same
/// thresholds the live roll uses. No battle RNG is consumed, so no shared-state collection is
/// needed.
/// </summary>
public class BattleMoraleForecastEstimateTests
{
    // The §5.2 reference case from MoraleConstants' hand-calculation: 25% casualties this turn,
    // 25% cumulative, force losing (disadvantage ~0.41) -> stress ~1.06.
    private static BattleMoraleEvaluator.MoraleCheckInput ReferenceInput(float ego, int count)
    {
        var soldiers = new List<BattleMoraleEvaluator.SoldierMoraleInput>();
        for (int i = 0; i < count; i++)
        {
            soldiers.Add(new BattleMoraleEvaluator.SoldierMoraleInput(i + 1, ego, IsLeader: false));
        }
        return new BattleMoraleEvaluator.MoraleCheckInput(
            soldiers,
            CasualtyFractionThisTurn: 0.25f,
            CumulativeCasualtyFraction: 0.25f,
            LeaderDead: false,
            RoutingVisibleFriendlyFraction: 0f,
            LocalOutnumberRatio: 0f,
            CommandAuraSupport: 0f,
            ForceDisadvantage: 0.41f);
    }

    [Fact]
    public void NormalCdf_MatchesKnownValues()
    {
        Assert.Equal(0.5f, BattleMoraleEvaluator.NormalCdf(0f), 3);
        Assert.True(BattleMoraleEvaluator.NormalCdf(3f) > 0.998f);
        Assert.True(BattleMoraleEvaluator.NormalCdf(-3f) < 0.002f);
    }

    [Fact]
    public void EstimateOutcome_UncoveredGauntSwarm_Routs()
    {
        // Ego-8 gaunts that lose synapse fall back to ordinary morale and shatter under the
        // reference stress — the §4.2 severance outcome the forecast must foresee.
        Assert.Equal(MoraleState.Routing, BattleMoraleEvaluator.EstimateOutcome(ReferenceInput(8f, 20)));
    }

    [Fact]
    public void EstimateOutcome_MarineSquad_HoldsSteady()
    {
        Assert.Equal(MoraleState.Steady, BattleMoraleEvaluator.EstimateOutcome(ReferenceInput(14f, 5)));
    }

    [Fact]
    public void EstimateExpectedFailFraction_IsHigherForLowerEgo()
    {
        float gaunt = BattleMoraleEvaluator.EstimateExpectedFailFraction(ReferenceInput(8f, 20));
        float marine = BattleMoraleEvaluator.EstimateExpectedFailFraction(ReferenceInput(14f, 5));

        Assert.True(gaunt > 0.5f, $"expected gaunt fail fraction > 0.5 but was {gaunt}");
        Assert.True(marine < 0.05f, $"expected marine fail fraction < 0.05 but was {marine}");
    }

    [Fact]
    public void Estimators_ConsumeNoRandomness_AndAreRepeatable()
    {
        BattleMoraleEvaluator.MoraleCheckInput input = ReferenceInput(8f, 20);
        Assert.Equal(
            BattleMoraleEvaluator.EstimateExpectedFailFraction(input),
            BattleMoraleEvaluator.EstimateExpectedFailFraction(input));
        Assert.Equal(
            BattleMoraleEvaluator.EstimateOutcome(input),
            BattleMoraleEvaluator.EstimateOutcome(input));
    }
}
