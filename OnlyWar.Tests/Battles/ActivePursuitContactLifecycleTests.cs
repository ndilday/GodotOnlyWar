using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 5 regression coverage for the active-pursuit invariant. These tests deliberately cross
/// the action metrics, assigned-pair materialization, and contact-rule seams instead of setting
/// evidence flags directly. The resolver tests cover the full battle loop; this fixture keeps the
/// contact lifecycle deterministic and small enough to isolate one round at a time.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class ActivePursuitContactLifecycleTests
{
    [Fact]
    public void PursuitProgress_DistinguishesSpeedAdvantageFromActualSeparationAndReassignment()
    {
        Fixture fixture = CreateFixture(quarryX: 30);
        List<string> log = [];
        Action<string> previous = BattleLog.Sink;
        try
        {
            BattleLog.Sink = log.Add;
            Assign(fixture, fixture.Quarry);
            fixture.Pursuer.Soldiers[0].CurrentSpeed = 6;
            fixture.Quarry.Soldiers[0].CurrentSpeed = 5;
            // A scalar speed advantage need not mean radial progress: leave both positions fixed.
            fixture.Service.LogPursuitProgress();
            string record = Assert.Single(log, line => line.StartsWith("PURSUIT_PROGRESS "));
            Assert.Contains("declared_speed_delta=1 ", record);
            Assert.Contains("declared_speed_not_evidence=true ", record);
            Assert.Contains("separation_gain=0 ", record);
            Assert.Contains("assignment_changed=true ", record);
            Assert.Contains("history_window=1/4 ", record);
            Assert.Contains("history_sample_valid=true ", record);
            Assert.Contains("history_validity_reason=valid ", record);
            Assert.Contains("history_reset_reason=none ", record);
            Assert.Contains("startup_status=grace ", record);
            Assert.Contains("observed_progress_contact_evidence=false ", record);
            Assert.All(record.Split(' ').Skip(1), field => Assert.Contains("=", field));
            log.Clear();
            Assign(fixture, fixture.Quarry);
            fixture.Service.LogPursuitProgress();
            Assert.Contains("assignment_changed=false ", Assert.Single(log));
            log.Clear();
            Assign(fixture, fixture.OtherQuarry);
            fixture.Service.LogPursuitProgress();
            Assert.Contains($"previous_quarry={fixture.Quarry.Id} ", Assert.Single(log));
            Assert.Contains("assignment_changed=true ", Assert.Single(log));
            log.Clear();
            Assign(fixture, fixture.OtherQuarry);
            var blocked = new MoveAction(fixture.Pursuer.Soldiers[0], fixture.Grid,
                (0, 0), (30, 0), 0);
            blocked.Execute(fixture.State);
            fixture.Service.LogPursuitProgress([blocked]);
            Assert.Contains("succeeded=false", Assert.Single(log,
                line => line.StartsWith("PURSUIT_MOVE_RESULT ")));
        }
        finally { BattleLog.Sink = previous; }
    }

    [Fact]
    public void FollowShotTrace_ExplainsThePairAggregateBranchWithoutChangingPosture()
    {
        Fixture fixture = CreateFixture(quarryX: 3000);
        List<string> log = [];
        Action<string> previous = BattleLog.Sink;
        try
        {
            BattleLog.Sink = log.Add;
            fixture.Service.EvaluatePursuitResponse(BattleSide.Opposing, []);
            string record = Assert.Single(log, line => line.StartsWith("FOLLOW_SHOT_EVAL "));
            Assert.Contains("scope=pair_aggregate ", record);
            Assert.Contains("pair_count=2 ", record);
            Assert.Contains("ranged_pursuer=", record);
            Assert.Contains("ranged_quarry=", record);
            Assert.Contains("shot_turns=none ", record);
            Assert.Contains("useful_attack_turns=none ", record);
            Assert.Contains("ranged_destination_condition=useful_range ", record);
            Assert.Contains("ranged_pursuer_move_speed=", record);
            Assert.Contains("ranged_quarry_move_speed=", record);
            Assert.Contains("ranged_unreachable_reason=", record);
            Assert.Contains("reason=", record);
            Assert.All(record.Split(' ').Skip(1), field => Assert.Contains("=", field));
            var posture = fixture.Service.GetPursuitPosture(BattleSide.Attacker);
            BattleLog.Sink = null;
            Fixture untraced = CreateFixture(quarryX: 3000);
            untraced.Service.EvaluatePursuitResponse(BattleSide.Opposing, []);
            Assert.Equal(posture, untraced.Service.GetPursuitPosture(BattleSide.Attacker));
        }
        finally { BattleLog.Sink = previous; }
    }

    [Fact]
    public void PursuitPosture_IsDecidedPerSquad_AndADrySquadThatCanCatchUpChases()
    {
        // Grist Nine Epsilon, 2026-09-22: posture was decided for the whole force, and a few
        // marines with rounds left held hundreds of dry ones at standoff. Now each squad answers
        // for itself: the loaded squad follows and shoots, the dry one chases.
        TwoPursuerFixture fixture = CreateTwoPursuerFixture(drySquadSpeed: 10f);

        fixture.Service.EvaluatePursuitResponse(BattleSide.Opposing, []);

        Assert.Equal(
            PursuitPosture.Follow,
            fixture.Service.GetSquadPursuitPosture(BattleSide.Attacker, fixture.Loaded.Id));
        Assert.Equal(
            PursuitPosture.Press,
            fixture.Service.GetSquadPursuitPosture(BattleSide.Attacker, fixture.Dry.Id));
        Assert.Equal(PursuitPosture.Press, fixture.Service.GetPursuitPosture(BattleSide.Attacker));

        Dictionary<int, EngagementRoleConstraint> constraints = [];
        fixture.Service.BuildRoleConstraints(
            BattleSide.Attacker,
            [fixture.Loaded, fixture.Dry],
            [fixture.Quarry],
            constraints,
            []);
        Assert.Equal(EngagementSquadRole.Follow, constraints[fixture.Loaded.Id].Role);
        Assert.Equal(EngagementSquadRole.Press, constraints[fixture.Dry.Id].Role);
    }

    [Fact]
    public void DrySquadThatCannotCatchUp_StandsDown_WhileTheForceKeepsPursuing()
    {
        // The same dry squad, but the quarry outruns it: it breaks off, and since the loaded squad
        // keeps pursuing it leaves the field instead of standing idle. Grist Nine Epsilon,
        // 2026-09-22: 22 of 33 squads broke off and stood still for hundreds of turns.
        TwoPursuerFixture fixture = CreateTwoPursuerFixture(drySquadSpeed: 4f);
        int dryBattleValue = fixture.Dry.AbleSoldiers.Sum(soldier => soldier.EffectiveBattleValue);
        int forceBattleValue = fixture.Metrics.BuildMetrics(BattleSide.Attacker).CurrentBattleValue;
        List<BattleEvent> events = [];

        BattleTerminalRequest terminal =
            fixture.Service.EvaluatePursuitResponse(BattleSide.Opposing, events);

        Assert.Null(terminal);
        Assert.Equal(BattleSquadStatus.Disengaged, fixture.Dry.Status);
        Assert.Equal(BattleSquadStatus.Active, fixture.Loaded.Status);
        Assert.Contains(fixture.Dry.Id, fixture.State.AttackerSide.StoodDownSquadIds);
        Assert.Contains(events, battleEvent =>
            battleEvent.Type == BattleEventType.SquadDisengaged
            && battleEvent.PrimarySquadId == fixture.Dry.Id);
        Assert.Equal(PursuitPosture.Follow, fixture.Service.GetPursuitPosture(BattleSide.Attacker));
        Assert.Equal(BattleSideIntent.Pursuing, fixture.State.AttackerSide.Intent);

        // Standing down is not a casualty: the side's strength is unchanged.
        Assert.Equal(dryBattleValue, fixture.State.AttackerSide.StoodDownBattleValue);
        Assert.Equal(
            forceBattleValue,
            fixture.Metrics.BuildMetrics(BattleSide.Attacker).CurrentBattleValue);
    }

    [Fact]
    public void PursuitTraceRecordsTheConcretePairSupplyingPressProjection()
    {
        Fixture fixture = CreateFixture(quarryX: 30);
        ((Soldier)fixture.Pursuer.Soldiers[0].Soldier).MoveSpeed = 10;
        ((Soldier)fixture.Quarry.Soldiers[0].Soldier).MoveSpeed = 6;
        ((Soldier)fixture.OtherQuarry.Soldiers[0].Soldier).MoveSpeed = 8;
        List<string> log = [];
        Action<string> previous = BattleLog.Sink;
        try
        {
            BattleLog.Sink = log.Add;
            fixture.Service.EvaluatePursuitResponse(BattleSide.Opposing, []);
            string record = Assert.Single(log, line => line.StartsWith("PURSUIT_EVAL "));
            Assert.Contains($"press_contact_pursuer={fixture.Pursuer.Id} ", record);
            Assert.Contains($"press_contact_quarry={fixture.Quarry.Id} ", record);
            Assert.Contains("press_attack_phase_turns=", record);
            Assert.Contains("press_contact_turns=", record);
            Assert.DoesNotContain("press_attack_phase_turns=never", record);
            string projection = Assert.Single(
                log,
                line => line.StartsWith("PRESS_CONTACT_PROJECTION "));
            Assert.Contains("estimate_kind=hypothetical_press_to_contact", projection);
            Assert.Contains("press_destination_condition=melee_contact_allowance", projection);
            Assert.Contains("press_pursuer_move_speed=", projection);
            Assert.Contains("press_quarry_move_speed=", projection);
            Assert.Contains("press_unreachable_reason=none", projection);
        }
        finally { BattleLog.Sink = previous; }
    }

    [Fact]
    public void LoggingDoesNotChangeObservedEvidenceOrContactDecision()
    {
        Action<string> previous = BattleLog.Sink;
        try
        {
            BattleLog.Sink = null;
            Fixture withoutLogging = CreateFixture(quarryX: 30);
            Assign(withoutLogging, withoutLogging.Quarry);
            Move(withoutLogging.Pursuer.Soldiers[0], withoutLogging, (0, 0), (4, 0), 4);
            Move(withoutLogging.Quarry.Soldiers[0], withoutLogging, (30, 0), (32, 0), 2);
            withoutLogging.Service.LogPursuitProgress();
            PursuitPairActivity activityWithoutLogging =
                Assert.Single(withoutLogging.BuildActivities());
            ContactBreakResult decisionWithoutLogging = EvaluateContact(activityWithoutLogging)
                .Decision;

            BattleLog.Sink = _ => { };
            Fixture withLogging = CreateFixture(quarryX: 30);
            Assign(withLogging, withLogging.Quarry);
            Move(withLogging.Pursuer.Soldiers[0], withLogging, (0, 0), (4, 0), 4);
            Move(withLogging.Quarry.Soldiers[0], withLogging, (30, 0), (32, 0), 2);
            withLogging.Service.LogPursuitProgress();
            PursuitPairActivity activityWithLogging =
                Assert.Single(withLogging.BuildActivities());
            ContactBreakResult decisionWithLogging = EvaluateContact(activityWithLogging).Decision;

            Assert.Equal(activityWithoutLogging, activityWithLogging);
            Assert.Equal(decisionWithoutLogging, decisionWithLogging);
        }
        finally
        {
            BattleLog.Sink = previous;
        }
    }

    [Fact]
    public void TheoreticalProjectedShotWithoutSelectedFireAction_BreaksEqualSpeedContact()
    {
        Fixture fixture = CreateFixture(quarryX: 8);
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(
            fixture.Pursuer.Soldiers[0].EquippedRangedWeapons[0].Template.MaximumRange
                > activity.CurrentSeparation);
        Assert.Null(fixture.Pursuer.Soldiers[0].Aim);
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.False(activity.FireCommitmentRemainsViable);

        ExhaustStartupGrace(fixture);
        activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void AimStartingAgainstAssignedQuarry_PreservesContact()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        BattleSoldier target = fixture.Quarry.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        AimAction aim = new(shooter, target, weapon, log: null);

        ExecuteAndRecord(fixture, aim);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.Equal(target.Soldier.Id, shooter.Aim?.Item1);
        Assert.Equal(0, shooter.Aim?.Item3);
        Assert.True(activity.FireCycleProgressedThisTurn);
        Assert.True(activity.FireCommitmentRemainsViable);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void AimAdvancementAgainstAssignedQuarry_PreservesContact()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        BattleSoldier target = fixture.Quarry.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, weapon, 2);
        AimAction aim = new(shooter, target, weapon, log: null);

        ExecuteAndRecord(fixture, aim);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.Equal(3, shooter.Aim?.Item3);
        Assert.True(activity.FireCycleProgressedThisTurn);
        Assert.True(activity.FireCommitmentRemainsViable);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void RetainedAimWithoutExecutedProgress_DoesNotPreserveContact()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        BattleSoldier target = fixture.Quarry.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, weapon, 2);
        fixture.Metrics.RecordRound(Array.Empty<IAction>());

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.True(activity.FireCommitmentRemainsViable);
        Assert.False(activity.FireCycleProgressedThisTurn);
        ExhaustStartupGrace(fixture);
        activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void AssignedAttackPreservesContactEvenWhenItMisses()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        ShootAction attack = ExecuteMissedAttack(fixture, fixture.Quarry);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());

        Assert.Equal(0, attack.HitCount);
        Assert.True(activity.PairAttackedRecently);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    // REVERSED 2026-09-22, deliberately. These two tests used to assert that an aim or attack at a
    // withdrawing squad other than the assigned quarry did NOT qualify the pair. Soldiers choose
    // their own targets, so that rule broke contact while the pursuers were still shooting the
    // withdrawal: in Grist Nine Epsilon all 33 pursuers were assigned to the Cover squad, ~100 aimed
    // and ~10 fired at other orks each turn, and the pursuit ended as stalled_pursuit with a third
    // of the ork force still under fire. Evidence is now credited to the pursuer's pair whichever
    // withdrawing squad it works on; a projected (theoretical) shot still never counts.
    [Fact]
    public void AimAgainstAnotherWithdrawingSquad_QualifiesThePursuersPair()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        AimAction aim = new(
            shooter,
            fixture.OtherQuarry.Soldiers[0],
            shooter.EquippedRangedWeapons[0],
            log: null);

        ExecuteAndRecord(fixture, aim);
        ExhaustStartupGrace(fixture);
        fixture.Metrics.RecordExecutedActions([aim]);
        fixture.Metrics.RecordRound();

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(activity.FireCycleProgressedThisTurn);
        Assert.True(activity.FireCommitmentRemainsViable);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void AttackAgainstAnotherWithdrawingSquad_QualifiesThePursuersPair()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        ExhaustStartupGrace(fixture);
        ShootAction attack = ExecuteMissedAttack(fixture, fixture.OtherQuarry);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.Equal(0, attack.HitCount);
        Assert.True(activity.PairAttackedRecently);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void FasterAssignedPursuer_PreservesContactThroughActualPairwiseClosing()
    {
        Fixture fixture = CreateFixture(quarryX: 30);
        Assign(fixture, fixture.Quarry);
        BattleSoldier pursuer = fixture.Pursuer.Soldiers[0];
        BattleSoldier quarry = fixture.Quarry.Soldiers[0];
        pursuer.CurrentSpeed = 8;
        quarry.CurrentSpeed = 7;
        float initialSeparation = Assert.Single(fixture.BuildActivities()).CurrentSeparation;

        Move(pursuer, fixture, (0, 0), (8, 0), 8);
        Move(quarry, fixture, (30, 0), (37, 0), 7);
        fixture.Service.LogPursuitProgress();

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.True(activity.CurrentSeparation < initialSeparation);
        Assert.True(activity.HasObservedClosingProgress);
        Assert.False(activity.CanReachContactThisTurn);
        Assert.Equal(ContactBreakResult.RemainInContact, result.Decision);
        Assert.Equal("pursuit_can_maintain_contact", result.Reason);
    }

    [Fact]
    public void CrossPairCapabilities_CannotBeAggregatedIntoContactEvidence()
    {
        CrossPairFixture fixture = CreateCrossPairFixture();
        fixture.Service.ReplaceCurrentTurnPursuitPairings(
        [
            new KeyValuePair<int, int>(fixture.FastEqualSpeedPursuer.Id, fixture.FastQuarry.Id),
            new KeyValuePair<int, int>(fixture.SlowEqualSpeedPursuer.Id, fixture.SlowQuarry.Id)
        ]);

        ExhaustStartupGrace(fixture);

        IReadOnlyList<PursuitPairActivity> activities = fixture.Service
            .BuildPursuitPairActivities(BattleSide.Attacker, BattleSide.Opposing);
        BattleContactRules.Result result = BattleContactRules.Evaluate(new(
            Turn: 1,
            IsFirstSide: true,
            ActivePursuerCount: 2,
            AllPursuersBreakOff: false,
            EnemyAlsoWithdrawing: false,
            PursuitPairs: activities,
            RearGuardActive: false,
            MaskedDepartureProgress: 0,
            WithdrawingSquadRunAllowance: 6));

        Assert.Equal(2, activities.Count);
        Assert.All(activities, activity =>
        {
            Assert.Equal(0, activity.ClosingSpeed);
            Assert.False(activity.HasActivePursuitEvidence);
        });
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void EliminatedQuarry_IsContactEvidenceNotAStalledPursuit()
    {
        // Grist Nine Epsilon, 2026-09-22: every pursuer was assigned to one single-soldier Cover
        // squad, that soldier was shot, and the empty pair list broke contact for the whole
        // withdrawing force.
        Fixture fixture = CreateFixture();
        ExhaustStartupGrace(fixture);
        fixture.State.RemoveSquad(fixture.Quarry);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.True(activity.QuarryEliminated);
        Assert.Equal(fixture.Quarry.Id, activity.QuarrySquadId);
        Assert.False(activity.HasStartupGrace);
        Assert.Contains("eliminated", activity.EvidenceReasonCode);
        Assert.Equal(ContactBreakResult.RemainInContact, result.Decision);
        Assert.Contains("quarry_eliminated_pairs=1", result.Trace.Render());
    }

    [Fact]
    public void DisengagedQuarry_IsNotContactEvidence()
    {
        Fixture fixture = CreateFixture();
        ExhaustStartupGrace(fixture);
        fixture.State.DisengageSquad(fixture.Quarry);

        IReadOnlyList<PursuitPairActivity> activities = fixture.BuildActivities();
        BattleContactRules.Result result = BattleContactRules.Evaluate(new(
            Turn: 1,
            IsFirstSide: false,
            ActivePursuerCount: 1,
            AllPursuersBreakOff: false,
            EnemyAlsoWithdrawing: false,
            PursuitPairs: activities,
            RearGuardActive: false,
            MaskedDepartureProgress: 0,
            WithdrawingSquadRunAllowance: 6));

        Assert.Empty(activities);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void FollowMovement_PreservesContactThroughGenuineClosing()
    {
        Fixture fixture = CreateFixture(quarryX: 30);
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.JogToward;
        BattleSoldier pursuer = fixture.Pursuer.Soldiers[0];
        BattleSoldier quarry = fixture.Quarry.Soldiers[0];
        pursuer.CurrentSpeed = 8;
        quarry.CurrentSpeed = 6;
        Move(pursuer, fixture, (0, 0), (8, 0), 8);
        Move(quarry, fixture, (30, 0), (36, 0), 6);
        fixture.Service.LogPursuitProgress();

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.True(activity.CurrentSeparation < 30);
        Assert.True(activity.HasObservedClosingProgress);
        Assert.False(activity.PairAttackedRecently);
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.Equal(ContactBreakResult.RemainInContact, result.Decision);
    }

    [Fact]
    public void PreparationCountsOnlyWhileItMakesBoundedProgressTowardViableAttack()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();
        ReadyRangedWeaponAction ready = new(shooter, weapon);

        ExecuteAndRecord(fixture, ready);
        PursuitPairActivity progressed = Assert.Single(fixture.BuildActivities());
        Assert.True(ready.Succeeded);
        Assert.True(progressed.HasQualifyingFireCycleProgress);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(progressed).Decision);

        fixture.Metrics.RecordRound(Array.Empty<IAction>());
        ExhaustStartupGrace(fixture);
        PursuitPairActivity stale = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(stale);

        Assert.False(stale.FireCycleProgressedThisTurn);
        Assert.False(stale.HasQualifyingFireCycleProgress);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void ReloadPreparationAgainstAssignedQuarry_PreservesContactOnlyForSuccessfulProgress()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        weapon.LoadedAmmo = 0;
        weapon.ReserveAmmo = weapon.Template.AmmoCapacity;
        ReloadRangedWeaponAction reload = new(shooter, weapon);

        ExecuteAndRecord(fixture, reload);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(reload.Succeeded);
        Assert.True(activity.HasQualifyingFireCycleProgress);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void InvalidatedTarget_ReleasesAimCommitmentAndAllowsContactBreak()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        BattleSoldier target = fixture.Quarry.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, weapon, 2);
        fixture.Grid.MoveSoldier(target, (200, 0), target.Orientation);
        target.TopLeft = (200, 0);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.False(activity.FireCommitmentRemainsViable);
        Assert.False(activity.HasQualifyingFireCycleProgress);
        ExhaustStartupGrace(fixture);
        activity = Assert.Single(fixture.BuildActivities());
        result = EvaluateContact(activity);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void InvalidatedWeapon_ReleasesAimCommitmentAndAllowsContactBreak()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        BattleSoldier target = fixture.Quarry.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, weapon, 2);
        weapon.LoadedAmmo = 0;

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.False(activity.FireCommitmentRemainsViable);
        Assert.False(activity.HasQualifyingFireCycleProgress);
        ExhaustStartupGrace(fixture);
        activity = Assert.Single(fixture.BuildActivities());
        result = EvaluateContact(activity);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    [Fact]
    public void MatchedSpeedHoldFireCycle_PreparationAimAdvanceShootDoesNotPermanentlyPreserveContact()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        BattlePursuitPlanner.Result posture = BattlePursuitPlanner.Evaluate(new(
            Turn: 1,
            IsFirstSide: false,
            Aggression: Aggression.Normal,
            PursuerAbleSoldiers: 1,
            WithdrawingAbleSoldiers: 1,
            FastestPursuitSpeed: 8,
            SlowestWithdrawalSpeed: 8,
            ProjectedPressInterceptTurns: float.PositiveInfinity,
            ProjectedFollowPositiveShotTurns: 0,
            WithdrawerReturnsFire: true,
            PursuerCanReachContactThisTurn: false));
        Assert.Equal(PursuitPosture.Standoff, posture.Posture);
        Assert.Equal("cannot_close_stands_and_fires", posture.Reason);
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        BattleSoldier target = fixture.Quarry.Soldiers[0];
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();

        ReadyRangedWeaponAction ready = new(shooter, weapon);
        ExecuteAndRecord(fixture, ready);
        AssertActiveContact(fixture);

        AimAction startAim = new(shooter, target, weapon, log: null);
        ExecuteAndRecord(fixture, startAim);
        PursuitPairActivity aimed = AssertActiveContact(fixture);
        Assert.True(aimed.FireCycleProgressedThisTurn);

        AimAction advanceAim = new(shooter, target, weapon, log: null);
        ExecuteAndRecord(fixture, advanceAim);
        Assert.Equal(1, shooter.Aim?.Item3);
        AssertActiveContact(fixture);

        ShootAction shoot = ExecuteMissedAttack(fixture, fixture.Quarry);
        PursuitPairActivity fired = AssertActiveContact(fixture);
        Assert.Equal(0, shoot.HitCount);
        Assert.True(fired.PairAttackedRecently);
        Assert.Null(shooter.Aim);
        Assert.Equal(0, shooter.CurrentSpeed);
        Assert.Equal(0, target.CurrentSpeed);

        // The attack gets the normal recent-action grace window, but there is no permanent
        // exception: once two complete rounds pass without closing or fire-cycle progress,
        // matched-speed contact is allowed to break.
        fixture.Metrics.RecordRound(Array.Empty<IAction>());
        AssertActiveContact(fixture);
        fixture.Metrics.RecordRound(Array.Empty<IAction>());
        ExhaustStartupGrace(fixture);
        PursuitPairActivity quiet = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(quiet);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, result.Decision);
        Assert.Equal("stalled_pursuit", result.Reason);
    }

    private static void Assign(Fixture fixture, BattleSquad quarry)
    {
        fixture.Service.ReplaceCurrentTurnPursuitPairings(
        [new KeyValuePair<int, int>(fixture.Pursuer.Id, quarry.Id)]);
    }

    private static void ExecuteAndRecord(Fixture fixture, IAction action)
    {
        action.Execute(fixture.State);
        fixture.Metrics.RecordRound([action]);
        fixture.Service.LogPursuitProgress();
    }

    private static ShootAction ExecuteMissedAttack(Fixture fixture, BattleSquad targetSquad)
    {
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        BattleSoldier target = targetSquad.Soldiers[0];
        ShootAction attack = new(
            shooter.Soldier.Id,
            target.Soldier.Id,
            shooter.EquippedRangedWeapons[0].Template.Id,
            range: 8,
            numberOfShots: 1,
            useBulk: false,
            grid: null,
            random: new GuaranteedMissRng());
        attack.Execute(fixture.State);
        fixture.Metrics.RecordRound([attack]);
        fixture.Service.LogPursuitProgress();
        return attack;
    }

    private static void ExhaustStartupGrace(Fixture fixture)
    {
        for (int turn = 0; turn < PursuitProgressPolicy.StartupGraceTurns; turn++)
        {
            Assign(fixture, fixture.Quarry);
            fixture.Service.LogPursuitProgress();
        }
    }

    private static void ExhaustStartupGrace(CrossPairFixture fixture)
    {
        for (int turn = 0; turn < PursuitProgressPolicy.StartupGraceTurns; turn++)
        {
            fixture.Service.ReplaceCurrentTurnPursuitPairings(
            [
                new KeyValuePair<int, int>(
                    fixture.FastEqualSpeedPursuer.Id,
                    fixture.FastQuarry.Id),
                new KeyValuePair<int, int>(
                    fixture.SlowEqualSpeedPursuer.Id,
                    fixture.SlowQuarry.Id)
            ]);
            fixture.Service.LogPursuitProgress();
        }
    }

    private static void Move(
        BattleSoldier soldier,
        Fixture fixture,
        ValueTuple<int, int> origin,
        ValueTuple<int, int> destination,
        float movementBudget)
    {
        MoveAction move = new(
            soldier,
            fixture.Grid,
            origin,
            destination,
            soldier.Orientation,
            movementBudget);
        move.Execute(fixture.State);
        Assert.True(move.Succeeded, move.Description());
    }

    private static PursuitPairActivity AssertActiveContact(Fixture fixture)
    {
        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);
        Assert.Equal(ContactBreakResult.RemainInContact, result.Decision);
        Assert.Equal("pursuit_can_maintain_contact", result.Reason);
        return activity;
    }

    private static BattleContactRules.Result EvaluateContact(PursuitPairActivity activity) =>
        BattleContactRules.Evaluate(new(
            Turn: 1,
            IsFirstSide: false,
            ActivePursuerCount: 1,
            AllPursuersBreakOff: false,
            EnemyAlsoWithdrawing: false,
            PursuitPairs: [activity],
            RearGuardActive: false,
            MaskedDepartureProgress: 0,
            WithdrawingSquadRunAllowance: 6));

    private static Fixture CreateFixture(int quarryX = 8)
    {
        BattleSquad pursuerSeed = CreateSquad("Pursuer", 95_001);
        BattleSquad quarrySeed = CreateSquad("Quarry", 95_002);
        BattleSquad otherQuarrySeed = CreateSquad("Other Quarry", 95_003);
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [pursuerSeed.Id] = pursuerSeed },
            new Dictionary<int, BattleSquad>
            {
                [quarrySeed.Id] = quarrySeed,
                [otherQuarrySeed.Id] = otherQuarrySeed
            });
        BattleSquad pursuer = state.GetSquad(pursuerSeed.Id);
        BattleSquad quarry = state.GetSquad(quarrySeed.Id);
        BattleSquad otherQuarry = state.GetSquad(otherQuarrySeed.Id);
        BattleGridManager grid = new();
        Place(grid, pursuer.Soldiers[0], true, 0, 0);
        Place(grid, quarry.Soldiers[0], false, quarryX, 0);
        Place(grid, otherQuarry.Soldiers[0], false, quarryX + 10, 0);
        BattleRoundMetrics metrics = new(state);
        BattleWithdrawalService service = CreateService(state, grid, metrics);
        return new(state, grid, service, metrics, pursuer, quarry, otherQuarry);
    }

    private static CrossPairFixture CreateCrossPairFixture()
    {
        BattleSquad fastPursuerSeed = CreateSquad("Fast Pursuer", 95_011);
        BattleSquad slowPursuerSeed = CreateSquad("Slow Pursuer", 95_012);
        BattleSquad fastQuarrySeed = CreateSquad("Fast Quarry", 95_013);
        BattleSquad slowQuarrySeed = CreateSquad("Slow Quarry", 95_014);
        BattleState state = new(
            new Dictionary<int, BattleSquad>
            {
                [fastPursuerSeed.Id] = fastPursuerSeed,
                [slowPursuerSeed.Id] = slowPursuerSeed
            },
            new Dictionary<int, BattleSquad>
            {
                [fastQuarrySeed.Id] = fastQuarrySeed,
                [slowQuarrySeed.Id] = slowQuarrySeed
            });
        BattleSquad fastPursuer = state.GetSquad(fastPursuerSeed.Id);
        BattleSquad slowPursuer = state.GetSquad(slowPursuerSeed.Id);
        BattleSquad fastQuarry = state.GetSquad(fastQuarrySeed.Id);
        BattleSquad slowQuarry = state.GetSquad(slowQuarrySeed.Id);
        BattleGridManager grid = new();
        Place(grid, fastPursuer.Soldiers[0], true, 0, 0);
        Place(grid, fastQuarry.Soldiers[0], false, 30, 0);
        Place(grid, slowPursuer.Soldiers[0], true, 0, 20);
        Place(grid, slowQuarry.Soldiers[0], false, 30, 20);
        fastPursuer.Soldiers[0].CurrentSpeed = 10;
        fastQuarry.Soldiers[0].CurrentSpeed = 10;
        slowPursuer.Soldiers[0].CurrentSpeed = 6;
        slowQuarry.Soldiers[0].CurrentSpeed = 6;
        BattleRoundMetrics metrics = new(state);
        BattleWithdrawalService service = CreateService(state, grid, metrics);
        return new(
            service,
            fastPursuer,
            slowPursuer,
            fastQuarry,
            slowQuarry);
    }

    private static TwoPursuerFixture CreateTwoPursuerFixture(float drySquadSpeed)
    {
        BattleSquad loadedSeed = CreateSquad("Loaded Pursuer", 95_021);
        BattleSquad drySeed = CreateSquad("Dry Pursuer", 95_022);
        BattleSquad quarrySeed = CreateSquad("Quarry", 95_023);
        BattleState state = new(
            new Dictionary<int, BattleSquad>
            {
                [loadedSeed.Id] = loadedSeed,
                [drySeed.Id] = drySeed
            },
            new Dictionary<int, BattleSquad> { [quarrySeed.Id] = quarrySeed });
        BattleSquad loaded = state.GetSquad(loadedSeed.Id);
        BattleSquad dry = state.GetSquad(drySeed.Id);
        BattleSquad quarry = state.GetSquad(quarrySeed.Id);
        EquipTypedRifle(loaded.Soldiers[0], 95_121);
        RangedWeapon empty = EquipTypedRifle(dry.Soldiers[0], 95_122);
        empty.LoadedAmmo = 0;
        empty.ReserveAmmo = 0;
        ((Soldier)loaded.Soldiers[0].Soldier).MoveSpeed = 10;
        ((Soldier)dry.Soldiers[0].Soldier).MoveSpeed = drySquadSpeed;
        ((Soldier)quarry.Soldiers[0].Soldier).MoveSpeed = 6;
        BattleGridManager grid = new();
        Place(grid, loaded.Soldiers[0], true, 0, 0);
        Place(grid, dry.Soldiers[0], true, 0, 4);
        Place(grid, quarry.Soldiers[0], false, 30, 2);
        BattleRoundMetrics metrics = new(state);
        return new(CreateService(state, grid, metrics), loaded, dry, quarry, state, metrics);
    }

    private static RangedWeapon EquipTypedRifle(BattleSoldier soldier, int templateId)
    {
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            templateId,
            "Typed rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 3,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 40,
            maxDistance: 200,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 1,
            doesDamageDegradeWithRange: false,
            reloadTime: 1,
            ammunitionType: new AmmunitionType(templateId + 1_000, "Typed rounds")));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(rifle);
        soldier.ReadyWeapon(rifle);
        return rifle;
    }

    private static BattleWithdrawalService CreateService(
        BattleState state,
        BattleGridManager grid,
        BattleRoundMetrics metrics)
    {
        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        FixedRNG random = new();
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        BattleMoraleService morale = new(
            state,
            grid,
            execution,
            state.AllAttackerSquads.Values.Concat(state.AllOpposingSquads.Values));
        return new(state, grid, rules, metrics, morale);
    }

    private static BattleSquad CreateSquad(string name, int soldierId)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(
            name: name,
            dexterity: 18,
            skills: [new Skill(TestSkills.Ranged, 12)]);
        soldier.Id = soldierId;
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldier));
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = (x, y);
        grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
    }

    private sealed record Fixture(
        BattleState State,
        BattleGridManager Grid,
        BattleWithdrawalService Service,
        BattleRoundMetrics Metrics,
        BattleSquad Pursuer,
        BattleSquad Quarry,
        BattleSquad OtherQuarry)
    {
        public IReadOnlyList<PursuitPairActivity> BuildActivities() =>
            Service.BuildPursuitPairActivities(BattleSide.Attacker, BattleSide.Opposing);
    }

    private sealed record TwoPursuerFixture(
        BattleWithdrawalService Service,
        BattleSquad Loaded,
        BattleSquad Dry,
        BattleSquad Quarry,
        BattleState State,
        BattleRoundMetrics Metrics);

    private sealed record CrossPairFixture(
        BattleWithdrawalService Service,
        BattleSquad FastEqualSpeedPursuer,
        BattleSquad SlowEqualSpeedPursuer,
        BattleSquad FastQuarry,
        BattleSquad SlowQuarry);

    private sealed class GuaranteedMissRng : IRNG
    {
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;

        public double GetLinearDouble() => 0;

        public int GetIntBelowMax(int min, int max) => min;

        public double NextRandomZValue() => 100;
    }

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
