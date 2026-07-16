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
        bool HasImmediateDisengagementCapability = false);

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
                 input.FastestPursuerSpeed <= input.SlowestWithdrawalSpeed)
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "mobility_break");
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
            BattleDecisionTrace.Field("rear_guard_active", input.RearGuardActive),
            BattleDecisionTrace.Field("masked_progress", input.MaskedDepartureProgress),
            BattleDecisionTrace.Field("masked_required", required),
            BattleDecisionTrace.Field("decision", decision),
            BattleDecisionTrace.Field("reason", reason)
        });
        BattleLog.Write(trace.Render());
        return new Result(decision, reason, trace);
    }
}
