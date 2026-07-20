using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class HandGroupTests
{
    [Fact]
    public void FunctioningHands_CountsGroupsRatherThanLocations()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        HitLocation leftArm = Find(soldier, "Left Arm");
        HitLocation leftHand = Find(soldier, "Left Hand");
        HitLocation rightHand = Find(soldier, "Right Hand");

        Assert.Equal(2, soldier.FunctioningHands);
        Assert.True(soldier.CanUseTwoHandedWeapon);

        Cripple(leftArm);
        Cripple(leftHand);

        Assert.Equal([1], soldier.FunctioningHandGroupIds);
        Assert.Equal(1, soldier.FunctioningHands);
        Assert.True(soldier.CanFight);
        Assert.False(soldier.CanUseTwoHandedWeapon);

        Cripple(rightHand);

        Assert.Empty(soldier.FunctioningHandGroupIds);
        Assert.False(soldier.CanFight);
    }

    [Fact]
    public void ReplacingBody_RebuildsHandGroupCache()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();

        soldier.Body = new Body([]);

        Assert.Empty(soldier.FunctioningHandGroupIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisablingHandGroup_DropsOnlyWeaponHeldByThatGroup(bool pistolInDisabledGroup)
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);
        RangedWeapon pistol = new(CreatePistol());
        MeleeWeapon sword = new(TestModelFactory.DefaultWeapons.PrimaryMeleeWeapon);
        int pistolGroup = pistolInDisabledGroup ? 0 : 1;
        int swordGroup = pistolInDisabledGroup ? 1 : 0;
        Assert.True(battleSoldier.ReadyWeapon(pistol, [pistolGroup]));
        Assert.True(battleSoldier.ReadyWeapon(sword, [swordGroup]));

        WoundResolver resolver = new();
        resolver.WoundQueue.Add(new WoundResolution(
            null,
            null,
            battleSoldier,
            float.MaxValue,
            Find(soldier, "Left Arm")));

        resolver.Resolve();

        if (pistolInDisabledGroup)
        {
            Assert.Empty(battleSoldier.EquippedRangedWeapons);
            Assert.Equal(sword, Assert.Single(battleSoldier.EquippedMeleeWeapons));
            Assert.Equal([1], battleSoldier.GetHandGroupIds(sword));
        }
        else
        {
            Assert.Equal(pistol, Assert.Single(battleSoldier.EquippedRangedWeapons));
            Assert.Equal([1], battleSoldier.GetHandGroupIds(pistol));
            Assert.Empty(battleSoldier.EquippedMeleeWeapons);
        }
    }

    [Fact]
    public void DisablingEitherGrip_DropsTwoHandedWeapon()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);
        RangedWeapon rifle = new(TestModelFactory.DefaultWeapons.PrimaryRangedWeapon);
        Assert.True(battleSoldier.ReadyWeapon(rifle, [0, 1]));

        WoundResolver resolver = new();
        resolver.WoundQueue.Add(new WoundResolution(
            null,
            null,
            battleSoldier,
            float.MaxValue,
            Find(soldier, "Right Hand")));

        resolver.Resolve();

        Assert.Empty(battleSoldier.EquippedRangedWeapons);
        Assert.Equal(1, battleSoldier.FunctioningHands);
        Assert.Equal([0], battleSoldier.FunctioningHandGroupIds);
    }

    [Fact]
    public void WoundResolution_RefreshesCachedBattleInjuryState()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier battleSoldier = new(soldier, null);
        Assert.True(battleSoldier.CanFight);
        Assert.False(battleSoldier.IsSlow);

        WoundResolver resolver = new();
        resolver.OnSoldierFall += (_, _) => { };
        resolver.WoundQueue.Add(new WoundResolution(
            null,
            null,
            battleSoldier,
            float.MaxValue,
            Find(soldier, "Left Leg")));

        resolver.Resolve();

        Assert.False(battleSoldier.CanFight);
        Assert.True(battleSoldier.IsSlow);
        Assert.Equal(soldier.MoveSpeed * 0.75f, battleSoldier.GetMoveSpeed());
    }

    [Fact]
    public void SharedSoldierInjury_RefreshesEveryBattleWrapper()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        BattleSoldier retainedWrapper = new(soldier, null);
        BattleSoldier activeWrapper = new(retainedWrapper, null);
        Assert.True(retainedWrapper.CanFight);

        WoundResolver resolver = new();
        resolver.OnSoldierFall += (_, _) => { };
        resolver.WoundQueue.Add(new WoundResolution(
            null,
            null,
            activeWrapper,
            float.MaxValue,
            Find(soldier, "Left Leg")));

        resolver.Resolve();

        Assert.False(activeWrapper.CanFight);
        Assert.False(retainedWrapper.CanFight);
        Assert.True(retainedWrapper.IsSlow);
    }

    [Fact]
    public void TwoHandedWeapon_CannotBeReadiedWithOneFunctioningHand()
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        Cripple(Find(soldier, "Left Hand"));
        BattleSoldier battleSoldier = new(soldier, null);
        RangedWeapon rifle = new(TestModelFactory.DefaultWeapons.PrimaryRangedWeapon);

        bool readied = battleSoldier.ReadyWeapon(rifle);

        Assert.False(readied);
        Assert.Empty(battleSoldier.EquippedRangedWeapons);
        Assert.Equal(1, battleSoldier.HandsFree);
    }

    private static HitLocation Find(Soldier soldier, string name)
    {
        return soldier.Body.HitLocations.Single(location => location.Template.Name == name);
    }

    private static void Cripple(HitLocation location)
    {
        location.Wounds.AddWound(WoundLevel.Critical);
    }

    private static RangedWeaponTemplate CreatePistol()
    {
        return new RangedWeaponTemplate(
            99,
            "Test Pistol",
            EquipLocation.OneHand,
            TestSkills.Ranged,
            0,
            1,
            1,
            0,
            3,
            30,
            1,
            6,
            0,
            0,
            false,
            1);
    }
}
