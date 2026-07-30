using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Orders;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattlePursuitPlannerTests
{
    // A pursuer with a genuine speed edge (10 vs 8), so the aggression policy is what these
    // cases exercise and not the cannot-close override.
    private static BattlePursuitPlanner.Input Input(Aggression aggression = Aggression.Normal) =>
        new(4, true, aggression, 6, 8, 10, 8, 2, 1, WithdrawerReturnsFire: true);

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
    public void UnresistingPreyOverride_RequiresAWorkingGun()
    {
        // No projected positive follow shot (no ranged weapons): Press stands. A melee-only
        // pursuer has nothing better to do than keep contact; the contact rules end the chase
        // once it demonstrably lands nothing.
        var meleeOnly = new BattlePursuitPlanner.Input(
            4, true, Aggression.Aggressive, 12, 8, 10, 8, 2, null, WithdrawerReturnsFire: false);
        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(meleeOnly).Posture);
    }

    // Equal speed, quarry already in range (a positive shot is available this turn, not in two),
    // and out of melee reach: the chase is unwinnable and only the guns are left.
    private static BattlePursuitPlanner.Input StalledChase(Aggression aggression) =>
        new(4, true, aggression, 12, 8, 8, 8, 1, 0, WithdrawerReturnsFire: true,
            PursuerCanReachMeleeThisTurn: false);

    [Theory]
    [InlineData(Aggression.Cautious)]
    [InlineData(Aggression.Normal)]
    [InlineData(Aggression.Attritional)]
    [InlineData(Aggression.Aggressive)]
    public void PursuerWithNoSpeedEdge_StandsAndFiresInsteadOfChasing(Aggression aggression)
    {
        // Neither chasing posture pays here. Press never shoots; Follow jogs at half a run, so
        // it cannot hold the gap either, and buys its extra turns in range with full-Bulk,
        // unaimed shots. Standing still is the higher-damage option for all four policies.
        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(StalledChase(aggression));

        Assert.Equal(PursuitPosture.Standoff, result.Posture);
        Assert.Equal("cannot_close_stands_and_fires", result.Reason);
    }

    [Fact]
    public void PursuerThatCanNeitherCloseNorShoot_BreaksOff()
    {
        // Out of range with no way to regain it (the projected shot is two turns of jogging
        // away, which an equal-speed quarry simply outruns): there is nothing left to gain from
        // contact, so the engagement ends rather than grinding to the turn cap.
        var outOfRange = StalledChase(Aggression.Aggressive) with
        {
            ProjectedFollowPositiveShotTurns = 2
        };
        Assert.Equal(PursuitPosture.BreakOff, BattlePursuitPlanner.Evaluate(outOfRange).Posture);
        Assert.Equal("cannot_close_or_shoot", BattlePursuitPlanner.Evaluate(outOfRange).Reason);

        // Same for a melee-only pursuer that will never lay a hand on them.
        var noGun = StalledChase(Aggression.Aggressive) with
        {
            ProjectedFollowPositiveShotTurns = null
        };
        Assert.Equal(PursuitPosture.BreakOff, BattlePursuitPlanner.Evaluate(noGun).Posture);
    }

    [Fact]
    public void CannotCloseOverride_YieldsToAMeleeCatchAvailableThisTurn()
    {
        // Matched speeds do not matter when the pursuer is already on top of the quarry — it can
        // catch someone this turn, so its policy stands.
        var inContact = StalledChase(Aggression.Aggressive) with
        {
            PursuerCanReachMeleeThisTurn = true
        };

        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(inContact).Posture);
    }

    [Fact]
    public void CannotCloseOverride_TreatsATrivialSpeedEdgeAsNoEdgeAtAll()
    {
        // Inside the shared tolerance the pursuer would need hundreds of turns to make up a
        // single hex; outside it, the chase is real and the aggression policy stands.
        var withinTolerance = StalledChase(Aggression.Aggressive) with
        {
            FastestPursuitSpeed = 8.1f
        };
        Assert.Equal(PursuitPosture.Standoff, BattlePursuitPlanner.Evaluate(withinTolerance).Posture);

        var beyondTolerance = withinTolerance with { FastestPursuitSpeed = 8.2f };
        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(beyondTolerance).Posture);
    }

    [Fact]
    public void TraceRenderer_IsStableAndComplete()
    {
        string trace = BattlePursuitPlanner.Evaluate(Input(Aggression.Cautious)).Trace.Render();

        Assert.Equal("PURSUIT_EVAL turn=4 side=first pursuer_soldiers=6 withdrawing_soldiers=8 " +
                     "fastest_pursuit_speed=10 slowest_withdrawal_speed=8 aggression=Cautious " +
                     "withdrawer_returns_fire=true melee_reach_this_turn=false " +
                     "press_intercept_turns=2 follow_shot_turns=1 decision=Follow reason=cautious_follows", trace);
    }
}
