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
public sealed class ActivePursuitContactLifecycleTests
{
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

    [Fact]
    public void AimAgainstUnrelatedQuarry_DoesNotQualifyAssignedPair()
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

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.False(activity.FireCommitmentRemainsViable);
        Assert.Equal("stalled_pursuit", EvaluateContact(activity).Reason);
    }

    [Fact]
    public void AttackAgainstUnrelatedQuarry_DoesNotQualifyAssignedPair()
    {
        Fixture fixture = CreateFixture();
        Assign(fixture, fixture.Quarry);
        ShootAction attack = ExecuteMissedAttack(fixture, fixture.OtherQuarry);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.Equal(0, attack.HitCount);
        Assert.False(activity.PairAttackedRecently);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(activity).Decision);
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

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.True(activity.CurrentSeparation < initialSeparation);
        Assert.True(activity.HasMeaningfulPositiveClosingSpeed);
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

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        BattleContactRules.Result result = EvaluateContact(activity);

        Assert.True(activity.CurrentSeparation < 30);
        Assert.True(activity.HasMeaningfulPositiveClosingSpeed);
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
            ProjectedPressInterceptTurns: 1,
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
        return attack;
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
