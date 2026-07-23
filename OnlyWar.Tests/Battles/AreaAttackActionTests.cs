using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Helpers.Battles.Resolutions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class AreaAttackActionTests
{
    [Fact]
    public void Execute_AutoHitsTargetsWithoutSizeOrEvasionModifiers()
    {
        RangedWeaponTemplate flamer = CreateFlamer(damageMultiplier: 100);
        Soldier shooter = CreateSoldier(1, "Shooter");
        Soldier normal = CreateSoldier(2, "Normal Target");
        Soldier elusive = CreateSoldier(3, "Elusive Giant", CreateElusiveTemplate());
        elusive.Size = 1_000;
        TestBattle battle = CreateBattle(flamer, [shooter], [normal, elusive]);
        battle.SetArmor(2, 0);
        battle.SetArmor(3, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 5, 0);
        battle.Place(3, false, 6, 1);
        AreaAttackAction action = battle.ExecuteAreaAttack(1, 2, seed: 1);

        Assert.Equal(1_000, battle.State.GetSoldier(3).Soldier.Size);
        Assert.Equal(1_000, battle.State.GetSoldier(3).Soldier.Template.Species.RangedEvasion);
        Assert.Equal(new[] { 2, 3 }, action.VictimIds);
        Assert.Equal(new[] { 2, 3 },
            action.WoundResolutions.Select(wound => wound.Suffererer.Soldier.Id));
    }

    [Fact]
    public void Execute_StillAppliesArmorPerVictim()
    {
        RangedWeaponTemplate flamer = CreateFlamer(damageMultiplier: 1);
        TestBattle battle = CreateBattle(
            flamer,
            [CreateSoldier(1, "Shooter")],
            [CreateSoldier(2, "Unarmored"), CreateSoldier(3, "Armored")]);
        battle.SetArmor(2, 0);
        battle.SetArmor(3, byte.MaxValue);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 5, 0);
        battle.Place(3, false, 6, 1);
        AreaAttackAction action = battle.ExecuteAreaAttack(1, 2, seed: 2);

        Assert.Equal(new[] { 2, 3 }, action.VictimIds);
        WoundResolution wound = Assert.Single(action.WoundResolutions);
        Assert.Equal(2, wound.Suffererer.Soldier.Id);
        Assert.Contains(
            "Hitting 2 times, with 1 hit doing no damage",
            action.Description());
    }

    [Theory]
    [InlineData(25, 15)]
    [InlineData(5, 0)]
    public void Execute_ConsumesFuelAndClampsAnEmptyTank(int loadedFuel, int expectedFuel)
    {
        RangedWeaponTemplate flamer = CreateFlamer(fuelPerBurst: 10);
        TestBattle battle = CreateBattle(
            flamer,
            [CreateSoldier(1, "Shooter")],
            [CreateSoldier(2, "Target")]);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 5, 0);
        RangedWeapon weapon = battle.State.GetSoldier(1).EquippedRangedWeapons.Single();
        weapon.LoadedAmmo = (ushort)loadedFuel;

        battle.ExecuteAreaAttack(1, 2, seed: 1);

        Assert.Equal(expectedFuel, weapon.LoadedAmmo);
    }

    [Fact]
    public void Execute_WoundsAndFlagsFriendlyFiguresCaughtInCone()
    {
        RangedWeaponTemplate flamer = CreateFlamer(damageMultiplier: 100);
        TestBattle battle = CreateBattle(
            flamer,
            [CreateSoldier(1, "Shooter"), CreateSoldier(3, "Friendly Marine")],
            [CreateSoldier(2, "Enemy")]);
        battle.SetArmor(2, 0);
        battle.SetArmor(3, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(3, true, 3, 0);
        battle.Place(2, false, 5, 0);
        AreaAttackAction action = battle.ExecuteAreaAttack(1, 2, seed: 3);

        Assert.True(action.IsFriendlyFire);
        Assert.Equal(new[] { 3 }, action.FriendlyVictimIds);
        Assert.Equal(new[] { 2, 3 },
            action.WoundResolutions.Select(wound => wound.Suffererer.Soldier.Id).OrderBy(id => id));
        Assert.Contains("Friendly fire", action.Description());
        Assert.Contains("Friendly Marine", action.Description());
    }

    [Fact]
    public void Execute_ASecondTimeReusesResolutionWithoutFuelOrTurnChanges()
    {
        RangedWeaponTemplate flamer = CreateFlamer(damageMultiplier: 100, fuelPerBurst: 10);
        TestBattle battle = CreateBattle(
            flamer,
            [CreateSoldier(1, "Shooter")],
            [CreateSoldier(2, "Target")]);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 5, 0);
        AreaAttackAction action = battle.ExecuteAreaAttack(1, 2, seed: 4);
        WoundResolution originalWound = Assert.Single(action.WoundResolutions);
        RangedWeapon weapon = battle.State.GetSoldier(1).EquippedRangedWeapons.Single();
        ushort remainingFuel = weapon.LoadedAmmo;
        ushort turnsShooting = battle.State.GetSoldier(1).TurnsShooting;

        action.Execute(battle.State);

        Assert.Same(originalWound, Assert.Single(action.WoundResolutions));
        Assert.Equal(remainingFuel, weapon.LoadedAmmo);
        Assert.Equal(turnsShooting, battle.State.GetSoldier(1).TurnsShooting);
    }

    private static RangedWeaponTemplate CreateFlamer(
        float damageMultiplier = 10,
        ushort fuelPerBurst = 10)
    {
        return new RangedWeaponTemplate(
            99,
            "Test Flamer",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            0,
            1,
            1,
            0,
            damageMultiplier,
            10,
            1,
            50,
            0,
            1,
            false,
            1,
            1,
            3,
            fuelPerBurst);
    }

    private static Soldier CreateSoldier(
        int id,
        string name,
        SoldierTemplate template = null)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(template: template, name: name);
        soldier.Id = id;
        return soldier;
    }

    private static SoldierTemplate CreateElusiveTemplate()
    {
        static NormalizedValueTemplate Value(float value) => new()
        {
            BaseValue = value,
            StandardDeviation = 0
        };

        Species species = new(
            99,
            "Extremely Elusive",
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(0),
            Value(10),
            Value(6),
            Value(1),
            1,
            1,
            0,
            1_000,
            SpeciesAbilities.None,
            HumanBodyTemplate.Instance,
            TestModelFactory.DefaultUnarmedWeapon);
        return new SoldierTemplate(
            99,
            species,
            "Elusive Target",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>());
    }

    private static TestBattle CreateBattle(
        RangedWeaponTemplate weaponTemplate,
        Soldier[] firstSide,
        Soldier[] secondSide)
    {
        BattleSquad attackers = new(
            true,
            TestModelFactory.CreateSquad("Attackers", firstSide));
        BattleSquad opponents = new(
            false,
            TestModelFactory.CreateSquad("Opponents", secondSide));

        foreach (BattleSoldier soldier in attackers.Soldiers.Concat(opponents.Soldiers))
        {
            // BattleState snapshots require placed battle coordinates during cloning. The
            // concrete grid positions are assigned by each test immediately afterward.
            soldier.TopLeft = new ValueTuple<int, int>(0, 0);
            soldier.ClearReadiedRangedWeapons();
            soldier.RangedWeapons.Clear();
            soldier.AddWeapons([new RangedWeapon(weaponTemplate)], []);
        }

        BattleState state = new(
            new Dictionary<int, BattleSquad> { [attackers.Id] = attackers },
            new Dictionary<int, BattleSquad> { [opponents.Id] = opponents });
        return new TestBattle(attackers, opponents, state);
    }

    private sealed class TestBattle(
        BattleSquad attackers,
        BattleSquad opponents,
        BattleState state)
    {
        public BattleGridManager Grid { get; } = new();
        public BattleState State { get; } = state;

        public void SetArmor(int soldierId, byte armor)
        {
            FindOriginal(soldierId).Armor = new Armor(
                new ArmorTemplate(100 + soldierId, $"Armor {armor}", armor, 0));
            State.GetSoldier(soldierId).Armor = FindOriginal(soldierId).Armor;
        }

        public void Place(int soldierId, bool side, int x, int y)
        {
            BattleSoldier soldier = FindOriginal(soldierId);
            soldier.TopLeft = new ValueTuple<int, int>(x, y);
            State.GetSoldier(soldierId).TopLeft = soldier.TopLeft;
            Grid.PlaceSoldier(
                soldier,
                side,
                [new ValueTuple<int, int>(x, y)]);
        }

        public AreaAttackAction ExecuteAreaAttack(int shooterId, int targetId, int seed)
        {
            int weaponId = State.GetSoldier(shooterId)
                .EquippedRangedWeapons.Single().Template.Id;
            AreaAttackAction action = new(
                shooterId,
                targetId,
                weaponId,
                Grid,
                new SeededRNG(seed));
            action.Execute(State);
            return action;
        }

        private BattleSoldier FindOriginal(int soldierId)
        {
            return attackers.Soldiers.Concat(opponents.Soldiers)
                .Single(soldier => soldier.Soldier.Id == soldierId);
        }
    }
}
