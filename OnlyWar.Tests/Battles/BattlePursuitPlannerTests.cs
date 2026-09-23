using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Domain.Orders;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattlePursuitPlannerTests
{
    // A pursuer with a genuine speed edge (10 vs 8), so the aggression policy is what these
    // cases exercise and not the cannot-close override.
    private static BattlePursuitPlanner.Input Input(Aggression aggression = Aggression.Normal) =>
        new(4, true, aggression, 6, 8, 10, 8, 2, 1, WithdrawerReturnsFire: true);

    [Fact]
    public void OutnumberingFasterForce_StillAnswersToItsAggressionPolicy()
    {
        // The old outnumber-and-faster shortcut forced Press here; a dominant pursuer now
        // follows the same policy as anyone else (a shooting-superior force should not be
        // railroaded into melee just because it is winning).
        var input = new BattlePursuitPlanner.Input(
            4, true, Aggression.Avoid, 12, 8, 10, 8, 2, 1, WithdrawerReturnsFire: true);

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(input);

        Assert.Equal(PursuitPosture.BreakOff, result.Posture);
        Assert.Equal("avoid_breaks_off", result.Reason);
    }

    [Theory]
    [InlineData(Aggression.Avoid, PursuitPosture.BreakOff)]
    [InlineData(Aggression.Cautious, PursuitPosture.Follow)]
    [InlineData(Aggression.Attritional, PursuitPosture.Press)]
    [InlineData(Aggression.Aggressive, PursuitPosture.Press)]
    public void Pursuer_UsesAggressionPolicy(Aggression aggression, PursuitPosture expected)
    {
        Assert.Equal(expected, BattlePursuitPlanner.Evaluate(Input(aggression)).Posture);
    }

    [Fact]
    public void Normal_PressesWhenInterceptTiesPositiveShot()
    {
        var input = Input() with { ProjectedPressInterceptTurns = 2, ProjectedFollowPositiveShotTurns = 2 };

        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(input).Posture);
    }

    [Fact]
    public void Normal_FollowsWhenPositiveShotComesFirst()
    {
        var input = Input() with { ProjectedPressInterceptTurns = 3, ProjectedFollowPositiveShotTurns = 2 };

        Assert.Equal(PursuitPosture.Follow, BattlePursuitPlanner.Evaluate(input).Posture);
    }

    [Theory]
    [InlineData(Aggression.Attritional)]
    [InlineData(Aggression.Aggressive)]
    [InlineData(Aggression.Normal)]
    public void FasterArmedPursuer_ShootsUnresistingPrey_InsteadOfPressing(Aggression aggression)
    {
        // Faster pursuer, positive follow shot available, and the withdrawer is not
        // returning fire (routed, or melee-only): even the most aggressive posture keeps
        // shooting rather than closing to melee. Projections favor Press (intercept turn 1
        // beats first positive shot at turn 2) so every one of these aggressions would
        // otherwise Press — the override is what flips them.
        var input = new BattlePursuitPlanner.Input(
            4, true, aggression, 12, 8, 10, 8, 1, 2, WithdrawerReturnsFire: false);

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(input);

        Assert.Equal(PursuitPosture.Follow, result.Posture);
        Assert.Equal("shoots_unresisting_prey", result.Reason);
    }

    [Fact]
    public void UnresistingPreyOverride_RequiresAWorkingGun()
    {
        // No projected positive follow shot (no ranged weapons): Press stands. A melee-only
        // pursuer has nothing better to do than keep contact; the contact rules end the chase
        // once it demonstrably lands nothing.
        var meleeOnly = new BattlePursuitPlanner.Input(
            4, true, Aggression.Aggressive, 12, 8, 10, 8, 2, null, WithdrawerReturnsFire: false);
        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(meleeOnly).Posture);
    }

    // Explicitly unreachable pair projection, quarry already in range (a positive shot is
    // available this turn, not in two), and out of melee reach: only the guns are left.
    private static BattlePursuitPlanner.Input StalledChase(Aggression aggression) =>
        new(4, true, aggression, 12, 8, 8, 8, float.PositiveInfinity, 0, WithdrawerReturnsFire: true,
            PursuerCanReachContactThisTurn: false);

    [Theory]
    [InlineData(Aggression.Cautious)]
    [InlineData(Aggression.Normal)]
    [InlineData(Aggression.Attritional)]
    [InlineData(Aggression.Aggressive)]
    public void PursuerWithNoSpeedEdge_StandsAndFiresInsteadOfChasing(Aggression aggression)
    {
        // Neither chasing posture pays here. Press never shoots; Follow jogs at half a run, so
        // it cannot hold the gap either, and buys its extra turns in range with full-Bulk,
        // unaimed shots. Standing still is the higher-damage option for all four policies.
        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(StalledChase(aggression));

        Assert.Equal(PursuitPosture.Standoff, result.Posture);
        Assert.Equal("cannot_close_stands_and_fires", result.Reason);
    }

    [Fact]
    public void PursuerThatCanNeitherCloseNorShoot_BreaksOff()
    {
        // Out of range with no way to regain it (the projected shot is two turns of jogging
        // away, which an equal-speed quarry simply outruns): there is nothing left to gain from
        // contact, so the engagement ends rather than grinding to the turn cap.
        var outOfRange = StalledChase(Aggression.Aggressive) with
        {
            ProjectedFollowPositiveShotTurns = 2
        };
        Assert.Equal(PursuitPosture.BreakOff, BattlePursuitPlanner.Evaluate(outOfRange).Posture);
        Assert.Equal("cannot_close_or_shoot", BattlePursuitPlanner.Evaluate(outOfRange).Reason);

        // Same for a melee-only pursuer that will never lay a hand on them.
        var noGun = StalledChase(Aggression.Aggressive) with
        {
            ProjectedFollowPositiveShotTurns = null
        };
        Assert.Equal(PursuitPosture.BreakOff, BattlePursuitPlanner.Evaluate(noGun).Posture);
    }

    [Fact]
    public void CannotCloseOverride_YieldsToAMeleeCatchAvailableThisTurn()
    {
        // Matched speeds do not matter when the pursuer is already on top of the quarry — it can
        // catch someone this turn, so its policy stands.
        var inContact = StalledChase(Aggression.Aggressive) with
        {
            PursuerCanReachContactThisTurn = true
        };

        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(inContact).Posture);
    }

    [Fact]
    public void CannotCloseOverride_UsesTheExplicitUnreachableProjection()
    {
        // The force extrema no longer decide reachability. An explicit unreachable pair keeps the
        // arithmetic override even when another force member happens to be faster.
        var unreachable = StalledChase(Aggression.Aggressive) with
        {
            FastestPursuitSpeed = 10f
        };
        Assert.Equal(PursuitPosture.Standoff, BattlePursuitPlanner.Evaluate(unreachable).Posture);

        var reachable = unreachable with
        {
            ProjectedPressInterceptTurns = 2f
        };
        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(reachable).Posture);
    }

    [Theory]
    [InlineData(PursuitDoctrine.FireSupport, Aggression.Normal)]
    [InlineData(PursuitDoctrine.Mixed, Aggression.Cautious)]
    public void SquadWithNoShot_ChasesWhateverItWouldOtherwisePrefer(
        PursuitDoctrine doctrine,
        Aggression aggression)
    {
        // Out of ammunition (or no gun that will ever reach): following or standing off can do
        // nothing, so the squad closes. The quarry is catchable (intercept in 2 turns).
        var dry = Input(aggression) with
        {
            ProjectedFollowPositiveShotTurns = null,
            Doctrine = doctrine
        };

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(dry);

        Assert.Equal(PursuitPosture.Press, result.Posture);
        Assert.Equal("cannot_shoot_chases", result.Reason);
    }

    [Fact]
    public void SquadWithNoShotThatCannotCatchUp_BreaksOff()
    {
        var dryAndOutrun = StalledChase(Aggression.Normal) with
        {
            ProjectedFollowPositiveShotTurns = null,
            Doctrine = PursuitDoctrine.FireSupport
        };

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(dryAndOutrun);

        Assert.Equal(PursuitPosture.BreakOff, result.Posture);
        Assert.Equal("cannot_close_or_shoot", result.Reason);
    }

    [Fact]
    public void NormalPolicy_FollowsTheSquadsEquipmentDoctrine()
    {
        // The projections favour Press here (contact in 1, first shot in 2) and Follow in the
        // second case (contact in 3, shot in 2). Doctrine overrides both for the specialists.
        var pressFavoured = Input() with { ProjectedPressInterceptTurns = 1, ProjectedFollowPositiveShotTurns = 2 };
        var followFavoured = Input() with { ProjectedPressInterceptTurns = 3, ProjectedFollowPositiveShotTurns = 2 };

        BattlePursuitPlanner.Result devastators = BattlePursuitPlanner.Evaluate(
            pressFavoured with { Doctrine = PursuitDoctrine.FireSupport });
        BattlePursuitPlanner.Result assault = BattlePursuitPlanner.Evaluate(
            followFavoured with { Doctrine = PursuitDoctrine.ContactSeeking });

        Assert.Equal(PursuitPosture.Follow, devastators.Posture);
        Assert.Equal("fire_support_follows", devastators.Reason);
        Assert.Equal(PursuitPosture.Press, assault.Posture);
        Assert.Equal("contact_seeking_presses", assault.Reason);
    }

    [Fact]
    public void ContactSeekingSquad_StillPressesUnresistingPrey()
    {
        // The "keep shooting helpless prey" override is for gunlines; an assault squad's pistols
        // do not make it one.
        var input = new BattlePursuitPlanner.Input(
            4, true, Aggression.Normal, 12, 8, 10, 8, 1, 2, WithdrawerReturnsFire: false,
            Doctrine: PursuitDoctrine.ContactSeeking);

        Assert.Equal(PursuitPosture.Press, BattlePursuitPlanner.Evaluate(input).Posture);
    }

    [Theory]
    [InlineData(Aggression.Normal, PursuitPosture.BreakOff)]
    [InlineData(Aggression.Aggressive, PursuitPosture.BreakOff)]
    public void PressingSquadFacingAHopelesslyLongChase_BreaksOff(
        Aggression aggression,
        PursuitPosture expected)
    {
        // Grist Nine Xi in sector generation: a one-cell-per-turn gain, contact ~700 turns out.
        // Reachable in the arithmetic, not catching up in any sense that matters.
        var longChase = Input(aggression) with
        {
            ProjectedPressInterceptTurns = BattlePursuitPlanner.MaximumChaseTurns + 1,
            ProjectedFollowPositiveShotTurns = null
        };

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(longChase);

        Assert.Equal(expected, result.Posture);
        Assert.Equal("chase_too_long_breaks_off", result.Reason);
    }

    [Theory]
    [InlineData(Aggression.Normal, PursuitDoctrine.ContactSeeking)]
    [InlineData(Aggression.Aggressive, PursuitDoctrine.Mixed)]
    public void LongChaseWithAShotLater_Follows(Aggression aggression, PursuitDoctrine doctrine)
    {
        // Grist Nine Epsilon, 2026-09-22: assault squads with bolt pistols broke off on the first
        // pursuit turn because the chase was too long, although they could still shoot.
        var longChase = Input(aggression) with
        {
            Doctrine = doctrine,
            ProjectedPressInterceptTurns = BattlePursuitPlanner.MaximumChaseTurns + 1,
            ProjectedFollowPositiveShotTurns = 3
        };

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(longChase);

        Assert.Equal(PursuitPosture.Follow, result.Posture);
        Assert.Equal("chase_too_long_follows", result.Reason);
    }

    [Fact]
    public void LongChaseWithAShotNow_StandsAndFires()
    {
        var longChase = Input(Aggression.Aggressive) with
        {
            ProjectedPressInterceptTurns = BattlePursuitPlanner.MaximumChaseTurns + 1,
            ProjectedFollowPositiveShotTurns = 0
        };

        BattlePursuitPlanner.Result result = BattlePursuitPlanner.Evaluate(longChase);

        Assert.Equal(PursuitPosture.Standoff, result.Posture);
        Assert.Equal("chase_too_long_stands_and_fires", result.Reason);
    }

    [Fact]
    public void FollowingSquadIsNotJudgedOnHowLongAChaseWouldTake()
    {
        // A following squad is shooting, not chasing; a far contact time says nothing about it.
        var shooting = Input(Aggression.Cautious) with
        {
            ProjectedPressInterceptTurns = BattlePursuitPlanner.MaximumChaseTurns + 50,
            ProjectedFollowPositiveShotTurns = 1
        };

        Assert.Equal(PursuitPosture.Follow, BattlePursuitPlanner.Evaluate(shooting).Posture);
    }

    [Theory]
    [InlineData(Aggression.Normal, PursuitDoctrine.ContactSeeking, true, 5f, false)]
    [InlineData(Aggression.Normal, PursuitDoctrine.ContactSeeking, true, 50f, true)]
    [InlineData(Aggression.Normal, PursuitDoctrine.Mixed, true, 5f, true)]
    [InlineData(Aggression.Normal, PursuitDoctrine.FireSupport, true, 5f, true)]
    [InlineData(Aggression.Aggressive, PursuitDoctrine.Mixed, true, 5f, false)]
    [InlineData(Aggression.Aggressive, PursuitDoctrine.Mixed, false, 5f, true)]
    [InlineData(Aggression.Avoid, PursuitDoctrine.Mixed, true, 5f, false)]
    public void FollowProjection_IsSkippedOnlyWhenItCannotChangeThePosture(
        Aggression aggression,
        PursuitDoctrine doctrine,
        bool withdrawerReturnsFire,
        float pressTurns,
        bool expectedNeeded)
    {
        Assert.Equal(
            expectedNeeded,
            BattlePursuitPlanner.NeedsFollowProjection(
                aggression, doctrine, withdrawerReturnsFire, pressTurns, false));
        if (expectedNeeded) return;

        // Where it is skipped, every possible projection gives the same posture.
        PursuitPosture[] postures = new float?[] { null, 0f, 1f, 5f }
            .Select(shot => BattlePursuitPlanner.Evaluate(Input(aggression) with
            {
                Doctrine = doctrine,
                WithdrawerReturnsFire = withdrawerReturnsFire,
                ProjectedPressInterceptTurns = pressTurns,
                ProjectedFollowPositiveShotTurns = shot
            }).Posture)
            .Distinct()
            .ToArray();
        Assert.Single(postures);
    }

    [Fact]
    public void SquadTrace_NamesTheSquadAndItsDoctrine()
    {
        string trace = BattlePursuitPlanner.Evaluate(Input() with
        {
            PursuerSquadId = 17,
            Doctrine = PursuitDoctrine.FireSupport
        }).Trace.Render();

        Assert.StartsWith(
            "PURSUIT_EVAL turn=4 side=first squad=17 doctrine=FireSupport pursuer_soldiers=6 ",
            trace);
    }

    [Fact]
    public void TraceRenderer_IsStableAndComplete()
    {
        string trace = BattlePursuitPlanner.Evaluate(Input(Aggression.Cautious)).Trace.Render();

        Assert.Equal("PURSUIT_EVAL turn=4 side=first pursuer_soldiers=6 withdrawing_soldiers=8 " +
                     "fastest_pursuit_speed=10 slowest_withdrawal_speed=8 aggression=Cautious " +
                     "withdrawer_returns_fire=true melee_reach_this_turn=false " +
                     "press_estimate_kind=hypothetical_press_to_contact " +
                     "press_attack_phase_turns=2 press_contact_turns=none " +
                     "press_contact_pursuer=none press_contact_quarry=none " +
                     "follow_estimate_kind=hypothetical_useful_attack_opportunity " +
                     "follow_useful_attack_turns=1 decision=Follow " +
                     "reason=cautious_follows", trace);
    }
}
