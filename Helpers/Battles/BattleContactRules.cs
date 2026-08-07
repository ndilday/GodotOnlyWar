using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Battles;

public enum ContactBreakResult
{
    RemainInContact,
    OrganizedForceDisengages,
    SquadDisengages
}

/// <summary>Open-ground contact-break rules, independent of map boundaries.</summary>
public static class BattleContactRules
{
    public const float MaskedDepartureRunAllowanceMultiplier = 1.0f;

    /// <summary>
    /// How much faster than the slowest withdrawing squad a pursuer must be before it counts as
    /// able to close the gap at all. A hair of extra speed is not a chase: at a tenth of a hex
    /// per turn the pursuer needs hundreds of turns to make up a single hex, which reads as a
    /// hung battle rather than a pursuit. Shared with <see cref="BattlePursuitPlanner"/> so the
    /// posture decision and the contact break agree on what "cannot close" means.
    /// </summary>
    public const float PursuitSpeedAdvantageTolerance = 0.1f;

    /// <summary>
    /// Extra reach beyond a pursuer's move allowance that still lets it reach melee this turn,
    /// matching the Run-to-melee term in the resolver's one-turn attack reach.
    /// </summary>
    public const float MeleeContactAllowance = 1.0f;

    /// <summary>
    /// Whether a pursuer can put someone in melee THIS turn, measured against the gap it can
    /// actually take out of the separation rather than the raw distance it can travel.
    ///
    /// The quarry moves in the same turn, so the distance that matters is the NET closing rate.
    /// Comparing separation to the pursuer's move alone made a stern chase at matched speed read
    /// as permanently one move from contact: separation settles at exactly the pursuer's move
    /// (it gains only the sliver by which it is faster), so "I can reach melee this turn" stayed
    /// true forever while contact never happened. Both escape hatches that end an unwinnable
    /// chase — <see cref="BattlePursuitPlanner"/>'s cannot-close override and the stalled_pursuit
    /// break below — are gated on this test, so the fixed point disabled both and the battle ran
    /// to the resolver's turn cap. Observed 2026-08-04 (Xibarrus Theta): 6.001 vs 6.001,
    /// separation pinned at 6, ~997 turns with nothing landed.
    /// </summary>
    public static bool CanReachMeleeThisTurn(
        float separation,
        float pursuerSpeed,
        float quarrySpeed) =>
        separation <= Math.Max(0, pursuerSpeed - quarrySpeed) + MeleeContactAllowance;

    /// <param name="PursuersAttackedRecently">
    /// The pursuing side produced a damaging action (fire or melee) within the evaluator's recent
    /// window. False means the pursuit is silent — it is neither shooting nor reaching melee.
    /// </param>
    /// <param name="PursuersHaveReasonableShot">
    /// The pursuing side's current fire-control projection says that a worthwhile shot is available
    /// now. This remains true while a stationary shooter is investing turns to mature an aimed shot,
    /// even though no attack action has executed yet.
    /// </param>
    public sealed record Input(
        int Turn,
        bool IsFirstSide,
        int ActivePursuerCount,
        bool AllPursuersBreakOff,
        bool EnemyAlsoWithdrawing,
        float MinimumCurrentSeparation,
        float MaximumOneTurnAttackReach,
        float FastestPursuerSpeed,
        float SlowestWithdrawalSpeed,
        bool RearGuardActive,
        float MaskedDepartureProgress,
        float WithdrawingSquadRunAllowance,
        bool HasImmediateDisengagementCapability = false,
        bool PursuersAttackedRecently = true,
        bool PursuersHaveReasonableShot = false);

    public sealed record Result(ContactBreakResult Decision, string Reason, BattleDecisionTrace Trace);

    public static float RequiredMaskedDepartureDistance(float runAllowance) =>
        runAllowance * MaskedDepartureRunAllowanceMultiplier;

    public static Result Evaluate(Input input)
    {
        float required = RequiredMaskedDepartureDistance(input.WithdrawingSquadRunAllowance);
        ContactBreakResult decision;
        string reason;

        if (input.HasImmediateDisengagementCapability)
            (decision, reason) = (ContactBreakResult.SquadDisengages, "special_capability");
        else if (input.ActivePursuerCount == 0 || input.AllPursuersBreakOff)
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "pursuer_stops");
        else if (input.EnemyAlsoWithdrawing)
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "mutual_withdrawal");
        else if (input.MinimumCurrentSeparation > input.MaximumOneTurnAttackReach &&
                 !PursuerCanClose(input))
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "mobility_break");
        // A pursuit that can neither close the gap nor land a blow has already ended in fact; the
        // mobility break alone does not catch it, because that clause measures separation against
        // the pursuer's *maximum* weapon range. A long-ranged pursuer therefore stays nominally in
        // contact forever at a distance where no shot is ever worth taking. Once its guns have gone
        // silent, has no reasonable shot to prepare, and is no longer running anyone down, let the
        // withdrawal succeed. A stationary shooter may spend several turns maturing an aim before
        // a ShootAction executes. Melee reach is still checked so a pursuer that is merely out of
        // ammo but standing on top of the quarry does not hand it a free escape.
        else if (!input.PursuersAttackedRecently
                 && !input.PursuersHaveReasonableShot
                 && !PursuerCanClose(input)
                 && !CanReachMeleeThisTurn(
                     input.MinimumCurrentSeparation,
                     input.FastestPursuerSpeed,
                     input.SlowestWithdrawalSpeed))
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "stalled_pursuit");
        else if (input.RearGuardActive && input.MaskedDepartureProgress >= required)
            (decision, reason) = (ContactBreakResult.SquadDisengages, "masked_departure");
        else
            (decision, reason) = (ContactBreakResult.RemainInContact, "pursuit_can_maintain_contact");

        BattleDecisionTrace trace = new("CONTACT_EVAL", new List<KeyValuePair<string, string>>
        {
            BattleDecisionTrace.Field("turn", input.Turn),
            BattleDecisionTrace.Field("side", input.IsFirstSide ? "first" : "second"),
            BattleDecisionTrace.Field("active_pursuers", input.ActivePursuerCount),
            BattleDecisionTrace.Field("separation", input.MinimumCurrentSeparation),
            BattleDecisionTrace.Field("attack_reach", input.MaximumOneTurnAttackReach),
            BattleDecisionTrace.Field("pursuer_speed", input.FastestPursuerSpeed),
            BattleDecisionTrace.Field("withdrawal_speed", input.SlowestWithdrawalSpeed),
            BattleDecisionTrace.Field("pursuers_attacked", input.PursuersAttackedRecently),
            BattleDecisionTrace.Field("pursuers_reasonable_shot", input.PursuersHaveReasonableShot),
            BattleDecisionTrace.Field("rear_guard_active", input.RearGuardActive),
            BattleDecisionTrace.Field("masked_progress", input.MaskedDepartureProgress),
            BattleDecisionTrace.Field("masked_required", required),
            BattleDecisionTrace.Field("decision", decision),
            BattleDecisionTrace.Field("reason", reason)
        });
        BattleLog.Write(trace.Render());
        return new Result(decision, reason, trace);
    }

    private static bool PursuerCanClose(Input input) =>
        input.FastestPursuerSpeed
            > input.SlowestWithdrawalSpeed + PursuitSpeedAdvantageTolerance;
}
