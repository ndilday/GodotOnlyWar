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

public class BlastAttackActionTests
{
    private const float Range = 10f;

    [Fact]
    public void Execute_LandsOnTheAimCellWhenMarginIsNonNegative()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Target")]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);

        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        Assert.False(action.DidScatter);
        Assert.Equal(new ValueTuple<int, int>(10, 0), action.ImpactCell);
        Assert.Contains(2, action.VictimIds);
        Assert.DoesNotContain("goes wide", action.Description());
    }

    [Fact]
    public void Execute_ScattersByMarginAndDirectionWhenMarginIsNegative()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Target")]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);

        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: -4f, directionRoll: 0.25));

        Assert.True(action.DidScatter);
        Assert.Equal(new ValueTuple<int, int>(10, 4), action.ImpactCell);
        Assert.Contains("goes wide, landing 4 cells off target", action.Description());
    }

    [Fact]
    public void Execute_ScalesDamageByQuadraticFalloffFromTheImpactCenter()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Center Victim"), CreateSoldier(3, "Rim Victim")]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.SetArmor(3, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);
        battle.Place(3, false, 15, 0);

        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        // Identical (zero) damage rolls: only the falloff differs between the victims.
        WoundResolution centerWound = action.WoundResolutions
            .Single(wound => wound.Suffererer.Soldier.Id == 2);
        WoundResolution rimWound = action.WoundResolutions
            .Single(wound => wound.Suffererer.Soldier.Id == 3);
        Assert.True(centerWound.Damage > rimWound.Damage);
        Assert.Equal(17.5f, centerWound.Damage);
    }

    [Fact]
    public void Execute_AppliesFalloffBeforeArmorAndArmorPerVictim()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Unarmored"), CreateSoldier(3, "Armored Rim")]);
        battle.SetArmor(1, byte.MaxValue);
        battle.SetArmor(2, 0);
        // Raw roll (17.5) beats this armor, but the rim-scaled roll does not — proving
        // the falloff is applied to the damage roll before armor subtraction.
        battle.SetArmor(3, 10);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);
        battle.Place(3, false, 15, 0);

        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        WoundResolution wound = Assert.Single(action.WoundResolutions);
        Assert.Equal(2, wound.Suffererer.Soldier.Id);
    }

    [Fact]
    public void Execute_AutoHitsHighEvasionGiantsCaughtInTheBlast()
    {
        Soldier elusive = CreateSoldier(2, "Elusive Giant", CreateElusiveTemplate());
        elusive.Size = 1_000;
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [elusive]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);

        // A margin barely above zero: if size or RangedEvasion modified the check,
        // the throw would scatter far away instead of landing on the aim cell.
        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        Assert.False(action.DidScatter);
        Assert.Contains(2, action.VictimIds);
        Assert.Contains(action.WoundResolutions, wound => wound.Suffererer.Soldier.Id == 2);
    }

    [Fact]
    public void Execute_RangeCheckTreatsTheAimPointAsStationary()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Sprinter")]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);
        battle.State.GetSoldier(2).CurrentSpeed = 50f;

        // The expected margin is computed against target speed 0; a moving target
        // would push the range modifier down and force a scatter.
        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        Assert.False(action.DidScatter);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void Execute_ConsumesOneGrenadeAndClampsAtEmpty(int loadedAmmo, int expectedAmmo)
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Target")]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);
        RangedWeapon weapon = GetBlastWeapon(battle.State.GetSoldier(1));
        weapon.LoadedAmmo = (ushort)loadedAmmo;

        battle.ExecuteBlast(1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        Assert.Equal(expectedAmmo, weapon.LoadedAmmo);
    }

    [Fact]
    public void Execute_CancelsAnyAccumulatedAim()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Target")]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);
        BattleSoldier thrower = battle.State.GetSoldier(1);
        thrower.Aim = new ValueTuple<int, RangedWeapon, int>(
            2, GetBlastWeapon(thrower), 2);

        battle.ExecuteBlast(1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        Assert.Null(thrower.Aim);
    }

    [Fact]
    public void Execute_CanWoundTheThrowerOnADangerCloseThrow()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Target")]);
        battle.SetArmor(1, 0);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 4, 0);

        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, 4f, CreateRng(battle, marginZ: 0.5f, range: 4f));

        Assert.Equal(new[] { 1, 2 }, action.VictimIds);
        Assert.True(action.IsFriendlyFire);
        Assert.Equal(new[] { 1 }, action.FriendlyVictimIds);
        Assert.Contains(action.WoundResolutions, wound => wound.Suffererer.Soldier.Id == 1);
        Assert.Contains("Friendly fire", action.Description());
        Assert.Contains("Thrower", action.Description());
    }

    [Fact]
    public void Execute_FlagsFriendlyVictimsCaughtInTheBlast()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower"), CreateSoldier(3, "Friendly Marine")],
            [CreateSoldier(2, "Enemy")]);
        battle.SetArmor(1, byte.MaxValue);
        battle.SetArmor(2, 0);
        battle.SetArmor(3, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(3, true, 12, 0);
        battle.Place(2, false, 10, 0);

        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: 0.5f));

        Assert.True(action.IsFriendlyFire);
        Assert.Contains(3, action.FriendlyVictimIds);
        Assert.Contains("Friendly fire", action.Description());
        Assert.Contains("Friendly Marine", action.Description());
    }

    [Fact]
    public void Execute_ASecondTimeReusesResolutionWithoutAmmoOrTurnChanges()
    {
        TestBattle battle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Target")]);
        battle.SetArmor(1, byte.MaxValue);
        battle.SetArmor(2, 0);
        battle.Place(1, true, 0, 0);
        battle.Place(2, false, 10, 0);
        BlastAttackAction action = battle.ExecuteBlast(
            1, 2, Range, CreateRng(battle, marginZ: 0.5f));
        WoundResolution originalWound = Assert.Single(action.WoundResolutions);
        ValueTuple<int, int> originalImpact = action.ImpactCell;
        RangedWeapon weapon = GetBlastWeapon(battle.State.GetSoldier(1));
        ushort remainingAmmo = weapon.LoadedAmmo;
        ushort turnsShooting = battle.State.GetSoldier(1).TurnsShooting;

        action.Execute(battle.State);

        Assert.Same(originalWound, Assert.Single(action.WoundResolutions));
        Assert.Equal(originalImpact, action.ImpactCell);
        Assert.Equal(remainingAmmo, weapon.LoadedAmmo);
        Assert.Equal(turnsShooting, battle.State.GetSoldier(1).TurnsShooting);
    }

    [Fact]
    public void Description_HurlsThrownBlastsAndFiresLaunchedOnes()
    {
        TestBattle thrownBattle = CreateBattle(
            TestModelFactory.FragGrenadeTemplate,
            [CreateSoldier(1, "Thrower")],
            [CreateSoldier(2, "Target")]);
        thrownBattle.SetArmor(1, byte.MaxValue);
        thrownBattle.SetArmor(2, 0);
        thrownBattle.Place(1, true, 0, 0);
        thrownBattle.Place(2, false, 10, 0);
        BlastAttackAction thrownAction = thrownBattle.ExecuteBlast(
            1, 2, Range, CreateRng(thrownBattle, marginZ: 0.5f));

        RangedWeaponTemplate launcher = CreateGrenadeLauncher();
        TestBattle launchedBattle = CreateBattle(
            launcher,
            [CreateSoldier(1, "Grenadier")],
            [CreateSoldier(2, "Target")]);
        launchedBattle.SetArmor(1, byte.MaxValue);
        launchedBattle.SetArmor(2, 0);
        launchedBattle.Place(1, true, 0, 0);
        launchedBattle.Place(2, false, 10, 0);
        RangedWeapon launcherWeapon = GetBlastWeapon(launchedBattle.State.GetSoldier(1));
        BlastAttackAction launchedAction = launchedBattle.ExecuteBlast(
            1, 2, Range, CreateRng(launchedBattle, marginZ: 0.5f));

        Assert.Contains("Thrower hurls a Test Frag Grenade at Target", thrownAction.Description());
        Assert.Contains("Grenadier fires a Test Grenade Launcher at Target", launchedAction.Description());
        Assert.Equal(11, launcherWeapon.LoadedAmmo);
    }

    /// <summary>
    /// Builds a scripted RNG whose first Z value produces the requested check margin
    /// (all later rolls are zero), so tests control scatter without statistical fuzz.
    /// </summary>
    private static ScriptedRNG CreateRng(
        TestBattle battle,
        float marginZ,
        double directionRoll = 0.0,
        float range = Range)
    {
        BattleSoldier shooter = battle.State.GetSoldier(1);
        RangedWeapon weapon = GetBlastWeapon(shooter);
        float skill = shooter.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
        float modifier = BattleModifiersUtil.CalculateBlastRangeModifier(
            shooter.Soldier, weapon.Template, range);
        double scatterZ = (skill + modifier - 10.5f - marginZ) / 3.0;
        return new ScriptedRNG(scatterZ, directionRoll);
    }

    // Thrown grenades ride the belt (RangedWeapons) without being equipped;
    // launched blasts are ordinary equipped guns — search both, like the action does.
    private static RangedWeapon GetBlastWeapon(BattleSoldier soldier) =>
        soldier.EquippedRangedWeapons.Concat(soldier.RangedWeapons)
            .Distinct()
            .Single(weapon => weapon.Template.IsBlastWeapon);

    private static RangedWeaponTemplate CreateGrenadeLauncher()
    {
        return new RangedWeaponTemplate(
            98,
            "Test Grenade Launcher",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            0,
            1,
            1,
            0,
            6,
            1_000,
            1,
            12,
            0,
            2,
            false,
            3,
            2,
            6,
            0);
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

    /// <summary>
    /// Deterministic RNG scripted for one blast resolution: the first Z value is the
    /// scatter roll, the first linear double the scatter direction; every subsequent
    /// roll (hit locations, damage) returns the distribution's fixed point.
    /// </summary>
    private sealed class ScriptedRNG(double scatterZ, double directionRoll) : IRNG
    {
        private bool _scatterRolled;
        private bool _directionRolled;

        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;

        public double GetLinearDouble()
        {
            if (_directionRolled)
            {
                return 0.0;
            }

            _directionRolled = true;
            return directionRoll;
        }

        public int GetIntBelowMax(int min, int max) => min;

        public double NextRandomZValue()
        {
            if (_scatterRolled)
            {
                return 0.0;
            }

            _scatterRolled = true;
            return scatterZ;
        }
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

        public BlastAttackAction ExecuteBlast(
            int shooterId,
            int targetId,
            float range,
            ScriptedRNG rng)
        {
            int weaponId = GetBlastWeapon(State.GetSoldier(shooterId)).Template.Id;
            BlastAttackAction action = new(
                shooterId,
                targetId,
                weaponId,
                range,
                useBulk: false,
                Grid,
                rng);
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
