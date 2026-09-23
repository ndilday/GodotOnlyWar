using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles;

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
    /// Tolerance retained for hypothetical reach and legacy escape formulas. Contact maintenance
    /// no longer treats a declared-speed advantage as pursuit evidence; it uses observed geometric
    /// progress from <see cref="PursuitProgressPolicy"/> instead.
    /// </summary>
    public const float PursuitSpeedAdvantageTolerance = 0.1f;

    /// <summary>
    /// Extra reach beyond a pursuer's move allowance that still lets it reach contact this turn,
    /// matching the Run-to-contact term in the resolver's one-turn reach.
    ///
    /// <para>This is a PLANNING reach, not the engaged-soldier test. Whether a soldier may actually
    /// strike is grid adjacency plus a combat-effective enemy — see
    /// MeleeActionBuilder.HasCombatEffectiveAdjacentEnemy. Gating a strike on this constant instead
    /// let a soldier standing 1.0 from a downed man trip the melee builder's invariant.</para>
    /// </summary>
    public const float MeleeContactAllowance = 1.0f;

    /// <summary>
    /// Whether a pursuer can reach contact THIS turn, measured against the gap it can actually take
    /// out of the separation rather than the raw distance it can travel.
    ///
    /// <para>Reaching contact is no longer the same event as landing a blow. Attacks resolve from
    /// turn-start geometry, so a pursuer that closes this turn strikes at the top of the next one
    /// (PRD §4.14, TDD §6.6). This pair-local observed-reach predicate remains for
    /// <see cref="PursuitPairActivity"/>'s current-turn contact evidence. Force-level
    /// hypothetical pressing uses <see cref="BattleInterceptionProjection"/> so it can retain
    /// concrete pair geometry and an explicit unreachable result.</para>
    ///
    /// The quarry moves in the same turn, so the distance that matters is the NET closing rate.
    /// Comparing separation to the pursuer's move alone made a stern chase at matched speed read
    /// as permanently one move from contact: separation settles at exactly the pursuer's move
    /// (it gains only the sliver by which it is faster), so "I can reach melee this turn" stayed
    /// true forever while contact never happened. The contact lifecycle's stalled_pursuit break
    /// remains gated on this test; the force-level pursuit planner has a separate counterfactual
    /// projection, so an equal-speed stern chase can be classified as unreachable without
    /// allowing an unrelated force extreme to decide the result. Observed 2026-08-04 (Xibarrus
    /// Theta): 6.001 vs 6.001, separation pinned at 6, ~997 turns with nothing landed.
    /// </summary>
    public static bool CanReachContactThisTurn(
        float separation,
        float pursuerSpeed,
        float quarrySpeed) =>
        separation <= Math.Max(0, pursuerSpeed - quarrySpeed) + MeleeContactAllowance;

    public sealed record Input(
        int Turn,
        bool IsFirstSide,
        int ActivePursuerCount,
        bool AllPursuersBreakOff,
        bool EnemyAlsoWithdrawing,
        IReadOnlyCollection<PursuitPairActivity> PursuitPairs,
        bool RearGuardActive,
        float MaskedDepartureProgress,
        float WithdrawingSquadRunAllowance,
        bool HasImmediateDisengagementCapability = false);

    public sealed record Result(ContactBreakResult Decision, string Reason, BattleDecisionTrace Trace);

    public static float RequiredMaskedDepartureDistance(float runAllowance) =>
        runAllowance * MaskedDepartureRunAllowanceMultiplier;

    public static Result Evaluate(Input input)
    {
        IReadOnlyCollection<PursuitPairActivity> pairs = input.PursuitPairs ?? [];
        float required = RequiredMaskedDepartureDistance(input.WithdrawingSquadRunAllowance);
        int observedProgressPairs = 0;
        int startupGracePairs = 0;
        int attackedRecentlyPairs = 0;
        int viableFireCyclePairs = 0;
        int reachThisTurnPairs = 0;
        int quarryEliminatedPairs = 0;
        int slowProgressPairs = 0;
        foreach (PursuitPairActivity pair in pairs)
        {
            // Only progress that reaches the pair's effect range within the chase horizon keeps
            // contact; see PursuitPairActivity.HasTimelyClosingProgress.
            if (pair.HasTimelyClosingProgress) observedProgressPairs++;
            else if (pair.HasObservedClosingProgress) slowProgressPairs++;
            if (pair.HasStartupGrace) startupGracePairs++;
            if (pair.PairAttackedRecently) attackedRecentlyPairs++;
            if (pair.HasQualifyingFireCycleProgress) viableFireCyclePairs++;
            if (pair.CanReachContactThisTurn) reachThisTurnPairs++;
            if (pair.QuarryEliminated) quarryEliminatedPairs++;
        }

        bool pairHasActiveEvidence = observedProgressPairs > 0
            || startupGracePairs > 0
            || attackedRecentlyPairs > 0
            || viableFireCyclePairs > 0
            || quarryEliminatedPairs > 0;
        bool pairCanReachContactThisTurn = reachThisTurnPairs > 0;
        ContactBreakResult decision;
        string reason;

        if (input.HasImmediateDisengagementCapability)
            (decision, reason) = (ContactBreakResult.SquadDisengages, "special_capability");
        else if (input.ActivePursuerCount == 0 || input.AllPursuersBreakOff)
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "pursuer_stops");
        else if (input.EnemyAlsoWithdrawing)
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "mutual_withdrawal");
        // Contact is maintained only by evidence from an assigned pair. In particular, a
        // theoretical force-wide shot is not evidence: a stationary pursuer has to execute an
        // AimAction this round and retain a viable commitment to this pair's quarry. Same-turn
        // reach remains a valid collision exception. Live pair activities supply the corrected
        // pair projection; the scalar helper remains the direct-call fallback for this predicate.
        else if (!pairHasActiveEvidence && !pairCanReachContactThisTurn)
            (decision, reason) = (ContactBreakResult.OrganizedForceDisengages, "stalled_pursuit");
        else if (input.RearGuardActive && input.MaskedDepartureProgress >= required)
            (decision, reason) = (ContactBreakResult.SquadDisengages, "masked_departure");
        else
            (decision, reason) = (ContactBreakResult.RemainInContact, "pursuit_can_maintain_contact");

        string maintenanceEvidence = string.Join(
            "+",
            new[]
            {
                observedProgressPairs > 0 ? "progress" : null,
                startupGracePairs > 0 ? "startup" : null,
                attackedRecentlyPairs > 0 ? "attack" : null,
                viableFireCyclePairs > 0 ? "fire" : null,
                pairCanReachContactThisTurn ? "reach" : null,
                quarryEliminatedPairs > 0 ? "eliminated" : null
            }.Where(code => code != null));
        if (maintenanceEvidence.Length == 0) maintenanceEvidence = "none";

        string pairReasons = string.Join(
            "|",
            pairs
                .OrderBy(pair => pair.PursuerSquadId)
                .ThenBy(pair => pair.QuarrySquadId)
                .Select(pair =>
                    $"{pair.PursuerSquadId}>{pair.QuarrySquadId}:{pair.EvidenceReasonCode}"));
        if (pairReasons.Length == 0) pairReasons = "none";

        BattleDecisionTrace trace = new("CONTACT_EVAL", new List<KeyValuePair<string, string>>
        {
            BattleDecisionTrace.Field("turn", input.Turn),
            BattleDecisionTrace.Field("side", input.IsFirstSide ? "first" : "second"),
            BattleDecisionTrace.Field("active_pursuers", input.ActivePursuerCount),
            BattleDecisionTrace.Field("pursuit_pairs", pairs.Count),
            BattleDecisionTrace.Field("observed_progress_pairs", observedProgressPairs),
            // Retain the old field name as a trace-compatibility alias, but its value is now
            // observed geometry rather than declared-speed advantage.
            BattleDecisionTrace.Field("positive_closing_pairs", observedProgressPairs),
            BattleDecisionTrace.Field("slow_progress_pairs", slowProgressPairs),
            BattleDecisionTrace.Field("startup_grace_pairs", startupGracePairs),
            BattleDecisionTrace.Field("attacked_recently_pairs", attackedRecentlyPairs),
            BattleDecisionTrace.Field("viable_fire_cycle_pairs", viableFireCyclePairs),
            BattleDecisionTrace.Field("quarry_eliminated_pairs", quarryEliminatedPairs),
            BattleDecisionTrace.Field("maintenance_evidence", maintenanceEvidence),
            BattleDecisionTrace.Field("pair_reasons", pairReasons),
            BattleDecisionTrace.Field("pair_active", pairHasActiveEvidence),
            BattleDecisionTrace.Field("pair_reach_this_turn", pairCanReachContactThisTurn),
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
