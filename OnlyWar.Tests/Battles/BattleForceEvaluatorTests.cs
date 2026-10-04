using System;
using System.Collections.Generic;

using OnlyWar.Domain;
using OnlyWar.Battles;
using OnlyWar.Domain.Orders;

using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleForceEvaluatorTests
{
    [Theory]
    [InlineData(Aggression.Avoid, 89, true)]
    [InlineData(Aggression.Avoid, 90, false)]
    [InlineData(Aggression.Cautious, 74, true)]
    [InlineData(Aggression.Cautious, 75, false)]
    [InlineData(Aggression.Normal, 49, true)]
    [InlineData(Aggression.Normal, 50, false)]
    [InlineData(Aggression.Attritional, 24, true)]
    [InlineData(Aggression.Attritional, 25, false)]
    public void Evaluate_UsesStrictAggressionRemainingShareThresholds(
        Aggression aggression,
        int remainingPercent,
        bool expectedEligible)
    {
        // The enemy has lost a smaller share, so an eligible friendly force is also outmatched.
        BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(Input(
            aggression,
            friendly: Metrics(remainingPercent),
            enemy: Metrics(remainingPercent + 1)));

        Assert.Equal(expectedEligible, result.IsEligible);
        Assert.Equal(expectedEligible, result.ShouldWithdraw);
    }

    [Fact]
    public void Evaluate_AggressiveForceDoesNotWithdrawFromCasualtiesAlone()
    {
        BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(Input(
            Aggression.Aggressive,
            friendly: Metrics(1, loss: 99, viableDamage: false, canProsecute: false),
            enemy: Metrics(100, viableDamage: true)));

        Assert.False(result.IsEligible);
        Assert.False(result.ShouldWithdraw);
        Assert.Null(result.EligibilityThreshold);
        Assert.Equal(
            VoluntaryWithdrawalReason.AggressiveCasualtyTolerance,
            result.Reason);
    }

    [Fact]
    public void Evaluate_EligibleForceStaysWhenNoContinuationPressureExists()
    {
        BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(Input(
            Aggression.Normal,
            friendly: Metrics(40, loss: 2),
            enemy: Metrics(35, loss: 3)));

        Assert.True(result.IsEligible);
        Assert.False(result.ShouldWithdraw);
        Assert.Equal(VoluntaryWithdrawalReason.EligibleButNoPressure, result.Reason);
    }

    [Fact]
    public void Evaluate_EqualLossSharesAreNotOutmatched()
    {
        // Both sides have lost the same share of their own strength. The rule compares shares,
        // never absolute strength, so even a much smaller force is not "outmatched" by ties.
        BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(Input(
            Aggression.Cautious,
            friendly: Metrics(70),
            enemy: Metrics(70)));

        Assert.True(result.IsEligible);
        Assert.False(result.ShouldWithdraw);
        Assert.Equal(VoluntaryWithdrawalReason.EligibleButNoPressure, result.Reason);
    }

    [Theory]
    [InlineData(39, 40, 2, 2, true, true, true,
        VoluntaryWithdrawalReason.EligibleAndOutmatched)]
    [InlineData(40, 35, 4, 3, true, true, true,
        VoluntaryWithdrawalReason.EligibleAndLosingExchange)]
    [InlineData(40, 35, 2, 3, false, true, true,
        VoluntaryWithdrawalReason.EligibleAndUnableToDamage)]
    [InlineData(40, 35, 2, 3, true, true, false,
        VoluntaryWithdrawalReason.EligibleAndMissionIncapable)]
    public void Evaluate_EligibleForceWithdrawsForEachContinuationRule(
        int friendlyRemainingPercent,
        int enemyRemainingPercent,
        int friendlyLossPercent,
        int enemyLossPercent,
        bool friendlyViableDamage,
        bool enemyViableDamage,
        bool canProsecute,
        VoluntaryWithdrawalReason expectedReason)
    {
        BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(Input(
            Aggression.Normal,
            friendly: Metrics(
                friendlyRemainingPercent,
                friendlyLossPercent,
                friendlyViableDamage,
                canProsecute),
            enemy: Metrics(enemyRemainingPercent, enemyLossPercent, enemyViableDamage)));

        Assert.True(result.ShouldWithdraw);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Fact]
    public void Evaluate_PreservesAnExistingWithdrawalWithoutOscillation()
    {
        BattleForceEvaluationInput input = Input(
            Aggression.Normal,
            friendly: Metrics(100),
            enemy: Metrics(10)) with
        {
            WithdrawalAlreadyOrdered = true
        };

        BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(input);

        Assert.True(result.ShouldWithdraw);
        Assert.Equal(VoluntaryWithdrawalReason.AlreadyWithdrawing, result.Reason);
    }

    [Fact]
    public void Evaluate_IsDeterministicAndConsumesNoRandomSource()
    {
        BattleForceEvaluationInput input = Input(
            Aggression.Cautious,
            friendly: Metrics(68, loss: 11),
            enemy: Metrics(61, loss: 3));

        BattleForceEvaluationResult first = BattleForceEvaluator.Evaluate(input);
        BattleForceEvaluationResult second = BattleForceEvaluator.Evaluate(input);

        Assert.Equal(first.Decision, second.Decision);
        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(first.IsEligible, second.IsEligible);
        Assert.Equal(first.RemainingShare, second.RemainingShare);
        Assert.Equal(first.EligibilityThreshold, second.EligibilityThreshold);
        Assert.Equal(first.Trace.Render(), second.Trace.Render());
    }

    [Fact]
    public void WithdrawTrace_HasStableFieldsAndInvariantRendering()
    {
        BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(new(
            Turn: 8,
            Side: "first",
            Aggression: Aggression.Cautious,
            Friendly: Metrics(68, loss: 11),
            Enemy: Metrics(61, loss: 3)));

        Assert.Equal("WITHDRAW_EVAL", result.Trace.RecordType);
        Assert.Equal("8", result.Trace["turn"]);
        Assert.Equal("first", result.Trace["side"]);
        Assert.Equal("eligible_and_losing_exchange", result.Trace["reason"]);
        Assert.Equal(
            "WITHDRAW_EVAL turn=8 side=first aggression=Cautious remaining=0.68 "
            + "threshold=0.75 friendly_lost=0.32 enemy_lost=0.39 "
            + "friendly_loss_2r=0.11 enemy_loss_2r=0.03 friendly_viable_damage=true "
            + "enemy_viable_damage=true can_prosecute_mission=true "
            + "decision=FightingWithdrawal reason=eligible_and_losing_exchange",
            result.Trace.Render());
    }

    internal static BattleForceEvaluationInput Input(
        Aggression aggression,
        BattleForceMetrics friendly,
        BattleForceMetrics enemy)
    {
        return new BattleForceEvaluationInput(
            Turn: 4,
            Side: "first",
            Aggression: aggression,
            Friendly: friendly,
            Enemy: enemy);
    }

    /// <summary>A force snapshot expressed as percentages of the side's own starting strength.</summary>
    internal static BattleForceMetrics Metrics(
        int remainingPercent,
        int loss = 0,
        bool viableDamage = true,
        bool canProsecute = true)
    {
        return new BattleForceMetrics(
            RemainingShare: remainingPercent / 100.0,
            LostSharePreviousTwoRounds: loss / 100.0,
            AbleSoldierCount: System.Math.Max(0, remainingPercent / 10),
            FastestPursuitSquadSpeed: 6,
            SlowestMainBodySquadSpeed: 5,
            RangedCoverSquadCount: 1,
            AnySquadInMelee: false,
            HasViableDamagingActionRecently: viableDamage,
            CanAnySquadProsecuteMission: canProsecute);
    }
}

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleForceEvaluatorSharedStateTests
{
    [Fact]
    public void Evaluate_WritesTraceOnlyWhenBattleLoggingIsEnabled()
    {
        List<string> logged = [];
        Action<string> originalSink = BattleLog.Sink;
        try
        {
            BattleLog.Sink = logged.Add;

            BattleForceEvaluationResult result = BattleForceEvaluator.Evaluate(
                BattleForceEvaluatorTests.Input(
                    Aggression.Normal,
                    friendly: BattleForceEvaluatorTests.Metrics(40),
                    enemy: BattleForceEvaluatorTests.Metrics(50)));

            Assert.Single(logged);
            Assert.Equal(result.Trace.Render(), logged[0]);

            BattleLog.Sink = null;
            BattleForceEvaluator.Evaluate(BattleForceEvaluatorTests.Input(
                Aggression.Normal,
                friendly: BattleForceEvaluatorTests.Metrics(40),
                enemy: BattleForceEvaluatorTests.Metrics(50)));
            Assert.Single(logged);
        }
        finally
        {
            BattleLog.Sink = originalSink;
        }
    }
}
