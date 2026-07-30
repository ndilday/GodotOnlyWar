using System.Collections.Generic;
using OnlyWar.Models.Orders;

namespace OnlyWar.Helpers.Battles;

/// <summary>
/// Ordered by how much the pursuer commits to closing the distance. Never persisted or
/// rendered — the resolver holds it for a turn and the squad planner switches on it — so the
/// ordinals carry no meaning beyond readability.
/// </summary>
public enum PursuitPosture
{
    BreakOff,
    /// <summary>Hold position and deliver aimed standing fire; do not chase.</summary>
    Standoff,
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
        bool WithdrawerReturnsFire,
        bool PursuerCanReachMeleeThisTurn = false);

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

        // A pursuer with no real speed edge is never going to run its quarry down, and neither
        // chasing posture is worth anything against it. Pressing trails the withdrawal at a
        // fixed gap with the guns silent; Following is worse than it looks, because a jog is
        // half of a run — it does not hold the gap, it only halves the rate the gap opens — and
        // moving fire pays the full Bulk penalty with the aim bonus zeroed. Standing and
        // shooting aimed does more damage per turn than either, so the pursuer plants itself:
        // fewer turns in range, but every shot is a real one. If it has no shot at all there is
        // nothing left to gain from contact and it breaks off, ending the engagement.
        //
        // This overrides every aggression policy — arithmetic, not temperament, decides whether
        // a quarry is catchable — but only once the pursuer has actually lost the chase. A
        // pursuer that can reach melee THIS turn can still catch someone despite the equal
        // speeds, so it keeps whatever posture its policy chose. The tolerance is shared with
        // BattleContactRules so the posture and the contact break agree on "cannot close".
        if (posture != PursuitPosture.BreakOff
            && !input.PursuerCanReachMeleeThisTurn
            && input.FastestPursuitSpeed
                <= input.SlowestWithdrawalSpeed + BattleContactRules.PursuitSpeedAdvantageTolerance)
        {
            (posture, reason) = input.ProjectedFollowPositiveShotTurns == 0f
                ? (PursuitPosture.Standoff, "cannot_close_stands_and_fires")
                : (PursuitPosture.BreakOff, "cannot_close_or_shoot");
        }

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
            BattleDecisionTrace.Field("melee_reach_this_turn", input.PursuerCanReachMeleeThisTurn),
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
