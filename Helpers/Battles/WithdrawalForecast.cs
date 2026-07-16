using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles;

/// <summary>
/// Deterministically compares an ordinary withdrawal projection with caller-produced,
/// geometry-only rear-guard projections. It never estimates combat by rolling RNG.
/// </summary>
public static class WithdrawalForecast
{
    public const int DefaultHorizonTurns = 3;
    public const int MinimumAdditionalSurvivors = 1;

    public sealed record Projection(
        int ExpectedAbleSurvivors,
        float ExpectedSurvivingBattleValue,
        int ExpectedSquadsIntercepted,
        int ExpectedMainBodyMaskedDepartures);

    public sealed record SquadGeometry(
        int SquadId,
        int AbleSoldiers,
        float BattleValue,
        float CurrentEnemySeparation,
        float RunSpeed);

    public sealed record Candidate(
        int SquadId,
        bool IsClosestSquad,
        bool IsEngaged,
        bool WithinNextTurnInterceptReach,
        bool IsSafelyWithdrawing,
        int OtherSquadsRemaining,
        float NearestEnemyDistance,
        float DelayPotential,
        Projection Projection,
        bool SavesDistinctRemainingBody = true);

    public sealed record Input(
        int Turn,
        bool IsFirstSide,
        Projection Baseline,
        IReadOnlyList<Candidate> Candidates,
        int HorizonTurns = DefaultHorizonTurns,
        bool IsRepeatSacrifice = false);

    public sealed record CandidateEvaluation(
        int SquadId,
        Projection Projection,
        int AdditionalSurvivors,
        float AdditionalSurvivingBattleValue,
        bool Eligible,
        string Reason);

    public sealed record Result(
        int? SelectedSquadId,
        IReadOnlyList<CandidateEvaluation> CandidateEvaluations,
        BattleDecisionTrace Trace,
        IReadOnlyList<BattleDecisionTrace> CandidateTraces);

    /// <summary>
    /// Produces a conservative open-ground projection. A rear guard is assumed lost, and
    /// fixes pursuit on itself for its delay window; combat outcomes are never invented.
    /// </summary>
    public static Projection ProjectOpenGround(
        IReadOnlyList<SquadGeometry> squads,
        float fastestPursuerSpeed,
        float oneTurnAttackReach,
        int horizonTurns = DefaultHorizonTurns,
        int? rearGuardSquadId = null,
        float rearGuardDelayTurns = 0)
    {
        if (horizonTurns < 1)
            throw new System.ArgumentOutOfRangeException(nameof(horizonTurns));

        int survivors = 0;
        float survivingBv = 0;
        int intercepted = 0;
        int masked = 0;
        float delay = rearGuardSquadId.HasValue
            ? System.Math.Clamp(rearGuardDelayTurns, 0, horizonTurns)
            : 0;

        foreach (SquadGeometry squad in squads.OrderBy(squad => squad.SquadId))
        {
            if (squad.SquadId == rearGuardSquadId)
            {
                intercepted++;
                continue;
            }

            float separationWhenChaseBegins = squad.CurrentEnemySeparation + squad.RunSpeed * delay;
            float chaseTurns = horizonTurns - delay;
            float finalSeparation = separationWhenChaseBegins +
                                    (squad.RunSpeed - fastestPursuerSpeed) * chaseTurns;
            float minimumSeparation = fastestPursuerSpeed > squad.RunSpeed
                ? finalSeparation
                : separationWhenChaseBegins;
            bool isIntercepted = minimumSeparation <= oneTurnAttackReach;

            if (isIntercepted)
            {
                intercepted++;
                continue;
            }

            survivors += squad.AbleSoldiers;
            survivingBv += squad.BattleValue;
            if (rearGuardSquadId.HasValue && delay >= MaskedDepartureTurnsRequired)
                masked++;
        }

        return new Projection(survivors, survivingBv, intercepted, masked);
    }

    public const float MaskedDepartureTurnsRequired = 1.0f;

    public static Result Evaluate(Input input)
    {
        List<(Candidate Candidate, CandidateEvaluation Evaluation)> evaluations = input.Candidates
            .OrderBy(candidate => candidate.SquadId)
            .Select(candidate => (candidate, EvaluateCandidate(input, candidate)))
            .ToList();

        (Candidate Candidate, CandidateEvaluation Evaluation)? selected = evaluations
            .Where(item => item.Evaluation.Eligible)
            .OrderByDescending(item => item.Evaluation.AdditionalSurvivors)
            .ThenByDescending(item => item.Evaluation.AdditionalSurvivingBattleValue)
            .ThenByDescending(item => item.Candidate.IsEngaged)
            .ThenBy(item => item.Candidate.NearestEnemyDistance)
            .ThenByDescending(item => item.Candidate.DelayPotential)
            .ThenBy(item => item.Candidate.SquadId)
            .Cast<(Candidate Candidate, CandidateEvaluation Evaluation)?>()
            .FirstOrDefault();

        int? selectedId = selected?.Candidate.SquadId;
        int survivorImprovement = selected?.Evaluation.AdditionalSurvivors ?? 0;
        float bvImprovement = selected?.Evaluation.AdditionalSurvivingBattleValue ?? 0;
        string reason = selectedId.HasValue ? "material_improvement" : "no_material_candidate";
        BattleDecisionTrace trace = new("REARGUARD_EVAL", new List<KeyValuePair<string, string>>
        {
            BattleDecisionTrace.Field("turn", input.Turn),
            BattleDecisionTrace.Field("side", input.IsFirstSide ? "first" : "second"),
            BattleDecisionTrace.Field("horizon", input.HorizonTurns),
            BattleDecisionTrace.Field("baseline_survivors", input.Baseline.ExpectedAbleSurvivors),
            BattleDecisionTrace.Field("baseline_bv", input.Baseline.ExpectedSurvivingBattleValue),
            BattleDecisionTrace.Field("baseline_intercepted", input.Baseline.ExpectedSquadsIntercepted),
            BattleDecisionTrace.Field("baseline_masked", input.Baseline.ExpectedMainBodyMaskedDepartures),
            BattleDecisionTrace.Field("selected_squad", selectedId),
            BattleDecisionTrace.Field("survivor_improvement", survivorImprovement),
            BattleDecisionTrace.Field("bv_improvement", bvImprovement),
            BattleDecisionTrace.Field("reason", reason)
        });
        List<BattleDecisionTrace> candidateTraces = evaluations.Select(item =>
            new BattleDecisionTrace("REARGUARD_EVAL_CANDIDATE", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("squad", item.Evaluation.SquadId),
                BattleDecisionTrace.Field("survivors", item.Evaluation.Projection.ExpectedAbleSurvivors),
                BattleDecisionTrace.Field("bv", item.Evaluation.Projection.ExpectedSurvivingBattleValue),
                BattleDecisionTrace.Field("intercepted", item.Evaluation.Projection.ExpectedSquadsIntercepted),
                BattleDecisionTrace.Field("masked", item.Evaluation.Projection.ExpectedMainBodyMaskedDepartures),
                BattleDecisionTrace.Field("survivor_improvement", item.Evaluation.AdditionalSurvivors),
                BattleDecisionTrace.Field("bv_improvement", item.Evaluation.AdditionalSurvivingBattleValue),
                BattleDecisionTrace.Field("eligible", item.Evaluation.Eligible),
                BattleDecisionTrace.Field("reason", item.Evaluation.Reason)
            })).ToList();
        BattleLog.Write(trace.Render());
        foreach (BattleDecisionTrace candidateTrace in candidateTraces)
            BattleLog.Write(candidateTrace.Render());
        return new Result(selectedId, evaluations.Select(item => item.Evaluation).ToList(), trace, candidateTraces);
    }

    private static CandidateEvaluation EvaluateCandidate(Input input, Candidate candidate)
    {
        int additionalSurvivors = candidate.Projection.ExpectedAbleSurvivors - input.Baseline.ExpectedAbleSurvivors;
        float additionalBv = candidate.Projection.ExpectedSurvivingBattleValue - input.Baseline.ExpectedSurvivingBattleValue;
        string rejection = null;

        if (!candidate.IsClosestSquad && !candidate.IsEngaged && !candidate.WithinNextTurnInterceptReach)
            rejection = "not_exposed";
        else if (candidate.IsSafelyWithdrawing)
            rejection = "safely_withdrawing";
        else if (candidate.OtherSquadsRemaining < 1)
            rejection = "no_other_squad_to_save";
        else if (input.IsRepeatSacrifice && !candidate.SavesDistinctRemainingBody)
            rejection = "repeat_saves_no_distinct_body";
        else if (additionalSurvivors < MinimumAdditionalSurvivors)
            rejection = "insufficient_survivor_improvement";

        return new CandidateEvaluation(candidate.SquadId, candidate.Projection,
            additionalSurvivors, additionalBv, rejection == null,
            rejection ?? "eligible_material_improvement");
    }
}
