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
    // §8.1 BV-primary metric: a rear guard must improve expected surviving Battle Value by at
    // least this margin over ordinary withdrawal (expected able survivors is now only the
    // tie-break). Replaces the former MinimumAdditionalSurvivors headcount gate. Home is here,
    // alongside the horizon, matching where the survivor gate lived. Phase 7 calibration target.
    public const float MinimumAdditionalBattleValue = 1f;

    public sealed record Projection(
        int ExpectedAbleSurvivors,
        float ExpectedSurvivingBattleValue,
        int ExpectedSquadsIntercepted,
        int ExpectedMainBodyMaskedDepartures);

    /// <param name="ProvidesSynapse">
    /// This squad projects a synapse/command aura (§8.2). If it is held as rear guard or run
    /// down, the squads depending on it lose coverage in that projection branch.
    /// </param>
    /// <param name="DependsOnSynapse">
    /// This squad relies on a friendly aura to stay steady (a covered gaunt-like squad). If every
    /// provider is lost in a branch it reverts to ordinary morale (§4.2).
    /// </param>
    /// <param name="RoutsIfSevered">
    /// Caller-supplied closed-form verdict (via <see cref="BattleMoraleEvaluator.EstimateOutcome"/>):
    /// would this squad rout if it lost its aura this turn? Deterministic and RNG-free. Only read
    /// when <paramref name="DependsOnSynapse"/> and the squad is actually severed in the branch.
    /// </param>
    /// <param name="ProvidesCommandAura">
    /// §4.3 Phase 6 — the second consumer of the aura-loss seam. This squad is an HQ projecting
    /// the command aura; losing every command provider in a branch strips the support term from
    /// its dependents.
    /// </param>
    /// <param name="DependsOnCommandAura">
    /// This squad is currently steadied by a living HQ's aura. If every command provider is lost
    /// in a branch, its support flips to the command-loss stress term (§4.3).
    /// </param>
    /// <param name="RoutsIfCommandLost">
    /// Caller-supplied closed-form verdict: would this squad rout at ordinary morale with the
    /// command-loss term applied instead of its current support? Only read when
    /// <paramref name="DependsOnCommandAura"/> and every command provider is lost in the branch.
    /// </param>
    public sealed record SquadGeometry(
        int SquadId,
        int AbleSoldiers,
        float BattleValue,
        float CurrentEnemySeparation,
        float RunSpeed,
        bool ProvidesSynapse = false,
        bool DependsOnSynapse = false,
        bool RoutsIfSevered = false,
        bool ProvidesCommandAura = false,
        bool DependsOnCommandAura = false,
        bool RoutsIfCommandLost = false);

    /// <param name="SquadEgo">
    /// Squad Ego for the §7 rear-guard predicate. A squad will hold without breaking iff its Ego
    /// clears <see cref="MoraleConstants.RearGuardEgoThreshold"/> or it stays synapse-covered for
    /// the whole hold. Default is a passing value so non-morale callers/tests are unaffected.
    /// </param>
    /// <param name="IsShaken">Shaken squads are ineligible for rear guard regardless (§6, §7).</param>
    /// <param name="WillRemainSynapseCoveredWhileHolding">
    /// §7 (amended 2026-07-16): a LIVING provider is projected to stay within radius for the whole
    /// hold. Coverage at selection time is not enough. Normally false — providers withdraw with the
    /// main body and the machinery supports a single rear-guard squad, so this arm stays latent
    /// until composite rear guards (§12); the Warriors themselves pass on Ego instead.
    /// </param>
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
        bool SavesDistinctRemainingBody = true,
        float SquadEgo = 20f,
        bool IsShaken = false,
        bool WillRemainSynapseCoveredWhileHolding = false);

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
    ///
    /// When <paramref name="modelCommandCollapse"/> is set (§8.2), the projection also prices
    /// command collapse: a squad that loses every surviving synapse/command provider in THIS
    /// branch — because its provider is held back as the rear guard, or is itself run down —
    /// reverts to ordinary morale, and if it is estimated to rout (its precomputed
    /// <see cref="SquadGeometry.RoutsIfSevered"/> / <see cref="SquadGeometry.RoutsIfCommandLost"/>
    /// verdict) it forfeits the masked-departure delay and is intercepted at the pursuit
    /// rules' higher rate. This is what charges the Warriors-hold branch for the escaping
    /// swarm's coverage lapse and stops BV-primary from sacrificing the wrong squad. The
    /// command aura is the second consumer of the same seam (§4.3, Phase 6): an HQ held as
    /// rear guard or run down strips its dependents' support, which is what protects an
    /// Imperial Captain — never worth more BV than a squad, but his loss costs the platoon.
    /// It is deterministic and RNG-free — expected value only.
    ///
    /// The propagation is capped at exactly ONE level (§8.2, locked): provider loss raises the
    /// dependents' rout verdict, which raises their interception. A dependent's projected rout
    /// never feeds back to re-sever anyone or raise a third squad's stress. Provider survival is
    /// therefore read from a single provisional pass that ignores command-collapse pricing.
    /// </summary>
    public static Projection ProjectOpenGround(
        IReadOnlyList<SquadGeometry> squads,
        float fastestPursuerSpeed,
        float oneTurnAttackReach,
        int horizonTurns = DefaultHorizonTurns,
        int? rearGuardSquadId = null,
        float rearGuardDelayTurns = 0,
        bool modelCommandCollapse = true)
    {
        if (horizonTurns < 1)
            throw new System.ArgumentOutOfRangeException(nameof(horizonTurns));

        float delay = rearGuardSquadId.HasValue
            ? System.Math.Clamp(rearGuardDelayTurns, 0, horizonTurns)
            : 0;

        bool Intercepted(SquadGeometry squad, float appliedDelay, float reach)
        {
            float separationWhenChaseBegins = squad.CurrentEnemySeparation + squad.RunSpeed * appliedDelay;
            float chaseTurns = horizonTurns - appliedDelay;
            float finalSeparation = separationWhenChaseBegins +
                                    (squad.RunSpeed - fastestPursuerSpeed) * chaseTurns;
            float minimumSeparation = fastestPursuerSpeed > squad.RunSpeed
                ? finalSeparation
                : separationWhenChaseBegins;
            return minimumSeparation <= reach;
        }

        // Level-0 pass (§8.2 one-level cap): does at least one aura provider survive this branch?
        // The rear guard is fixed by the pursuit and counts as lost; every other squad uses its
        // ordinary interception. This pass never consults command-collapse pricing, so a
        // dependent's projected rout cannot alter which providers are deemed to survive. Synapse
        // and command are evaluated as independent aura pools — the one-level cap also means no
        // cross-aura coupling (losing a Tyrant that is both synapse and HQ prices each dependency
        // through its own verdict, computed against live state for the other aura).
        bool anySurvivingProvider = modelCommandCollapse && squads.Any(provider =>
            provider.ProvidesSynapse
            && provider.SquadId != rearGuardSquadId
            && !Intercepted(provider, delay, oneTurnAttackReach));
        bool anySurvivingCommandProvider = modelCommandCollapse && squads.Any(provider =>
            provider.ProvidesCommandAura
            && provider.SquadId != rearGuardSquadId
            && !Intercepted(provider, delay, oneTurnAttackReach));

        int survivors = 0;
        float survivingBv = 0;
        int intercepted = 0;
        int masked = 0;

        foreach (SquadGeometry squad in squads.OrderBy(squad => squad.SquadId))
        {
            if (squad.SquadId == rearGuardSquadId)
            {
                intercepted++;
                continue;
            }

            // Level-1: a dependent with no surviving provider reverts to ordinary morale (synapse)
            // or takes the command-loss stress term (command aura). If the closed-form verdict is
            // that it routs, it drops out of the cover rotation, loses the rear guard's
            // masked-departure delay, and is run down at the higher interception rate.
            bool severed = modelCommandCollapse && squad.DependsOnSynapse && !anySurvivingProvider;
            bool commandLost = modelCommandCollapse
                && squad.DependsOnCommandAura
                && !anySurvivingCommandProvider;
            bool routsAfterAuraLoss = (severed && squad.RoutsIfSevered)
                || (commandLost && squad.RoutsIfCommandLost);
            float appliedDelay = routsAfterAuraLoss ? 0f : delay;
            float reach = routsAfterAuraLoss
                ? oneTurnAttackReach * MoraleConstants.RoutInterceptionReachMultiplier
                : oneTurnAttackReach;

            if (Intercepted(squad, appliedDelay, reach))
            {
                intercepted++;
                continue;
            }

            survivors += squad.AbleSoldiers;
            survivingBv += squad.BattleValue;
            if (rearGuardSquadId.HasValue && !routsAfterAuraLoss && delay >= MaskedDepartureTurnsRequired)
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

        // §8.1 / §9 (items 1 and 2 swapped 2026-07-16): greatest expected additional surviving
        // BV first, then greatest additional survivors as the tie-break — the inverse of the old
        // survivor-primary rule. The structural gates (not_exposed / safely_withdrawing) still
        // guard against sacrificing a safe squad, so BV-primary does not reintroduce that trap.
        (Candidate Candidate, CandidateEvaluation Evaluation)? selected = evaluations
            .Where(item => item.Evaluation.Eligible)
            .OrderByDescending(item => item.Evaluation.AdditionalSurvivingBattleValue)
            .ThenByDescending(item => item.Evaluation.AdditionalSurvivors)
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
                BattleDecisionTrace.Field("ego", item.Candidate.SquadEgo),
                BattleDecisionTrace.Field("shaken", item.Candidate.IsShaken),
                BattleDecisionTrace.Field("sustained_coverage", item.Candidate.WillRemainSynapseCoveredWhileHolding),
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
        // §7 rear-guard predicate: a squad that would break while holding cannot hold at all.
        else if (candidate.IsShaken)
            rejection = "shaken";
        else if (!WillNotBreakWhileHolding(candidate))
            rejection = "will_break_if_held";
        // §8.1 BV-primary gate (replaces the former survivor-improvement gate).
        else if (additionalBv < MinimumAdditionalBattleValue)
            rejection = "insufficient_bv_improvement";

        return new CandidateEvaluation(candidate.SquadId, candidate.Projection,
            additionalSurvivors, additionalBv, rejection == null,
            rejection ?? "eligible_material_improvement");
    }

    /// <summary>
    /// §7 rear-guard predicate (amended 2026-07-16). A squad will hold the line without breaking
    /// iff its Ego clears the threshold (the marine last stand and the Ego-20 Warriors) OR a
    /// living synapse provider is projected to remain within radius for the whole hold — coverage
    /// at selection time alone is not enough, because a provider that withdraws with the main body
    /// severs the "rear guard" mid-hold and produces the exact rout the gate exists to prevent.
    /// </summary>
    private static bool WillNotBreakWhileHolding(Candidate candidate) =>
        candidate.SquadEgo >= MoraleConstants.RearGuardEgoThreshold
        || candidate.WillRemainSynapseCoveredWhileHolding;
}
