using System;
using System.Collections.Generic;
using OnlyWar.Abstractions;
using OnlyWar.Battles.Actions;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using Xunit;

namespace OnlyWar.Tests.Battles;

public sealed class BattleRoundMetricsTests
{
    [Fact]
    public void AttackAgainstAssignedQuarry_IsFound()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);

        ShootAction attack = ExecuteMissedAttack(fixture, fixture.Quarry);
        metrics.RecordRound([attack]);

        Assert.True(metrics.HasPairAttackedRecently(fixture.Pursuer.Id, fixture.Quarry.Id));
    }

    [Fact]
    public void MissedAttackStillCountsAsAttackEvidence()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);

        ShootAction attack = ExecuteMissedAttack(fixture, fixture.Quarry);

        Assert.Equal(0, attack.HitCount);
        metrics.RecordRound([attack]);

        Assert.True(metrics.HasPairAttackedRecently(fixture.Pursuer.Id, fixture.Quarry.Id));
    }

    [Fact]
    public void AttackAgainstAnotherQuarry_DoesNotCountForAssignedPair()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);

        ShootAction attack = ExecuteMissedAttack(fixture, fixture.OtherQuarry);
        metrics.RecordRound([attack]);

        Assert.False(metrics.HasPairAttackedRecently(fixture.Pursuer.Id, fixture.Quarry.Id));
        Assert.True(metrics.HasPairAttackedRecently(fixture.Pursuer.Id, fixture.OtherQuarry.Id));
    }

    [Fact]
    public void NewAimCountsAsFireCycleProgress()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        AimAction aim = CreateAim(fixture, fixture.Quarry);

        aim.Execute(fixture.State);
        metrics.RecordRound([aim]);

        Assert.True(metrics.HasPairFireCycleProgressedThisRound(
            fixture.Pursuer.Id,
            fixture.Quarry.Id));
    }

    [Fact]
    public void AimAgainstAnotherQuarry_DoesNotCountForAssignedPair()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        AimAction aim = CreateAim(fixture, fixture.OtherQuarry);

        aim.Execute(fixture.State);
        metrics.RecordRound([aim]);

        Assert.False(metrics.HasPairFireCycleProgressedThisRound(
            fixture.Pursuer.Id,
            fixture.Quarry.Id));
        Assert.True(metrics.HasPairFireCycleProgressedThisRound(
            fixture.Pursuer.Id,
            fixture.OtherQuarry.Id));
    }

    [Fact]
    public void AdvancedAimCountsAsFireCycleProgress()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        BattleSoldier target = fixture.State.GetSoldier(fixture.Quarry.Soldiers[0].Soldier.Id);
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, weapon, 2);
        AimAction aim = new(shooter, target, weapon, log: null);

        aim.Execute(fixture.State);
        metrics.RecordRound([aim]);

        Assert.Equal(3, shooter.Aim?.Item3);
        Assert.True(metrics.HasPairFireCycleProgressedThisRound(
            fixture.Pursuer.Id,
            fixture.Quarry.Id));
    }

    [Fact]
    public void RetainedButUnexecutedAim_DoesNotCountAsFireCycleProgress()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        BattleSoldier target = fixture.State.GetSoldier(fixture.Quarry.Soldiers[0].Soldier.Id);
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, weapon, 2);

        metrics.RecordRound([]);

        Assert.False(metrics.HasPairFireCycleProgressedThisRound(
            fixture.Pursuer.Id,
            fixture.Quarry.Id));
    }

    [Fact]
    public void SuccessfulReadyAgainstAssignedQuarry_IsRecordedAsPreparationProgress()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();
        ReadyRangedWeaponAction ready = new(shooter, weapon);

        ready.Execute(fixture.State);
        metrics.RecordRound([ready]);

        Assert.True(ready.Succeeded);
        Assert.Contains(
            metrics.GetRangedPreparationProgressThisRound(fixture.Pursuer.Id),
            progress => progress.SoldierId == shooter.Soldier.Id
                && ReferenceEquals(progress.Weapon, weapon));
    }

    [Fact]
    public void UnexecutedReady_DoesNotCountAsPreparationProgress()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        RangedWeapon weapon = shooter.RangedWeapons[0];
        shooter.ClearReadiedRangedWeapons();
        ReadyRangedWeaponAction ready = new(shooter, weapon);

        metrics.RecordRound([ready]);

        Assert.False(ready.Succeeded);
        Assert.Empty(metrics.GetRangedPreparationProgressThisRound(fixture.Pursuer.Id));
    }

    [Fact]
    public void SuccessfulReloadAgainstAssignedQuarry_IsRecordedAsPreparationProgress()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        weapon.LoadedAmmo = 0;
        weapon.ReserveAmmo = weapon.Template.AmmoCapacity;
        ReloadRangedWeaponAction reload = new(shooter, weapon);

        reload.Execute(fixture.State);
        metrics.RecordRound([reload]);

        Assert.True(reload.Succeeded);
        Assert.Contains(
            metrics.GetRangedPreparationProgressThisRound(fixture.Pursuer.Id),
            progress => progress.SoldierId == shooter.Soldier.Id
                && ReferenceEquals(progress.Weapon, weapon));
    }

    [Fact]
    public void RepeatedNoOpPreparation_DoesNotProduceFireCycleProgress()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        weapon.LoadedAmmo = 0;
        weapon.ReserveAmmo = weapon.Template.AmmoCapacity;
        ReloadRangedWeaponAction firstReload = new(shooter, weapon);

        firstReload.Execute(fixture.State);
        metrics.RecordRound([firstReload]);
        Assert.True(firstReload.Succeeded);

        ReloadRangedWeaponAction repeatedReload = new(shooter, weapon);
        repeatedReload.Execute(fixture.State);
        metrics.RecordRound([repeatedReload]);

        Assert.False(repeatedReload.Succeeded);
        Assert.Empty(metrics.GetRangedPreparationProgressThisRound(fixture.Pursuer.Id));
    }

    [Fact]
    public void AttackEvidenceAgesOutAfterRecentActionWindow()
    {
        Fixture fixture = CreateFixture();
        BattleRoundMetrics metrics = new(fixture.State);

        metrics.RecordRound([ExecuteMissedAttack(fixture, fixture.Quarry)]);
        Assert.True(metrics.HasPairAttackedRecently(fixture.Pursuer.Id, fixture.Quarry.Id));

        metrics.RecordRound([]);
        Assert.True(metrics.HasPairAttackedRecently(fixture.Pursuer.Id, fixture.Quarry.Id));

        metrics.RecordRound([]);
        Assert.False(metrics.HasPairAttackedRecently(fixture.Pursuer.Id, fixture.Quarry.Id));
    }

    private static ShootAction ExecuteMissedAttack(Fixture fixture, BattleSquad targetSquad)
    {
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        BattleSoldier target = fixture.State.GetSoldier(targetSquad.Soldiers[0].Soldier.Id);
        ShootAction attack = new(
            shooter.Soldier.Id,
            target.Soldier.Id,
            shooter.EquippedRangedWeapons[0].Template.Id,
            range: 1,
            numberOfShots: 1,
            useBulk: false,
            grid: null,
            random: new GuaranteedMissRng());
        attack.Execute(fixture.State);
        return attack;
    }

    private static AimAction CreateAim(Fixture fixture, BattleSquad targetSquad)
    {
        BattleSoldier shooter = fixture.State.GetSoldier(fixture.Pursuer.Soldiers[0].Soldier.Id);
        BattleSoldier target = fixture.State.GetSoldier(targetSquad.Soldiers[0].Soldier.Id);
        return new AimAction(
            shooter,
            target,
            shooter.EquippedRangedWeapons[0],
            log: null);
    }

    private static Fixture CreateFixture()
    {
        BattleSquad pursuerSeed = CreateSquad("Pursuer");
        BattleSquad quarrySeed = CreateSquad("Quarry");
        BattleSquad otherQuarrySeed = CreateSquad("Other Quarry");
        BattleState state = new(
            new Dictionary<int, BattleSquad> { [pursuerSeed.Id] = pursuerSeed },
            new Dictionary<int, BattleSquad>
            {
                [quarrySeed.Id] = quarrySeed,
                [otherQuarrySeed.Id] = otherQuarrySeed
            });
        return new(
            state,
            state.GetSquad(pursuerSeed.Id),
            state.GetSquad(quarrySeed.Id),
            state.GetSquad(otherQuarrySeed.Id));
    }

    private static BattleSquad CreateSquad(string name)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: $"{name} Soldier");
        Squad squad = TestModelFactory.CreateSquad(name, soldier);
        return new BattleSquad(isPlayerSquad: false, squad);
    }

    private sealed record Fixture(
        BattleState State,
        BattleSquad Pursuer,
        BattleSquad Quarry,
        BattleSquad OtherQuarry);

    private sealed class GuaranteedMissRng : IRNG
    {
        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;

        public double GetLinearDouble() => 0;

        public int GetIntBelowMax(int min, int max) => min;

        public double NextRandomZValue() => 100;
    }
}
