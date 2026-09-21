using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Models;
using OnlyWar.Domain;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public sealed class PursuitFirePreparationTests
{
    [Fact]
    public void RequiredReadyAgainstAssignedViableQuarry_PreservesContactForThatRound()
    {
        Fixture fixture = CreateFixture();
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();
        ReadyRangedWeaponAction ready = new(shooter, weapon);

        ExecuteAsHoldPair(fixture, ready);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(activity.FireCycleProgressedThisTurn);
        Assert.True(activity.FireCommitmentRemainsViable);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void RequiredReloadAgainstAssignedViableQuarry_PreservesContactForThatRound()
    {
        Fixture fixture = CreateFixture();
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        weapon.LoadedAmmo = 0;
        weapon.ReserveAmmo = weapon.Template.AmmoCapacity;
        ReloadRangedWeaponAction reload = new(shooter, weapon);

        ExecuteAsHoldPair(fixture, reload);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(reload.Succeeded);
        Assert.True(activity.FireCycleProgressedThisTurn);
        Assert.True(activity.FireCommitmentRemainsViable);
        Assert.Equal(ContactBreakResult.RemainInContact, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void PreparationUnderMovementPolicy_DoesNotQualify()
    {
        Fixture fixture = CreateFixture();
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();
        ReadyRangedWeaponAction ready = new(shooter, weapon);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.StepForward;
        fixture.Service.ReplaceCurrentTurnPursuitPairings([
            new KeyValuePair<int, int>(fixture.Pursuer.Id, fixture.Quarry.Id)]);

        ready.Execute(fixture.State);
        fixture.Metrics.RecordRound([ready]);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(ready.Succeeded);
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.False(activity.FireCommitmentRemainsViable);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void ReloadUnderMovementPolicy_DoesNotQualify()
    {
        Fixture fixture = CreateFixture();
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        weapon.LoadedAmmo = 0;
        weapon.ReserveAmmo = weapon.Template.AmmoCapacity;
        ReloadRangedWeaponAction reload = new(shooter, weapon);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.JogToward;
        fixture.Service.ReplaceCurrentTurnPursuitPairings([
            new KeyValuePair<int, int>(fixture.Pursuer.Id, fixture.Quarry.Id)]);

        reload.Execute(fixture.State);
        fixture.Metrics.RecordRound([reload]);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(reload.Succeeded);
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.False(activity.FireCommitmentRemainsViable);
    }

    [Fact]
    public void PreparationForWeaponWithNoWorthwhileFollowUpShot_DoesNotQualify()
    {
        Fixture fixture = CreateFixture(quarryX: 400);
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();
        ReadyRangedWeaponAction ready = new(shooter, weapon);

        ExecuteAsHoldPair(fixture, ready);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(ready.Succeeded);
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.False(activity.FireCommitmentRemainsViable);
        Assert.Equal(ContactBreakResult.OrganizedForceDisengages, EvaluateContact(activity).Decision);
    }

    [Fact]
    public void ReloadForWeaponWithNoWorthwhileFollowUpShot_DoesNotQualify()
    {
        Fixture fixture = CreateFixture(quarryX: 400);
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        weapon.LoadedAmmo = 0;
        weapon.ReserveAmmo = weapon.Template.AmmoCapacity;
        ReloadRangedWeaponAction reload = new(shooter, weapon);

        ExecuteAsHoldPair(fixture, reload);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.True(reload.Succeeded);
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.False(activity.FireCommitmentRemainsViable);
    }

    [Fact]
    public void TargetReassignmentOnNextRound_InvalidatesOldPreparationCommitment()
    {
        Fixture fixture = CreateFixture();
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();
        ReadyRangedWeaponAction ready = new(shooter, weapon);

        ExecuteAsHoldPair(fixture, ready);
        Assert.True(Assert.Single(fixture.BuildActivities()).HasQualifyingFireCycleProgress);

        // Preparation is round-local. A new planning pass may assign the squad elsewhere, but it
        // cannot inherit the old pair's evidence after the completed round has been committed.
        fixture.Metrics.RecordRound([]);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        fixture.Service.ReplaceCurrentTurnPursuitPairings([
            new KeyValuePair<int, int>(fixture.Pursuer.Id, fixture.OtherQuarry.Id)]);

        PursuitPairActivity reassigned = Assert.Single(fixture.BuildActivities());
        Assert.Equal(fixture.OtherQuarry.Id, reassigned.QuarrySquadId);
        Assert.False(reassigned.FireCycleProgressedThisTurn);
        Assert.False(reassigned.FireCommitmentRemainsViable);
    }

    [Fact]
    public void PreparationDoesNotLoopWhenReloadStopsChangingWeaponState()
    {
        Fixture fixture = CreateFixture();
        BattleSoldier shooter = fixture.Pursuer.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        weapon.LoadedAmmo = 0;
        weapon.ReserveAmmo = weapon.Template.AmmoCapacity;
        ReloadRangedWeaponAction firstReload = new(shooter, weapon);

        ExecuteAsHoldPair(fixture, firstReload);
        Assert.True(Assert.Single(fixture.BuildActivities()).HasQualifyingFireCycleProgress);

        // The next attempted reload is not an executed state-changing preparation, and the
        // current-round evidence has no timer that could keep the pair alive.
        fixture.Metrics.RecordRound([]);
        ReloadRangedWeaponAction repeatedReload = new(shooter, weapon);
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        fixture.Service.ReplaceCurrentTurnPursuitPairings([
            new KeyValuePair<int, int>(fixture.Pursuer.Id, fixture.Quarry.Id)]);
        repeatedReload.Execute(fixture.State);
        fixture.Metrics.RecordRound([repeatedReload]);

        PursuitPairActivity activity = Assert.Single(fixture.BuildActivities());
        Assert.False(repeatedReload.Succeeded);
        Assert.False(activity.FireCycleProgressedThisTurn);
        Assert.False(activity.FireCommitmentRemainsViable);
    }

    private static void ExecuteAsHoldPair(Fixture fixture, IAction preparation)
    {
        fixture.Pursuer.LastEngagementOptionKind = EngagementOptionKind.Hold;
        fixture.Service.ReplaceCurrentTurnPursuitPairings([
            new KeyValuePair<int, int>(fixture.Pursuer.Id, fixture.Quarry.Id)]);
        preparation.Execute(fixture.State);
        fixture.Metrics.RecordRound([preparation]);
    }

    private static BattleContactRules.Result EvaluateContact(PursuitPairActivity activity) =>
        BattleContactRules.Evaluate(new BattleContactRules.Input(
            Turn: 1,
            IsFirstSide: false,
            ActivePursuerCount: 1,
            AllPursuersBreakOff: false,
            EnemyAlsoWithdrawing: false,
            PursuitPairs: [activity],
            RearGuardActive: false,
            MaskedDepartureProgress: 0,
            WithdrawingSquadRunAllowance: 6,
            HasImmediateDisengagementCapability: false));

    private static Fixture CreateFixture(int quarryX = 8)
    {
        BattleSquad pursuerSeed = CreateSquad("Pursuer", 94_001);
        BattleSquad quarrySeed = CreateSquad("Quarry", 94_002);
        BattleSquad otherQuarrySeed = CreateSquad("Other Quarry", 94_003);
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
        Place(grid, pursuer.Soldiers[0], true, 0);
        Place(grid, quarry.Soldiers[0], false, quarryX);
        Place(grid, otherQuarry.Soldiers[0], false, 16);

        GameRulesData rules = OnlyWar.Persistence.Database.GameRules.GameRulesLoader.Load(
            RulesDatabaseFixture.DatabasePath);
        FixedRNG random = new();
        BattleAftermathDependencies aftermath = new(
            new Date(1, 1, 1),
            random,
            NoOpPlayerBattleAftermathSink.Instance);
        BattleExecutionContext execution = new(rules, random, aftermath);
        BattleRoundMetrics metrics = new(state);
        BattleMoraleService morale = new(
            state,
            grid,
            execution,
            state.AllAttackerSquads.Values.Concat(state.AllOpposingSquads.Values));
        BattleWithdrawalService service = new(state, grid, rules, metrics, morale);
        return new(state, grid, service, metrics, pursuer, quarry, otherQuarry);
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

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool side, int x)
    {
        soldier.TopLeft = (x, 0);
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

    private sealed class NoOpPlayerBattleAftermathSink : IPlayerBattleAftermathSink
    {
        public static NoOpPlayerBattleAftermathSink Instance { get; } = new();

        public void MoveToFallenBrothers(PlayerSoldier soldier) { }
        public void AddRecoveredGeneseed(float purity) { }
        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
