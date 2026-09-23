using System;
using System.Collections.Generic;

namespace OnlyWar.Battles;

/// <summary>
/// Shared machine-readable fields for counterfactual interception and useful-attack projections.
/// Keeping the formatting here prevents the press, follow, and escape traces from silently
/// acquiring different meanings for the same geometry terms.
/// </summary>
internal static class BattleProjectionDiagnosticTrace
{
    internal static void AddInterception(
        ICollection<KeyValuePair<string, string>> fields,
        string prefix,
        BattleInterceptionProjection.AggregateResult projection,
        string destinationCondition)
    {
        BattleInterceptionProjection.PairInput? input = projection.SelectedInput;
        BattleInterceptionProjection.PairResult? result = projection.SelectedPair;
        bool hasCandidate = input.HasValue && result.HasValue;
        int? pursuerId = projection.PursuerSquadId
            ?? (hasCandidate ? input.Value.PursuerSquadId : null);
        int? quarryId = projection.QuarrySquadId
            ?? (hasCandidate ? input.Value.QuarrySquadId : null);

        fields.Add(BattleDecisionTrace.Field($"{prefix}_supplier_status",
            projection.IsReachable ? "finite" : hasCandidate ? "unreachable" : "none"));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_supplier_pursuer", pursuerId));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_supplier_quarry", quarryId));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_pair_count", projection.PairCount));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_pursuer_x",
            hasCandidate ? input.Value.PursuerPosition.X : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_pursuer_y",
            hasCandidate ? input.Value.PursuerPosition.Y : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_x",
            hasCandidate ? input.Value.QuarryPosition.X : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_y",
            hasCandidate ? input.Value.QuarryPosition.Y : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_initial_separation",
            FiniteOrNull(result?.InitialDistance)));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_heading_x",
            hasCandidate ? input.Value.QuarryHeading.X : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_heading_y",
            hasCandidate ? input.Value.QuarryHeading.Y : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_pursuer_move_speed",
            hasCandidate ? input.Value.PursuerMoveSpeed : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_move_speed",
            hasCandidate ? input.Value.QuarryMoveSpeed : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_destination_condition",
            destinationCondition));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_destination_allowance",
            hasCandidate ? input.Value.ContactAllowance : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_movement_turns",
            FiniteOrNull(result?.ContactTurns)));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_attack_phase_turns",
            FiniteOrNull(result?.AttackableContactTurns)));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_can_reach_this_turn",
            projection.CanReachContactThisTurn));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_unreachable_reason",
            projection.IsReachable ? "none" : projection.SelectionReason));
    }

    internal static void AddOpportunity(
        ICollection<KeyValuePair<string, string>> fields,
        string prefix,
        BattleAttackOpportunity opportunity,
        string fallbackDestinationCondition = "useful_attack")
    {
        BattleInterceptionProjection.PairInput? geometry = opportunity?.Geometry;
        bool hasGeometry = geometry.HasValue;
        int? pursuerId = opportunity == null || opportunity.PursuerSquadId == 0
            ? null
            : opportunity.PursuerSquadId;
        int? quarryId = opportunity == null || opportunity.QuarrySquadId == 0
            ? null
            : opportunity.QuarrySquadId;
        string destination = opportunity?.DestinationCondition is { Length: > 0 } named
            && named != "none"
            ? named
            : fallbackDestinationCondition;

        fields.Add(BattleDecisionTrace.Field($"{prefix}_supplier_status",
            opportunity?.IsReachable == true ? "finite" : opportunity == null ? "none" : "unreachable"));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_supplier_pursuer", pursuerId));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_supplier_quarry", quarryId));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_pursuer_x",
            hasGeometry ? geometry.Value.PursuerPosition.X : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_pursuer_y",
            hasGeometry ? geometry.Value.PursuerPosition.Y : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_x",
            hasGeometry ? geometry.Value.QuarryPosition.X : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_y",
            hasGeometry ? geometry.Value.QuarryPosition.Y : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_initial_separation",
            hasGeometry
                ? BattleInterceptionProjection.Distance(
                    geometry.Value.PursuerPosition,
                    geometry.Value.QuarryPosition)
                : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_heading_x",
            hasGeometry ? geometry.Value.QuarryHeading.X : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_heading_y",
            hasGeometry ? geometry.Value.QuarryHeading.Y : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_pursuer_move_speed",
            hasGeometry ? geometry.Value.PursuerMoveSpeed : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_quarry_move_speed",
            hasGeometry ? geometry.Value.QuarryMoveSpeed : null));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_destination_condition", destination));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_destination_allowance",
            opportunity?.DestinationRange));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_continuous_movement_turns",
            FiniteOrNull(opportunity?.MovementTurns)));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_attack_phase_turns",
            FiniteOrNull(opportunity?.ElapsedTurns)));
        fields.Add(BattleDecisionTrace.Field($"{prefix}_unreachable_reason",
            opportunity?.IsReachable == true ? "none" : opportunity?.Reason ?? "no_opportunity"));
    }

    private static float? FiniteOrNull(float? value) =>
        value.HasValue && IsFinite(value.Value) ? value : null;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
