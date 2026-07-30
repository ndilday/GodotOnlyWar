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

    /// <param name="PursuersAttackedRecently">
    /// The pursuing side produced a damaging action (fire or melee) within the evaluator's recent
    /// window. False means the pursuit is silent — it is neither shooting nor reaching melee.
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
        bool PursuersAttackedRecently = true);

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
        // silent and it is no longer running anyone down, let the withdrawal succeed. Melee reach
        // is still checked so a pursuer that is merely out of ammo but standing on top of the
        // quarry does not hand it a free escape.
        else if (!input.PursuersAttackedRecently
                 && !PursuerCanClose(input)
                 && input.MinimumCurrentSeparation
                     > input.FastestPursuerSpeed + MeleeContactAllowance)
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
