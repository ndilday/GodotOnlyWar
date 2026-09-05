using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles;

/// <summary>
/// Determines when an unpursued withdrawing squad is far enough outside the active engagement to
/// be represented as disengaged. This is an open-ground contact abstraction, not a map boundary.
/// </summary>
public static class BattleEscapeRules
{
    // Keep a squad in the simulation while an enemy could still reconsider and attack it within
    // the engagement planner's existing two-turn lookahead.
    public const float RetargetingHorizonTurns = BattleSquadPlanner.EngagementLookaheadHorizon;

    public sealed record Threat(
        int PursuerSquadId,
        float Separation,
        float UsefulAttackRange,
        float PursuerMoveSpeed,
        float WithdrawalMoveSpeed);

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
        BattleDecisionTrace Trace);

    public static Result Evaluate(Input input)
    {
        List<Threat> threats = input.Threats?.ToList() ?? [];
        float earliest = threats
            .Select(ProjectInterceptTurns)
            .DefaultIfEmpty(float.PositiveInfinity)
            .Min();
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

        BattleDecisionTrace trace = new("ESCAPE_EVAL", new List<KeyValuePair<string, string>>
        {
            BattleDecisionTrace.Field("turn", input.Turn),
            BattleDecisionTrace.Field("side", input.IsFirstSide ? "first" : "second"),
            BattleDecisionTrace.Field("squad", input.WithdrawingSquadId),
            BattleDecisionTrace.Field("pursued", input.IsPursued),
            BattleDecisionTrace.Field("threats", threats.Count),
            BattleDecisionTrace.Field("intercept_turns",
                float.IsPositiveInfinity(earliest) ? "never" : earliest),
            BattleDecisionTrace.Field("decision", escapes ? "Disengage" : "Remain"),
            BattleDecisionTrace.Field("reason", reason)
        });
        BattleLog.Write(trace.Render());
        return new Result(escapes, earliest, reason, trace);
    }

    private static float ProjectInterceptTurns(Threat threat)
    {
        float remaining = Math.Max(0, threat.Separation - threat.UsefulAttackRange);
        if (remaining <= 0) return 0;
        float relativeClosingSpeed = threat.PursuerMoveSpeed - threat.WithdrawalMoveSpeed;
        return relativeClosingSpeed <= BattleContactRules.PursuitSpeedAdvantageTolerance
            ? float.PositiveInfinity
            : remaining / relativeClosingSpeed;
    }
}
