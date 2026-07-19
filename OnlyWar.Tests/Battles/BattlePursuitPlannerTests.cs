using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Orders;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattlePursuitPlannerTests
{
    private static BattlePursuitPlanner.Input Input(Aggression aggression = Aggression.Normal) =>
        new(4, true, aggression, 6, 8, 8, 8, 2, 1, WithdrawerReturnsFire: true);

    [Fact]
    public void OutnumberingFasterForce_StillAnswersToItsAggressionPolicy()
    {
        // The old outnumber-and-faster shortcut forced Press here; a dominant pursuer now
        // follows the same policy as anyone else (a shooting-superior force should not be
        // railroaded into melee just because it is winning).
        var input = new BattlePursuitPlanner.Input(
            4, true, Aggression.Avoid, 12, 8, 10, 8, 2, 1, WithdrawerReturnsFire: true);

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(input);

        Assert.Equal(PursuitPosture.BreakOff, result.Posture);
        Assert.Equal("avoid_breaks_off", result.Reason);
    }

    [Theory]
    [InlineData(Aggression.Avoid, PursuitPosture.BreakOff)]
    [InlineData(Aggression.Cautious, PursuitPosture.Follow)]
    [InlineData(Aggression.Attritional, PursuitPosture.Press)]
    [InlineData(Aggression.Aggressive, PursuitPosture.Press)]
    public void Pursuer_UsesAggressionPolicy(Aggression aggression, PursuitPosture expected)
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

    [Theory]
    [InlineData(Aggression.Attritional)]
    [InlineData(Aggression.Aggressive)]
    [InlineData(Aggression.Normal)]
    public void FasterArmedPursuer_ShootsUnresistingPrey_InsteadOfPressing(Aggression aggression)
    {
        // Faster pursuer, positive follow shot available, and the withdrawer is not
        // returning fire (routed, or melee-only): even the most aggressive posture keeps
        // shooting rather than closing to melee. Projections favor Press (intercept turn 1
        // beats first positive shot at turn 2) so every one of these aggressions would
        // otherwise Press — the override is what flips them.
        var input = new BattlePursuitPlanner.Input(
            4, true, aggression, 12, 8, 10, 8, 1, 2, WithdrawerReturnsFire: false);

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(input);

        Assert.Equal(PursuitPosture.Follow, result.Posture);
        Assert.Equal("shoots_unresisting_prey", result.Reason);
    }

    [Fact]
    public void UnresistingPreyOverride_RequiresSpeedAdvantageAndAWorkingGun()
    {
        // Not faster: Press stands (following would let the quarry open the gap).
        var slower = new BattlePursuitPlanner.Input(
            4, true, Aggression.Aggressive, 12, 8, 8, 8, 2, 1, WithdrawerReturnsFire: false);
        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(slower).Posture);

        // No projected positive follow shot (no ranged weapons): Press stands.
        var meleeOnly = new BattlePursuitPlanner.Input(
            4, true, Aggression.Aggressive, 12, 8, 10, 8, 2, null, WithdrawerReturnsFire: false);
        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(meleeOnly).Posture);
    }

    [Fact]
    public void TraceRenderer_IsStableAndComplete()
    {
        string trace = BattlePursuitPlanner.Evaluate(Input(Aggression.Cautious)).Trace.Render();

        Assert.Equal("PURSUIT_EVAL turn=4 side=first pursuer_soldiers=6 withdrawing_soldiers=8 " +
                     "fastest_pursuit_speed=8 slowest_withdrawal_speed=8 aggression=Cautious " +
                     "withdrawer_returns_fire=true " +
                     "press_intercept_turns=2 follow_shot_turns=1 decision=Follow reason=cautious_follows", trace);
    }
}
