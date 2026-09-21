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
    /// How much faster than its assigned quarry a pursuer must be before it counts as able to
    /// close the gap at all. A hair of extra speed is not a chase: at a tenth of a hex per turn
    /// the pursuer needs hundreds of turns to make up a single hex, which reads as a hung battle
    /// rather than a pursuit. Shared with <see cref="BattlePursuitPlanner"/> so the posture
    /// decision and the contact break agree on what "cannot close" means.
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
    /// (PRD §4.14, TDD §6.6). Both escape hatches below want the former — is this pursuit still
    /// going anywhere — so the test and its call sites are unchanged; only the name moved, from the
    /// older CanReachMeleeThisTurn.</para>
    ///
    /// The quarry moves in the same turn, so the distance that matters is the NET closing rate.
    /// Comparing separation to the pursuer's move alone made a stern chase at matched speed read
    /// as permanently one move from contact: separation settles at exactly the pursuer's move
    /// (it gains only the sliver by which it is faster), so "I can reach melee this turn" stayed
    /// true forever while contact never happened. Both escape hatches that end an unwinnable
    /// chase — <see cref="BattlePursuitPlanner"/>'s cannot-close override and the stalled_pursuit
    /// break below — are gated on this test, so the fixed point disabled both and the battle ran
    /// to the resolver's turn cap. Observed 2026-08-04 (Xibarrus Theta): 6.001 vs 6.001,
    /// separation pinned at 6, ~997 turns with nothing landed.
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
        int positiveClosingPairs = 0;
        int attackedRecentlyPairs = 0;
        int viableFireCyclePairs = 0;
        int reachThisTurnPairs = 0;
        foreach (PursuitPairActivity pair in pairs)
        {
            if (pair.HasMeaningfulPositiveClosingSpeed) positiveClosingPairs++;
            if (pair.PairAttackedRecently) attackedRecentlyPairs++;
            if (pair.HasQualifyingFireCycleProgress) viableFireCyclePairs++;
            if (pair.CanReachContactThisTurn) reachThisTurnPairs++;
        }

        bool pairHasActiveEvidence = positiveClosingPairs > 0
            || attackedRecentlyPairs > 0
            || viableFireCyclePairs > 0;
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
        // reach remains a valid collision exception, evaluated with the same pair-local speeds.
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
                positiveClosingPairs > 0 ? "close" : null,
                attackedRecentlyPairs > 0 ? "attack" : null,
                viableFireCyclePairs > 0 ? "fire" : null,
                pairCanReachContactThisTurn ? "reach" : null
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
            BattleDecisionTrace.Field("positive_closing_pairs", positiveClosingPairs),
            BattleDecisionTrace.Field("attacked_recently_pairs", attackedRecentlyPairs),
            BattleDecisionTrace.Field("viable_fire_cycle_pairs", viableFireCyclePairs),
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
