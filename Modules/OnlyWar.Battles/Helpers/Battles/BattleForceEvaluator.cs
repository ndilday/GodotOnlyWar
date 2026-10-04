using System;
using System.Collections.Generic;

using OnlyWar.Domain.Orders;

namespace OnlyWar.Battles
{
    /// <param name="RemainingShare">
    /// Sum over the side's squads of w * able / starting, where w is the squad's share of its OWN
    /// side's starting battle value. 1 at battle start, 0 when no squad has an able soldier.
    /// </param>
    /// <param name="LostSharePreviousTwoRounds">
    /// The same weighted sum over soldiers lost in the previous two rounds: w * lost / starting.
    /// </param>
    public sealed record BattleForceMetrics(
        double RemainingShare,
        double LostSharePreviousTwoRounds,
        int AbleSoldierCount,
        float FastestPursuitSquadSpeed,
        float SlowestMainBodySquadSpeed,
        int RangedCoverSquadCount,
        bool AnySquadInMelee,
        bool HasViableDamagingActionRecently,
        bool CanAnySquadProsecuteMission)
    {
        /// <summary>Share of the side's starting strength lost so far (1 - RemainingShare).</summary>
        public double LostShareSinceStart => 1.0 - RemainingShare;
    }

    public sealed record BattleForceEvaluationInput(
        int Turn,
        string Side,
        Aggression Aggression,
        BattleForceMetrics Friendly,
        BattleForceMetrics Enemy,
        bool WithdrawalAlreadyOrdered = false);

    public enum VoluntaryWithdrawalDecision
    {
        Engaged,
        FightingWithdrawal
    }

    public enum VoluntaryWithdrawalReason
    {
        AlreadyWithdrawing,
        AggressiveCasualtyTolerance,
        AboveCasualtyThreshold,
        EligibleButNoPressure,
        EligibleAndOutmatched,
        EligibleAndLosingExchange,
        EligibleAndUnableToDamage,
        EligibleAndMissionIncapable
    }

    public sealed record BattleForceEvaluationResult(
        VoluntaryWithdrawalDecision Decision,
        VoluntaryWithdrawalReason Reason,
        bool IsEligible,
        double RemainingShare,
        double? EligibilityThreshold,
        BattleDecisionTrace Trace)
    {
        public bool ShouldWithdraw => Decision == VoluntaryWithdrawalDecision.FightingWithdrawal;
    }

    /// <summary>
    /// Evaluates voluntary withdrawal from an immutable force snapshot. The decision consumes no
    /// RNG and has no Godot or campaign dependencies. Its only side effect is the required
    /// diagnostic write, which BattleLog discards when battle logging is disabled.
    /// </summary>
    public static class BattleForceEvaluator
    {
        // Shares are sums of weighted fractions, so two mathematically equal values can differ in
        // the last bits. Differences below this are ties (and exact threshold hits are not
        // "below" the threshold), matching the exact integer comparisons the BV metrics gave.
        private const double ShareTolerance = 1e-9;

        public const double AvoidEligibilityThreshold = 0.90;
        public const double CautiousEligibilityThreshold = 0.75;
        public const double NormalEligibilityThreshold = 0.50;
        public const double AttritionalEligibilityThreshold = 0.25;

        public static double? GetEligibilityThreshold(Aggression aggression) => aggression switch
        {
            Aggression.Avoid => AvoidEligibilityThreshold,
            Aggression.Cautious => CautiousEligibilityThreshold,
            Aggression.Normal => NormalEligibilityThreshold,
            Aggression.Attritional => AttritionalEligibilityThreshold,
            Aggression.Aggressive => null,
            _ => throw new ArgumentOutOfRangeException(nameof(aggression), aggression, null)
        };

        public static BattleForceEvaluationResult Evaluate(BattleForceEvaluationInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Friendly);
            ArgumentNullException.ThrowIfNull(input.Enemy);

            double remaining = input.Friendly.RemainingShare;
            double? threshold = GetEligibilityThreshold(input.Aggression);
            bool eligible = threshold.HasValue && remaining < threshold.Value - ShareTolerance;

            (VoluntaryWithdrawalDecision decision, VoluntaryWithdrawalReason reason) =
                SelectDecision(input, eligible, threshold);

            BattleDecisionTrace trace = BuildTrace(
                input,
                remaining,
                threshold,
                decision,
                reason);
            BattleLog.Write(trace.Render());

            return new BattleForceEvaluationResult(
                decision,
                reason,
                eligible,
                remaining,
                threshold,
                trace);
        }

        private static (VoluntaryWithdrawalDecision, VoluntaryWithdrawalReason) SelectDecision(
            BattleForceEvaluationInput input,
            bool eligible,
            double? threshold)
        {
            if (input.WithdrawalAlreadyOrdered)
            {
                return (
                    VoluntaryWithdrawalDecision.FightingWithdrawal,
                    VoluntaryWithdrawalReason.AlreadyWithdrawing);
            }

            if (!threshold.HasValue)
            {
                return (
                    VoluntaryWithdrawalDecision.Engaged,
                    VoluntaryWithdrawalReason.AggressiveCasualtyTolerance);
            }

            if (!eligible)
            {
                return (
                    VoluntaryWithdrawalDecision.Engaged,
                    VoluntaryWithdrawalReason.AboveCasualtyThreshold);
            }

            // Cumulative loss shares: each side's fraction of its own starting strength lost.
            if (input.Friendly.LostShareSinceStart
                > input.Enemy.LostShareSinceStart + ShareTolerance)
            {
                return (
                    VoluntaryWithdrawalDecision.FightingWithdrawal,
                    VoluntaryWithdrawalReason.EligibleAndOutmatched);
            }

            if (input.Friendly.LostSharePreviousTwoRounds
                > input.Enemy.LostSharePreviousTwoRounds + ShareTolerance)
            {
                return (
                    VoluntaryWithdrawalDecision.FightingWithdrawal,
                    VoluntaryWithdrawalReason.EligibleAndLosingExchange);
            }

            if (!input.Friendly.HasViableDamagingActionRecently
                && input.Enemy.HasViableDamagingActionRecently)
            {
                return (
                    VoluntaryWithdrawalDecision.FightingWithdrawal,
                    VoluntaryWithdrawalReason.EligibleAndUnableToDamage);
            }

            if (!input.Friendly.CanAnySquadProsecuteMission)
            {
                return (
                    VoluntaryWithdrawalDecision.FightingWithdrawal,
                    VoluntaryWithdrawalReason.EligibleAndMissionIncapable);
            }

            return (
                VoluntaryWithdrawalDecision.Engaged,
                VoluntaryWithdrawalReason.EligibleButNoPressure);
        }

        private static BattleDecisionTrace BuildTrace(
            BattleForceEvaluationInput input,
            double remaining,
            double? threshold,
            VoluntaryWithdrawalDecision decision,
            VoluntaryWithdrawalReason reason)
        {
            List<KeyValuePair<string, string>> fields =
            [
                BattleDecisionTrace.Field("turn", input.Turn),
                BattleDecisionTrace.Field("side", input.Side),
                BattleDecisionTrace.Field("aggression", input.Aggression),
                BattleDecisionTrace.Field("remaining", remaining),
                BattleDecisionTrace.Field("threshold", threshold),
                BattleDecisionTrace.Field("friendly_lost", input.Friendly.LostShareSinceStart),
                BattleDecisionTrace.Field("enemy_lost", input.Enemy.LostShareSinceStart),
                BattleDecisionTrace.Field(
                    "friendly_loss_2r",
                    input.Friendly.LostSharePreviousTwoRounds),
                BattleDecisionTrace.Field(
                    "enemy_loss_2r",
                    input.Enemy.LostSharePreviousTwoRounds),
                BattleDecisionTrace.Field(
                    "friendly_viable_damage",
                    input.Friendly.HasViableDamagingActionRecently),
                BattleDecisionTrace.Field(
                    "enemy_viable_damage",
                    input.Enemy.HasViableDamagingActionRecently),
                BattleDecisionTrace.Field(
                    "can_prosecute_mission",
                    input.Friendly.CanAnySquadProsecuteMission),
                BattleDecisionTrace.Field("decision", decision),
                BattleDecisionTrace.Field("reason", RenderReason(reason))
            ];

            return new BattleDecisionTrace("WITHDRAW_EVAL", fields);
        }

        private static string RenderReason(VoluntaryWithdrawalReason reason) => reason switch
        {
            VoluntaryWithdrawalReason.AlreadyWithdrawing => "already_withdrawing",
            VoluntaryWithdrawalReason.AggressiveCasualtyTolerance =>
                "aggressive_casualty_tolerance",
            VoluntaryWithdrawalReason.AboveCasualtyThreshold => "above_casualty_threshold",
            VoluntaryWithdrawalReason.EligibleButNoPressure => "eligible_but_no_pressure",
            VoluntaryWithdrawalReason.EligibleAndOutmatched => "eligible_and_outmatched",
            VoluntaryWithdrawalReason.EligibleAndLosingExchange =>
                "eligible_and_losing_exchange",
            VoluntaryWithdrawalReason.EligibleAndUnableToDamage =>
                "eligible_and_unable_to_damage",
            VoluntaryWithdrawalReason.EligibleAndMissionIncapable =>
                "eligible_and_mission_incapable",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
    }
}
