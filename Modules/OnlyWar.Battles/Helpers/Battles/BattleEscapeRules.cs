using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles;

/// <summary>
/// Determines when an unpursued withdrawing squad is far enough outside the active engagement to
/// be represented as disengaged. This is an open-ground contact abstraction, not a map boundary.
/// </summary>
public static class BattleEscapeRules
{
    // Keep a squad in the simulation while an enemy could still reconsider and attack it within
    // a two-turn retargeting horizon. The constant is inherited from the engagement planner's
    // former two-ply policy rollout; the rollout is gone, but the horizon is still this rule's.
    public const float RetargetingHorizonTurns = BattleSquadPlanner.EngagementLookaheadHorizon;

    public sealed record Threat(
        int PursuerSquadId,
        float Separation,
        float UsefulAttackRange,
        float PursuerMoveSpeed,
        float WithdrawalMoveSpeed,
        BattleAttackOpportunity AttackOpportunity = null);

    public sealed record Input(
        int Turn,
        bool IsFirstSide,
        int WithdrawingSquadId,
        bool IsPursued,
        IReadOnlyCollection<Threat> Threats);

    public sealed record Result(
        bool Escapes,
        float EarliestInterceptTurns,
        string Reason,
        BattleDecisionTrace Trace,
        int? EarliestPursuerSquadId = null,
        int? EarliestQuarrySquadId = null,
        BattleAttackMode? EarliestAttackMode = null,
        BattleAttackPreparation? EarliestPreparation = null,
        bool? EarliestRequiresMovement = null);

    public static Result Evaluate(Input input)
    {
        List<Threat> threats = input.Threats?.ToList() ?? [];
        List<(Threat Threat, float Turns)> projections = threats
            .Select(threat => (threat, ProjectInterceptTurns(threat)))
            .ToList();
        List<(Threat Threat, float Turns)> orderedProjections = projections
            .Where(projection => !float.IsNaN(projection.Turns))
            .OrderBy(projection => projection.Turns)
            .ThenBy(projection => projection.Threat.PursuerSquadId)
            .ThenBy(projection => projection.Threat.AttackOpportunity?.QuarrySquadId ?? int.MaxValue)
            .ToList();
        (Threat Threat, float Turns)? earliestProjection = orderedProjections.Count > 0
            ? orderedProjections[0]
            : null;
        float earliest = earliestProjection?.Turns
            ?? float.PositiveInfinity;
        Threat selectedThreat = earliestProjection?.Threat;
        BattleAttackOpportunity selectedOpportunity = selectedThreat?.AttackOpportunity;
        bool usesLegacyScalarEstimate = selectedThreat != null && selectedOpportunity == null;
        bool escapes;
        string reason;
        if (input.IsPursued)
        {
            (escapes, reason) = (false, "actively_pursued");
        }
        else if (earliest <= 0)
        {
            (escapes, reason) = (false, "inside_attack_range");
        }
        else if (earliest <= RetargetingHorizonTurns)
        {
            (escapes, reason) = (false, "enemy_can_retarget");
        }
        else if (float.IsPositiveInfinity(earliest))
        {
            (escapes, reason) = (true, "no_possible_intercept");
        }
        else
        {
            (escapes, reason) = (true, "beyond_retarget_horizon");
        }

        List<KeyValuePair<string, string>> fields = new()
        {
            BattleDecisionTrace.Field("turn", input.Turn),
            BattleDecisionTrace.Field("side", input.IsFirstSide ? "first" : "second"),
            BattleDecisionTrace.Field("squad", input.WithdrawingSquadId),
            BattleDecisionTrace.Field("pursued", input.IsPursued),
            BattleDecisionTrace.Field("threats", threats.Count),
            BattleDecisionTrace.Field("estimate_kind", selectedOpportunity != null
                ? "hypothetical_useful_attack_opportunity"
                : "legacy_scalar_diagnostic"),
            BattleDecisionTrace.Field("intercept_turns",
                float.IsPositiveInfinity(earliest) ? "never" : earliest),
            BattleDecisionTrace.Field("attack_opportunity_turns",
                float.IsPositiveInfinity(earliest) ? "never" : earliest),
            BattleDecisionTrace.Field("intercept_turns_scope", selectedOpportunity != null
                ? "counterfactual_useful_attack"
                : "legacy_speed_ratio_diagnostic"),
            BattleDecisionTrace.Field(
                "legacy_scalar_estimate_diagnostic_only", usesLegacyScalarEstimate),
            BattleDecisionTrace.Field("decision", escapes ? "Disengage" : "Remain"),
            BattleDecisionTrace.Field("reason", reason)
        };
        if (selectedOpportunity != null)
        {
            fields.Add(BattleDecisionTrace.Field("scope", "pair_opportunity"));
            fields.Add(BattleDecisionTrace.Field(
                "pursuer", selectedOpportunity.PursuerSquadId));
            fields.Add(BattleDecisionTrace.Field(
                "quarry", selectedOpportunity.QuarrySquadId));
            fields.Add(BattleDecisionTrace.Field("attack_mode", selectedOpportunity.Mode));
            fields.Add(BattleDecisionTrace.Field("attack_kind", selectedOpportunity.Kind));
            fields.Add(BattleDecisionTrace.Field("shooter", selectedOpportunity.ShooterId));
            fields.Add(BattleDecisionTrace.Field("target", selectedOpportunity.TargetId));
            fields.Add(BattleDecisionTrace.Field(
                "weapon_template", selectedOpportunity.WeaponTemplateId));
            fields.Add(BattleDecisionTrace.Field("preparation", selectedOpportunity.Preparation));
            fields.Add(BattleDecisionTrace.Field(
                "requires_movement", selectedOpportunity.RequiresMovement));
            fields.Add(BattleDecisionTrace.Field(
                "movement_turns", selectedOpportunity.MovementTurns));
            fields.Add(BattleDecisionTrace.Field("opportunity_reason", selectedOpportunity.Reason));
            BattleProjectionDiagnosticTrace.AddOpportunity(
                fields,
                "selected",
                selectedOpportunity,
                "useful_attack");
        }
        BattleDecisionTrace trace = new("ESCAPE_EVAL", fields);
        BattleLog.Write(trace.Render());
        return new Result(
            escapes,
            earliest,
            reason,
            trace,
            selectedOpportunity?.PursuerSquadId,
            selectedOpportunity?.QuarrySquadId,
            selectedOpportunity?.Mode,
            selectedOpportunity?.Preparation,
            selectedOpportunity?.RequiresMovement);
    }

    private static float ProjectInterceptTurns(Threat threat)
    {
        if (threat.AttackOpportunity != null)
        {
            return threat.AttackOpportunity.IsReachable
                ? threat.AttackOpportunity.ElapsedTurns
                : float.PositiveInfinity;
        }

        float remaining = Math.Max(0, threat.Separation - threat.UsefulAttackRange);
        if (remaining <= 0) return 0;
        float relativeClosingSpeed = threat.PursuerMoveSpeed - threat.WithdrawalMoveSpeed;
        return relativeClosingSpeed <= BattleContactRules.PursuitSpeedAdvantageTolerance
            ? float.PositiveInfinity
            : remaining / relativeClosingSpeed;
    }
}
