using System.Collections.Generic;
using OnlyWar.Battles;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleContactRulesTests
{
    private static PursuitPairActivity Pair(
        int pursuerId = 11,
        int quarryId = 22,
        float separation = 15,
        float pursuerSpeed = 9,
        float quarrySpeed = 7,
        bool pairAttackedRecently = false,
        bool fireCycleProgressedThisTurn = false,
        bool fireCommitmentRemainsViable = false,
        bool hasObservedClosingProgress = false) =>
        new(
            pursuerId,
            quarryId,
            separation,
            pursuerSpeed,
            quarrySpeed,
            pairAttackedRecently,
            fireCycleProgressedThisTurn,
            fireCommitmentRemainsViable,
            HasObservedClosingProgress: hasObservedClosingProgress);

    private static BattleContactRules.Input Input(
        IReadOnlyCollection<PursuitPairActivity> pairs = null,
        bool rearGuardActive = false,
        float maskedDepartureProgress = 0,
        float withdrawingSquadRunAllowance = 7,
        bool hasImmediateDisengagementCapability = false,
        int activePursuerCount = 2,
        bool allPursuersBreakOff = false,
        bool enemyAlsoWithdrawing = false) =>
        new(
            Turn: 7,
            IsFirstSide: false,
            ActivePursuerCount: activePursuerCount,
            AllPursuersBreakOff: allPursuersBreakOff,
            EnemyAlsoWithdrawing: enemyAlsoWithdrawing,
            PursuitPairs: pairs ?? new[] { Pair() },
            RearGuardActive: rearGuardActive,
            MaskedDepartureProgress: maskedDepartureProgress,
            WithdrawingSquadRunAllowance: withdrawingSquadRunAllowance,
            HasImmediateDisengagementCapability: hasImmediateDisengagementCapability);

    [Fact]
    public void SlowerWithdrawal_CannotEscapeActiveFasterPursuit()
    {
        Assert.Equal(
            ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(Input([Pair(hasObservedClosingProgress: true)])).Decision);
    }

    // Grist Nine Epsilon, 2026-09-22: 16 pursuers gained about half a cell a turn on routing
    // orks 445 cells away, never fired, and held contact to the 1000-turn cap on that progress
    // alone. Progress now counts only if it brings the pair to where it can act within the chase
    // horizon.
    [Fact]
    public void ProgressTooSlowToReachEffectRangeWithinTheChaseHorizon_DoesNotHoldContact()
    {
        PursuitPairActivity crawling = new(
            11, 22, CurrentSeparation: 445, 6, 5,
            PairAttackedRecently: false,
            FireCycleProgressedThisTurn: false,
            FireCommitmentRemainsViable: false,
            ObservedSeparationGain: 2,
            HasObservedClosingProgress: true,
            ProgressHistorySamples: 4,
            EffectSeparation: 1);

        BattleContactRules.Result result = BattleContactRules.Evaluate(Input([crawling]));

        Assert.False(crawling.HasTimelyClosingProgress);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
        Assert.Contains("slow_progress_pairs=1", result.Trace.Render());
        Assert.Contains("11>22:slow_progress", result.Trace.Render());
    }

    [Fact]
    public void ProgressThatReachesUsefulFireRangeWithinTheChaseHorizon_HoldsContact()
    {
        // 40 cells outside a 400-cell useful fire range, closing 2 a turn: 20 turns.
        PursuitPairActivity closing = new(
            11, 22, CurrentSeparation: 440, 7, 5,
            PairAttackedRecently: false,
            FireCycleProgressedThisTurn: false,
            FireCommitmentRemainsViable: false,
            ObservedSeparationGain: 8,
            HasObservedClosingProgress: true,
            ProgressHistorySamples: 4,
            EffectSeparation: 400);

        Assert.Equal(20f, closing.TurnsToEffect, 3);
        Assert.True(closing.HasTimelyClosingProgress);
        Assert.Equal(
            ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(Input([closing])).Decision);
    }

    [Fact]
    public void EqualSpeedSilentPairWithOnlyTheoreticalShot_Disengages()
    {
        PursuitPairActivity silent = Pair(pursuerSpeed: 7, quarrySpeed: 7);

        BattleContactRules.Result result = BattleContactRules.Evaluate(Input([silent]));

        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void PairWithAdvancingViableAim_RemainsInContact()
    {
        PursuitPairActivity aiming = Pair(
            pursuerSpeed: 7,
            quarrySpeed: 7,
            fireCycleProgressedThisTurn: true,
            fireCommitmentRemainsViable: true);

        Assert.Equal(
            ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(Input([aiming])).Decision);
    }

    [Fact]
    public void RetainedButNonAdvancingAim_DoesNotPreserveContact()
    {
        PursuitPairActivity retainedAim = Pair(
            pursuerSpeed: 7,
            quarrySpeed: 7,
            fireCycleProgressedThisTurn: false,
            fireCommitmentRemainsViable: true);

        Assert.Equal(
            ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(Input([retainedAim])).Decision);
    }

    [Fact]
    public void FasterAssignedPursuer_PreservesContact()
    {
        PursuitPairActivity faster = Pair(
            pursuerSpeed: 8,
            quarrySpeed: 7,
            hasObservedClosingProgress: true);

        Assert.Equal(
            ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(Input([faster])).Decision);
    }

    [Fact]
    public void AttackAgainstAssignedQuarry_PreservesContact()
    {
        PursuitPairActivity attack = Pair(
            pursuerSpeed: 7,
            quarrySpeed: 7,
            pairAttackedRecently: true);

        Assert.Equal(
            ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(Input([attack])).Decision);
    }

    [Fact]
    public void UnrelatedAttackOrAim_DoesNotQualifyAssignedPair()
    {
        // The action metrics must not copy evidence from another target into this assigned pair.
        // The contact rule sees only the pair-local facts built by BattleWithdrawalService.
        PursuitPairActivity assignedPair = Pair(
            pursuerSpeed: 7,
            quarrySpeed: 7,
            pairAttackedRecently: false,
            fireCycleProgressedThisTurn: false,
            fireCommitmentRemainsViable: false);

        Assert.Equal(
            ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(Input([assignedPair])).Decision);
    }

    [Fact]
    public void FastPursuerAndSlowQuarryFromDifferentAssignments_CannotBeCombined()
    {
        // Each actual assignment is equal-speed. A force-wide max pursuer speed plus min quarry
        // speed would invent a positive closing rate of four for this input.
        PursuitPairActivity fastPair = Pair(
            pursuerId: 11,
            quarryId: 22,
            pursuerSpeed: 10,
            quarrySpeed: 10);
        PursuitPairActivity slowPair = Pair(
            pursuerId: 33,
            quarryId: 44,
            pursuerSpeed: 6,
            quarrySpeed: 6);

        Assert.Equal(
            ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(Input(new[] { fastPair, slowPair })).Decision);
    }

    [Fact]
    public void RearGuardMasksOnlyAfterFullRunAllowanceWhileActive()
    {
        var almost = Input(
            [Pair(hasObservedClosingProgress: true)],
            rearGuardActive: true,
            maskedDepartureProgress: 6.99f);
        var enough = almost with { MaskedDepartureProgress = 7 };
        var inactive = enough with { RearGuardActive = false };

        Assert.Equal(ContactBreakResult.RemainInContact, BattleContactRules.Evaluate(almost).Decision);
        Assert.Equal(ContactBreakResult.SquadDisengages, BattleContactRules.Evaluate(enough).Decision);
        Assert.Equal(ContactBreakResult.RemainInContact, BattleContactRules.Evaluate(inactive).Decision);
    }

    [Fact]
    public void SpecialCapability_ImmediatelyDisengagesSquad()
    {
        var input = Input(hasImmediateDisengagementCapability: true);

        Assert.Equal(
            ContactBreakResult.SquadDisengages,
            BattleContactRules.Evaluate(input).Decision);
    }

    [Fact]
    public void DeclaredSpeedAdvantage_DoesNotPreserveContact()
    {
        var withinTolerance = Input([Pair(pursuerSpeed: 7.1f, quarrySpeed: 7)]);
        var beyondTolerance = Input([Pair(pursuerSpeed: 7.2f, quarrySpeed: 7)]);

        Assert.Equal(
            ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(withinTolerance).Decision);
        Assert.Equal(
            ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(beyondTolerance).Decision);
    }

    [Theory]
    // Standing quarry: the pursuer keeps its whole move, so reach is move + allowance.
    [InlineData(6f, 0f, 7f, true)]
    [InlineData(6f, 0f, 7.01f, false)]
    // Genuinely faster pursuer: only the two-per-turn it actually gains counts.
    [InlineData(8f, 6f, 3f, true)]
    [InlineData(8f, 6f, 3.01f, false)]
    // The Xibarrus Theta fixed point: net closing is only 0.001, so six units of separation are
    // not a same-turn collision even though the pursuer's raw move is six.
    [InlineData(6.001f, 6.001f, 6f, false)]
    public void CanReachContactThisTurn_MeasuresNetClosingNotRawMove(
        float pursuerSpeed,
        float quarrySpeed,
        float separation,
        bool expected)
    {
        Assert.Equal(
            expected,
            BattleContactRules.CanReachContactThisTurn(separation, pursuerSpeed, quarrySpeed));
    }

    [Fact]
    public void SameTurnContactReach_RemainsValidForAssignedPair()
    {
        PursuitPairActivity atContact = Pair(
            separation: 1,
            pursuerSpeed: 7,
            quarrySpeed: 7);
        PursuitPairActivity justOutsideContact = atContact with { CurrentSeparation = 1.01f };

        Assert.Equal(
            ContactBreakResult.RemainInContact,
            BattleContactRules.Evaluate(Input([atContact])).Decision);
        Assert.Equal(
            ContactBreakResult.OrganizedForceDisengages,
            BattleContactRules.Evaluate(Input([justOutsideContact])).Decision);
    }

    [Fact]
    public void PursuerStopping_DisengagesOrganizedForce()
    {
        var input = Input(allPursuersBreakOff: true);

        Assert.Equal("pursuer_stops", BattleContactRules.Evaluate(input).Reason);
    }

    [Fact]
    public void TraceRenderer_UsesPairEvidenceFields()
    {
        string trace = BattleContactRules.Evaluate(
            Input([Pair(hasObservedClosingProgress: true)])).Trace.Render();

        Assert.Equal(
            "CONTACT_EVAL turn=7 side=second active_pursuers=2 pursuit_pairs=1 "
            + "observed_progress_pairs=1 positive_closing_pairs=1 slow_progress_pairs=0 "
            + "startup_grace_pairs=0 "
            + "attacked_recently_pairs=0 viable_fire_cycle_pairs=0 quarry_eliminated_pairs=0 "
            + "maintenance_evidence=progress pair_reasons=11>22:progress "
            + "pair_active=true pair_reach_this_turn=false rear_guard_active=false "
            + "masked_progress=0 masked_required=7 decision=RemainInContact "
            + "reason=pursuit_can_maintain_contact",
            trace);
    }

    [Fact]
    public void TraceRenderer_ClassifiesEachPairWithoutTheoreticalShotEvidence()
    {
        string trace = BattleContactRules.Evaluate(Input(
            [
                Pair(11, 22, pursuerSpeed: 7, quarrySpeed: 7, pairAttackedRecently: true),
                Pair(33, 44, pursuerSpeed: 7, quarrySpeed: 7,
                    fireCycleProgressedThisTurn: true,
                    fireCommitmentRemainsViable: true),
                Pair(55, 66, pursuerSpeed: 7, quarrySpeed: 7)
            ])).Trace.Render();

        Assert.Contains("pursuit_pairs=3", trace);
        Assert.Contains("positive_closing_pairs=0", trace);
        Assert.Contains("attacked_recently_pairs=1", trace);
        Assert.Contains("viable_fire_cycle_pairs=1", trace);
        Assert.Contains("maintenance_evidence=attack+fire", trace);
        Assert.Contains("pair_reasons=11>22:attack|33>44:fire|55>66:none", trace);
        Assert.Contains("reason=pursuit_can_maintain_contact", trace);
        Assert.DoesNotContain("pursuers_reasonable_shot", trace);
    }

    [Fact]
    public void TraceRenderer_ReportsFinalStallReasonWhenOnlyTheoreticalCapabilityRemains()
    {
        string trace = BattleContactRules.Evaluate(Input(
            [Pair(11, 22, pursuerSpeed: 7, quarrySpeed: 7)])).Trace.Render();

        Assert.Contains("pursuit_pairs=1", trace);
        Assert.Contains("positive_closing_pairs=0", trace);
        Assert.Contains("attacked_recently_pairs=0", trace);
        Assert.Contains("viable_fire_cycle_pairs=0", trace);
        Assert.Contains("maintenance_evidence=none", trace);
        Assert.Contains("pair_reasons=11>22:none", trace);
        Assert.Contains("decision=OrganizedForceDisengages", trace);
        Assert.Contains("reason=stalled_pursuit", trace);
    }
}
