using System.Collections.Generic;
using OnlyWar.Models.Orders;

namespace OnlyWar.Helpers.Battles;

public enum PursuitPosture
{
    BreakOff,
    Follow,
    Press
}

/// <summary>Pure force-level pursuit policy. Action planning remains the resolver's concern.</summary>
public static class BattlePursuitPlanner
{
    public sealed record Input(
        int Turn,
        bool IsFirstSide,
        Aggression Aggression,
        int PursuerAbleSoldiers,
        int WithdrawingAbleSoldiers,
        float FastestPursuitSpeed,
        float SlowestWithdrawalSpeed,
        float ProjectedPressInterceptTurns,
        float? ProjectedFollowPositiveShotTurns,
        bool WithdrawerReturnsFire);

    public sealed record Result(PursuitPosture Posture, string Reason, BattleDecisionTrace Trace);

    public static Result Evaluate(Input input)
    {
        // No strength shortcut here: outnumbering a slower enemy is not by itself a reason
        // to prefer running them down over a firing pursuit — a shooting-superior force
        // pressing into melee hands the enemy the exchange it was losing at range. Every
        // pursuer answers to its aggression policy; Normal weighs intercept-first against
        // shoot-first via SelectNormal.
        (PursuitPosture posture, string reason) = input.Aggression switch
        {
            Aggression.Avoid => (PursuitPosture.BreakOff, "avoid_breaks_off"),
            Aggression.Cautious => (PursuitPosture.Follow, "cautious_follows"),
            Aggression.Attritional => (PursuitPosture.Press, "attritional_presses"),
            Aggression.Aggressive => (PursuitPosture.Press, "aggressive_presses"),
            _ => SelectNormal(input)
        };

        // Even the most aggressive pursuer keeps shooting rather than closing to melee when
        // the prey is helpless at range: the pursuer has guns that will reach (a positive
        // follow shot exists), is faster (so following concedes nothing — the quarry cannot
        // open the gap), and the withdrawer is not returning fire (routing, or simply has no
        // ranged weapons). Charging such an enemy only hands it the melee exchange it wanted.
        if (posture == PursuitPosture.Press
            && !input.WithdrawerReturnsFire
            && input.ProjectedFollowPositiveShotTurns.HasValue
            && input.FastestPursuitSpeed > input.SlowestWithdrawalSpeed)
        {
            posture = PursuitPosture.Follow;
            reason = "shoots_unresisting_prey";
        }

        BattleDecisionTrace trace = new("PURSUIT_EVAL", new List<KeyValuePair<string, string>>
        {
            BattleDecisionTrace.Field("turn", input.Turn),
            BattleDecisionTrace.Field("side", input.IsFirstSide ? "first" : "second"),
            BattleDecisionTrace.Field("pursuer_soldiers", input.PursuerAbleSoldiers),
            BattleDecisionTrace.Field("withdrawing_soldiers", input.WithdrawingAbleSoldiers),
            BattleDecisionTrace.Field("fastest_pursuit_speed", input.FastestPursuitSpeed),
            BattleDecisionTrace.Field("slowest_withdrawal_speed", input.SlowestWithdrawalSpeed),
            BattleDecisionTrace.Field("aggression", input.Aggression),
            BattleDecisionTrace.Field("withdrawer_returns_fire", input.WithdrawerReturnsFire),
            BattleDecisionTrace.Field("press_intercept_turns", input.ProjectedPressInterceptTurns),
            BattleDecisionTrace.Field("follow_shot_turns", input.ProjectedFollowPositiveShotTurns),
            BattleDecisionTrace.Field("decision", posture),
            BattleDecisionTrace.Field("reason", reason)
        });
        BattleLog.Write(trace.Render());
        return new Result(posture, reason, trace);
    }

    private static (PursuitPosture Posture, string Reason) SelectNormal(Input input)
    {
        if (!input.ProjectedFollowPositiveShotTurns.HasValue)
            return (PursuitPosture.Press, "normal_no_positive_follow_shot");

        return input.ProjectedPressInterceptTurns <= input.ProjectedFollowPositiveShotTurns.Value
            ? (PursuitPosture.Press, "normal_press_reaches_first_or_ties")
            : (PursuitPosture.Follow, "normal_follow_shoots_first");
    }
}
