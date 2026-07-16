using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Orders;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattlePursuitPlannerTests
{
    private static BattlePursuitPlanner.Input Input(Aggression aggression = Aggression.Normal) =>
        new(4, true, aggression, 6, 8, 8, 8, 2, 1);

    [Fact]
    public void OutnumberingFasterForce_PressesRegardlessOfCaution()
    {
        var input = new BattlePursuitPlanner.Input(4, true, Aggression.Avoid, 12, 8, 10, 8, 2, 1);

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(input);

        Assert.Equal(PursuitPosture.Press, result.Posture);
        Assert.Equal("outnumbers_and_faster", result.Reason);
    }

    [Theory]
    [InlineData(Aggression.Avoid, PursuitPosture.BreakOff)]
    [InlineData(Aggression.Cautious, PursuitPosture.Follow)]
    [InlineData(Aggression.Attritional, PursuitPosture.Press)]
    [InlineData(Aggression.Aggressive, PursuitPosture.Press)]
    public void NonDominantForce_UsesAggressionPolicy(Aggression aggression, PursuitPosture expected)
    {
        Assert.Equal(expected, BattlePursuitPlanner.Evaluate(Input(aggression)).Posture);
    }

    [Fact]
    public void Normal_PressesWhenInterceptTiesPositiveShot()
    {
        var input = Input() with { ProjectedPressInterceptTurns = 2, ProjectedFollowPositiveShotTurns = 2 };

        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(input).Posture);
    }

    [Fact]
    public void Normal_FollowsWhenPositiveShotComesFirst()
    {
        var input = Input() with { ProjectedPressInterceptTurns = 3, ProjectedFollowPositiveShotTurns = 2 };

        Assert.Equal(PursuitPosture.Follow, BattlePursuitPlanner.Evaluate(input).Posture);
    }

    [Fact]
    public void TraceRenderer_IsStableAndComplete()
    {
        string trace = BattlePursuitPlanner.Evaluate(Input(Aggression.Cautious)).Trace.Render();

        Assert.Equal("PURSUIT_EVAL turn=4 side=first pursuer_soldiers=6 withdrawing_soldiers=8 " +
                     "fastest_pursuit_speed=8 slowest_withdrawal_speed=8 aggression=Cautious " +
                     "press_intercept_turns=2 follow_shot_turns=1 decision=Follow reason=cautious_follows", trace);
    }
}
