using System;
using System.Collections.Generic;
using OnlyWar.Battles;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Equippables;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// The aim-versus-shoot rate (RangedTargetSelector.EvaluateFireTiming) and the range projection it
/// rests on. The planner fires when a shot now is worth at least the best per-turn value of
/// aiming first, and prices a planned or stored aim at that same rate.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class PreparedShotRateTests
{
    [Fact]
    public void RangeProjection_FleeingMovesAwayAdvancingClosesHoldingStays()
    {
        Fixture fixture = CreateFixture(distance: 20, targetSpeed: 5);

        fixture.Target.BattleSquad.WithdrawalRole = WithdrawalRole.Bound;
        float fleeing = RangedTargetSelector.ProjectedRangeChangePerTurn(fixture.Target);
        fixture.Target.BattleSquad.WithdrawalRole = WithdrawalRole.None;
        fixture.Target.BattleSquad.LastEngagementOptionKind = EngagementOptionKind.RunToward;
        float advancing = RangedTargetSelector.ProjectedRangeChangePerTurn(fixture.Target);
        fixture.Target.BattleSquad.LastEngagementOptionKind = EngagementOptionKind.Hold;
        float holding = RangedTargetSelector.ProjectedRangeChangePerTurn(fixture.Target);

        Assert.Equal(5f, fleeing);
        Assert.Equal(-5f, advancing);
        Assert.Equal(0f, holding);
    }

    [Fact]
    public void AimingIsWorthMoreAgainstATargetClosingInThanOneRunningAway()
    {
        // Same range, same speed, opposite direction. Every turn spent aiming gives a runner back
        // range and brings a closing target nearer, so the value of waiting must split.
        Fixture closing = CreateFixture(distance: 20, targetSpeed: 5);
        closing.Target.BattleSquad.LastEngagementOptionKind = EngagementOptionKind.RunToward;
        Fixture fleeing = CreateFixture(distance: 20, targetSpeed: 5);
        fleeing.Target.BattleSquad.WithdrawalRole = WithdrawalRole.Bound;

        float closingRate = closing.Rate(currentAimBonus: null);
        float fleeingRate = fleeing.Rate(currentAimBonus: null);

        Assert.True(closingRate > 0, $"expected a worthwhile prepared shot, got {closingRate}");
        Assert.True(
            closingRate > fleeingRate,
            $"closing target rate {closingRate} should exceed fleeing target rate {fleeingRate}");
    }

    [Fact]
    public void FullAimLeavesNothingToWaitFor()
    {
        Fixture fixture = CreateFixture(distance: 20, targetSpeed: 0);

        float rate = fixture.Rate(currentAimBonus: RangedTargetSelector.FullAimBonusTurns);

        Assert.Equal(float.MinValue, rate);
        Assert.Equal(0f, fixture.Timing(currentAimBonus: RangedTargetSelector.FullAimBonusTurns)
            .Readiness);
    }

    [Fact]
    public void ScarceAmmunitionChargesAShotForItsRounds()
    {
        // Same shot, same aimed alternatives; only the pouches differ. With six magazines' worth
        // the rounds are free and firing now is worth its full score. On the last magazine every
        // round must earn what the best round on offer would, so the same burst is worth less.
        Fixture plentiful = CreateFixture(distance: 60, targetSpeed: 0, typedAmmunition: true);
        plentiful.Weapon.ReserveAmmo =
            (RangedTargetSelector.PlentifulMagazines - 1) * plentiful.Weapon.Template.AmmoCapacity;
        Fixture scarce = CreateFixture(distance: 60, targetSpeed: 0, typedAmmunition: true);
        scarce.Weapon.ReserveAmmo = 0;

        RangedTargetEvaluation plentifulShot = plentiful.ShootNow();
        RangedTargetSelector.FireTiming plentifulTiming = plentiful.Timing(null, plentifulShot);
        RangedTargetSelector.FireTiming scarceTiming = scarce.Timing(null, scarce.ShootNow());

        Assert.Equal(0f, RangedTargetSelector.AmmunitionScarcity(plentiful.Weapon));
        Assert.True(RangedTargetSelector.AmmunitionScarcity(scarce.Weapon) > 0.5f);
        Assert.Equal(plentifulShot.Score, plentifulTiming.ShootNowValue, 4);
        Assert.True(
            scarceTiming.ShootNowValue < plentifulTiming.ShootNowValue,
            $"scarce {scarceTiming.ShootNowValue} should be below plentiful "
                + $"{plentifulTiming.ShootNowValue}");
    }

    [Fact]
    public void UntypedReloadIsNeverScarce()
    {
        Fixture fixture = CreateFixture(distance: 20, targetSpeed: 0);
        fixture.Weapon.LoadedAmmo = 1;

        Assert.Equal(0f, RangedTargetSelector.AmmunitionScarcity(fixture.Weapon));
    }

    [Fact]
    public void ARecedingTargetThatLeavesRangeStopsTheAimProjection()
    {
        // Two turns of flight take the target past the weapon's reach, so only the one-turn aim
        // can still be cashed and the rate is that shot over two turns.
        Fixture fixture = CreateFixture(distance: 25, targetSpeed: 5, maximumRange: 32);
        fixture.Target.BattleSquad.WithdrawalRole = WithdrawalRole.Bound;

        float rate = fixture.Rate(currentAimBonus: null);
        RangedTargetEvaluation oneAimTurn = fixture.Selector.EvaluateRangedTarget(
            fixture.Shooter,
            fixture.Target,
            fixture.Weapon,
            30,
            fixture.Weapon.Template.Accuracy + 1);

        Assert.Equal(oneAimTurn.Score / 2, rate, 4);
    }

    private static Fixture CreateFixture(
        int distance,
        float targetSpeed,
        float maximumRange = 200,
        bool typedAmmunition = false)
    {
        const int idBase = 98_400;
        SoldierTemplate shooterTemplate = new(
            idBase + 1,
            TestModelFactory.HumanSpecies,
            "Rate Shooter",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        SoldierTemplate targetTemplate = new(
            idBase + 2,
            TestModelFactory.HumanSpecies,
            "Rate Target",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: 20);
        Soldier shooterModel = TestModelFactory.CreateSoldier(
            shooterTemplate,
            "Rate Shooter",
            dexterity: 14,
            skills: new Skill(TestSkills.Ranged, 4));
        Soldier targetModel = TestModelFactory.CreateSoldier(targetTemplate, "Rate Target");
        shooterModel.Id = idBase + 101;
        targetModel.Id = idBase + 102;

        BattleSquad shooterSquad = new(
            false,
            TestModelFactory.CreateSquad("Rate Shooters", shooterModel));
        BattleSquad targetSquad = new(
            false,
            TestModelFactory.CreateSquad("Rate Targets", targetModel));
        BattleSoldier shooter = shooterSquad.Soldiers[0];
        BattleSoldier target = targetSquad.Soldiers[0];
        target.CurrentSpeed = targetSpeed;
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            idBase + 201,
            "Rate rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 3,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 40,
            maxDistance: maximumRange,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 1,
            doesDamageDegradeWithRange: false,
            reloadTime: 1,
            ammunitionType: typedAmmunition
                ? new AmmunitionType(idBase + 301, "Rate ammunition")
                : null));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(weapon);
        shooter.ReadyWeapon(weapon);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0);
        Place(grid, target, false, distance);
        Dictionary<int, BattleSoldier> soldiers = new()
        {
            [shooter.Soldier.Id] = shooter,
            [target.Soldier.Id] = target
        };
        SquadPlanningServices services = new(
            grid,
            soldiers,
            new Dictionary<int, MeleeWeaponTemplate>(),
            log: null,
            new BattlePlanningContext());
        return new Fixture(
            shooter,
            target,
            weapon,
            distance,
            new RangedTargetSelector(new RangedTargetingServices(services)));
    }

    private static void Place(BattleGridManager grid, BattleSoldier soldier, bool firstSide, int x)
    {
        soldier.TopLeft = (x, 0);
        grid.PlaceSoldier(soldier, firstSide, [soldier.TopLeft.Value]);
    }

    private sealed record Fixture(
        BattleSoldier Shooter,
        BattleSoldier Target,
        RangedWeapon Weapon,
        float Range,
        RangedTargetSelector Selector)
    {
        internal float Rate(int? currentAimBonus) => Timing(currentAimBonus).PreparedRate;

        internal RangedTargetSelector.FireTiming Timing(
            int? currentAimBonus,
            RangedTargetEvaluation shootNow = null) => Selector.EvaluateFireTiming(
            Shooter,
            Target,
            shootNow,
            Weapon,
            Range,
            currentAimBonus,
            bulkMultiplier: 0,
            aimMultiplier: 1f);

        internal RangedTargetEvaluation ShootNow() =>
            Selector.EvaluateRangedTarget(Shooter, Target, Weapon, Range, 0);
    }
}
