using System;
using System.Collections.Generic;

using OnlyWar.Models.Orders;

namespace OnlyWar.Helpers.Battles
{
    public sealed record BattleForceMetrics(
        int StartingBattleValue,
        int CurrentBattleValue,
        int BattleValueLostPreviousTwoRounds,
        int AbleSoldierCount,
        float FastestPursuitSquadSpeed,
        float SlowestMainBodySquadSpeed,
        int RangedCoverSquadCount,
        bool AnySquadInMelee,
        bool HasViableDamagingActionRecently,
        bool CanAnySquadProsecuteMission);

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
        double RemainingBattleValueFraction,
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

            double remaining = input.Friendly.StartingBattleValue > 0
                ? (double)input.Friendly.CurrentBattleValue / input.Friendly.StartingBattleValue
                : 0;
            double? threshold = GetEligibilityThreshold(input.Aggression);
            bool eligible = threshold.HasValue && remaining < threshold.Value;

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

            if (input.Friendly.CurrentBattleValue < input.Enemy.CurrentBattleValue)
            {
                return (
                    VoluntaryWithdrawalDecision.FightingWithdrawal,
                    VoluntaryWithdrawalReason.EligibleAndOutmatched);
            }

            if (input.Friendly.BattleValueLostPreviousTwoRounds
                > input.Enemy.BattleValueLostPreviousTwoRounds)
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
                BattleDecisionTrace.Field("start_bv", input.Friendly.StartingBattleValue),
                BattleDecisionTrace.Field("current_bv", input.Friendly.CurrentBattleValue),
                BattleDecisionTrace.Field("remaining", remaining),
                BattleDecisionTrace.Field("threshold", threshold),
                BattleDecisionTrace.Field("friendly_bv", input.Friendly.CurrentBattleValue),
                BattleDecisionTrace.Field("enemy_bv", input.Enemy.CurrentBattleValue),
                BattleDecisionTrace.Field(
                    "friendly_loss_2r",
                    input.Friendly.BattleValueLostPreviousTwoRounds),
                BattleDecisionTrace.Field(
                    "enemy_loss_2r",
                    input.Enemy.BattleValueLostPreviousTwoRounds),
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
