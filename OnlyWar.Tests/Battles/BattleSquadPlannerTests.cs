using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleSquadPlannerTests
{
    private sealed class EngagedDecisionScenario
    {
        public BattleSquad ShooterSquad { get; init; }
        public BattleSoldier Shooter { get; init; }
        public IReadOnlyList<BattleSoldier> Attackers { get; init; }
        public MeleeWeapon ProjectedMeleeWeapon { get; init; }
        public BattleSquadPlanner Planner { get; init; }
        public List<IAction> ShootActions { get; init; }
        public List<IAction> MeleeActions { get; init; }
    }

    private static BattleSquad CreateSquad(
        string name,
        int soldierId,
        int battleValue = 2,
        float size = 1)
    {
        SoldierTemplate template = new(
            10_000 + soldierId,
            TestModelFactory.HumanSpecies,
            $"{name} Template",
            1,
            1,
            false,
            0,
            Array.Empty<ValueTuple<BaseSkill, float>>(),
            battleValue: battleValue);
        Soldier soldier = TestModelFactory.CreateSoldier(template, name);
        soldier.Id = soldierId;
        soldier.Size = size;
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldier));
    }

    private static BattleSquad CreateSquad(
        string name,
        params (int SoldierId, int BattleValue)[] members)
    {
        List<Soldier> soldiers = members
            .Select(member =>
            {
                SoldierTemplate template = new(
                    20_000 + member.SoldierId,
                    TestModelFactory.HumanSpecies,
                    $"{name} {member.SoldierId} Template",
                    1,
                    1,
                    false,
                    0,
                    Array.Empty<ValueTuple<BaseSkill, float>>(),
                    battleValue: member.BattleValue);
                Soldier soldier = TestModelFactory.CreateSoldier(
                    template,
                    $"{name} {member.SoldierId}");
                soldier.Id = member.SoldierId;
                return soldier;
            })
            .ToList();
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldiers.ToArray()));
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = (x, y);
        grid.PlaceSoldier(soldier, side, [new ValueTuple<int, int>(x, y)]);
    }

    private static BattleSquadPlanner CreatePlanner(
        BattleGridManager grid,
        params BattleSquad[] squads)
    {
        return CreatePlanner(
            grid,
            new List<IAction>(),
            new List<IAction>(),
            new List<IAction>(),
            squads);
    }

    private static BattleSquadPlanner CreatePlanner(
        BattleGridManager grid,
        ICollection<IAction> shootActions,
        ICollection<IAction> moveActions,
        ICollection<IAction> meleeActions,
        params BattleSquad[] squads)
    {
        Dictionary<int, BattleSoldier> soldiers = squads
            .SelectMany(squad => squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        return new BattleSquadPlanner(
            grid,
            soldiers,
            shootActions,
            moveActions,
            meleeActions,
            null,
            CreateMeleeTemplateMap(soldiers.Values),
            new SeededRNG(12345));
    }

    private static RangedWeapon EquipTemplateWeapon(
        BattleSoldier soldier,
        float areaRadius = 5,
        float maximumRange = 30)
    {
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            99_200,
            "Test Flamer",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 5,
            maxDistance: maximumRange,
            rof: 1,
            ammo: 50,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 3,
            templateType: 1,
            areaRadius: areaRadius,
            fuelPerBurst: 10));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(weapon);
        soldier.ReadyWeapon(weapon);
        return weapon;
    }

    private static RangedWeapon EquipAimTestRifle(BattleSoldier soldier, int templateId)
    {
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            templateId,
            "Aim Test Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 6,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 100,
            maxDistance: 100,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 4,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(rifle);
        soldier.ReadyWeapon(rifle);
        return rifle;
    }

    [Fact]
    public void CalculateOpeningDistance_HitLimitedHeavyWeaponOpensFarWhileOptimalIsZero()
    {
        // A single-shot heavy weapon (missile-launcher-like) can wound at any range but rarely
        // hits a small target at range in ordinary hands, so CalculateOptimalDistance collapses
        // to 0 ("no standoff range"). The opening-range variant recognizes this is hit-limited,
        // not wound-limited, and keeps the squad opening far to take its low-odds shots rather
        // than being dragged toward a close start where its bulk and single shot are wasted.
        BattleSquad squad = CreateSquad("Missile Gunner", 91_001);
        BattleSoldier shooter = squad.Soldiers[0];
        RangedWeapon launcher = new(new RangedWeaponTemplate(
            99_301,
            "Missile Launcher",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 0.5f,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 100,
            maxDistance: 500,
            rof: 1,
            ammo: 4,
            recoil: 0,
            bulk: 8,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(launcher);
        shooter.ReadyWeapon(launcher);

        float optimal = BattleModifiersUtil.CalculateOptimalDistance(shooter, 1f, 15f, 30f);
        float opening = BattleModifiersUtil.CalculateOpeningDistance(shooter, 1f, 15f, 30f);

        Assert.Equal(0f, optimal);
        Assert.True(opening > 0f, $"expected hit-limited weapon to open far, got {opening}");
    }

    [Fact]
    public void CalculateOpeningDistance_WoundLimitedWeaponStaysCloseLikeOptimal()
    {
        // A weapon that hits fine but cannot wound the target at any range gains nothing by
        // standing off, so both the optimal and opening distances are 0 (open/stay close, where
        // a rush or lucky penetration is at least possible). This is the case that must NOT be
        // pushed outward by the hit-limited exception.
        BattleSquad squad = CreateSquad("Light Gunner", 91_002);
        BattleSoldier shooter = squad.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20; // accurate enough to hit at range
        RangedWeapon popgun = new(new RangedWeaponTemplate(
            99_302,
            "Popgun",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 5,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 2,
            maxDistance: 300,
            rof: 3,
            ammo: 30,
            recoil: 0,
            bulk: 4,
            doesDamageDegradeWithRange: true,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(popgun);
        shooter.ReadyWeapon(popgun);

        // Armor 30 the 2-damage popgun can never overcome: DamageMultiplier*6 = 12 < 30.
        float optimal = BattleModifiersUtil.CalculateOptimalDistance(shooter, 1f, 30f, 30f);
        float opening = BattleModifiersUtil.CalculateOpeningDistance(shooter, 1f, 30f, 30f);

        Assert.Equal(0f, optimal);
        Assert.Equal(0f, opening);
    }

    [Fact]
    public void PrepareActions_FlamerInRangeSelectsStationaryTier()
    {
        BattleSquad shooters = CreateSquad("Stationary Flamer", 90_001);
        BattleSquad enemies = CreateSquad("Nearby Enemy", 90_002);
        BattleSoldier shooter = shooters.Soldiers[0];
        EquipTemplateWeapon(shooter, maximumRange: 5);
        shooter.LeftoverMovement = 3.25f;
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 3, 0);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, enemies);

        planner.PrepareActions(shooters);

        Assert.Equal(SquadMovementTier.Stationary, shooters.MovementTier);
        Assert.Equal(0, shooter.CurrentSpeed);
        Assert.Equal(0, shooter.LeftoverMovement);
        Assert.IsType<AreaAttackAction>(Assert.Single(shootActions));
    }

    [Fact]
    public void PrepareActions_FlamerOutOfRangeSelectsRunWithoutFiring()
    {
        BattleSquad shooters = CreateSquad("Running Flamer", 90_011);
        BattleSquad enemies = CreateSquad("Distant Enemy", 90_012);
        BattleSoldier shooter = shooters.Soldiers[0];
        EquipTemplateWeapon(shooter, maximumRange: 3);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 20, 0);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, moveActions, [], shooters, enemies);

        planner.PrepareActions(shooters);

        Assert.Equal(SquadMovementTier.Run, shooters.MovementTier);
        Assert.Equal(shooter.GetMoveSpeed(), shooter.CurrentSpeed);
        Assert.Empty(shootActions.OfType<AreaAttackAction>());
        Assert.IsType<MoveAction>(Assert.Single(moveActions));
    }

    [Fact]
    public void PrepareActions_HeavyWeaponInsidePreferredRangeHoldsInsteadOfWalking()
    {
        // A step-back applies half Bulk to every shot that turn. For a Bulk-8 heavy weapon
        // that guts the soldier's firepower, so he should plant and fire (Stationary) rather
        // than kite back the way a light-weapon squad would. Same geometry as
        // PrepareActions_EnemyInsidePreferredRangeSelectsWalk, only the weapon is heavier.
        BattleSquad shooters = CreateSquad("Heavy Gunner", 90_045);
        BattleSquad enemies = CreateSquad("Close Enemy", 90_046);
        BattleSoldier shooter = shooters.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon heavy = new(new RangedWeaponTemplate(
            99_245,
            "Heavy Weapon",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 100,
            maxDistance: 100,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 8,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(heavy);
        shooter.ReadyWeapon(heavy);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 10, 0);
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, [], moveActions, [], shooters, enemies);

        planner.PrepareActions(shooters);

        Assert.Equal(SquadMovementTier.Stationary, shooters.MovementTier);
        Assert.Empty(moveActions);
    }

    [Fact]
    public void PrepareActions_EnemyInsidePreferredRangeSelectsWalk()
    {
        BattleSquad shooters = CreateSquad("Walking Rifle", 90_015);
        BattleSquad enemies = CreateSquad("Close Enemy", 90_016);
        BattleSoldier shooter = shooters.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            99_215,
            "Accurate Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 100,
            maxDistance: 100,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 4,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(rifle);
        shooter.ReadyWeapon(rifle);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 10, 0);
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, [], moveActions, [], shooters, enemies);

        planner.PrepareActions(shooters);

        Assert.Equal(SquadMovementTier.Walk, shooters.MovementTier);
        Assert.Equal(shooter.GetMoveSpeed() / 5f, shooter.CurrentSpeed, precision: 4);
        Assert.IsType<MoveAction>(Assert.Single(moveActions));
    }

    [Fact]
    public void PrepareActions_WalkingShooterAtAimCapFiresInsteadOfContinuingToAim()
    {
        BattleSquad shooters = CreateSquad("Walking Aimed Rifle", 90_041);
        BattleSquad enemies = CreateSquad("Close Aim Target", 90_042);
        BattleSoldier shooter = shooters.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon rifle = EquipAimTestRifle(shooter, 99_241);
        BattleSoldier target = enemies.Soldiers[0];
        shooter.TargetId = target.Soldier.Id;
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, rifle, 3);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, target, false, 10, 0);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, enemies);

        planner.PrepareActions(shooters);

        Assert.Equal(SquadMovementTier.Walk, shooters.MovementTier);
        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(shootActions));
        Assert.Equal(target.Soldier.Id, shot.TargetId);
        Assert.Equal(rifle.Template.Id, shot.WeaponId);
        Assert.Equal(0.5f, shot.AimMultiplier);
    }

    [Fact]
    public void PrepareCoverActions_AimPastCapStillForcesShot()
    {
        BattleSquad shooters = CreateSquad("Overshot Aim Rifle", 90_043);
        BattleSquad enemies = CreateSquad("Overshot Aim Target", 90_044);
        BattleSoldier shooter = shooters.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon rifle = EquipAimTestRifle(shooter, 99_243);
        BattleSoldier target = enemies.Soldiers[0];
        shooter.TargetId = target.Soldier.Id;
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(target.Soldier.Id, rifle, 7);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, target, false, 20, 0);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, enemies);

        planner.PrepareCoverActions(shooters);

        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(shootActions));
        Assert.Equal(target.Soldier.Id, shot.TargetId);
        Assert.Equal(rifle.Template.Id, shot.WeaponId);
    }

    [Fact]
    public void PrepareActions_ClosingSquadWithWorthwhileUnaimedShotSelectsJog()
    {
        BattleSquad shooters = CreateSquad("Jogging Rifle", 90_017);
        BattleSquad enemies = CreateSquad("Far Enemy", 90_018);
        BattleSquad valuableBehindEnemies = CreateSquad(
            "Valuable Enemy Behind",
            90_019,
            battleValue: 10_000);
        BattleSoldier shooter = shooters.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 16;
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            99_217,
            "Mobile Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 100,
            maxDistance: 1_000,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 1,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(rifle);
        shooter.ReadyWeapon(rifle);
        BattleSoldier enemy = enemies.Soldiers[0];
        ((Soldier)enemy.Soldier).Dexterity = 20;
        RangedWeapon longRifle = new(new RangedWeaponTemplate(
            99_218,
            "Long Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 6,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 100,
            maxDistance: 1_000,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        enemy.RangedWeapons.Clear();
        enemy.ClearReadiedRangedWeapons();
        enemy.RangedWeapons.Add(longRifle);
        enemy.ReadyWeapon(longRifle);
        float preferredDistance = BattleModifiersUtil.CalculateOptimalDistance(
            shooter,
            enemies.GetAverageSize(),
            enemies.GetAverageArmor(),
            enemies.GetAverageConstitution(),
            enemies.GetAverageRangedEvasion());
        int enemyX = (int)System.Math.Ceiling(preferredDistance * 1.5f);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, enemyX, 0);
        Place(grid, valuableBehindEnemies.Soldiers[0], false, -enemyX - 1, 0);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid,
            shootActions,
            moveActions,
            [],
            shooters,
            enemies,
            valuableBehindEnemies);

        planner.PrepareActions(shooters);

        Assert.Equal(SquadMovementTier.Jog, shooters.MovementTier);
        Assert.Equal(shooter.GetMoveSpeed() / 2f, shooter.CurrentSpeed, precision: 4);
        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(shootActions));
        Assert.Equal(enemies.Soldiers[0].Soldier.Id, shot.TargetId);
        Assert.Equal(1f, shot.BulkMultiplier);
        Assert.Equal(0f, shot.AimMultiplier);
        Assert.IsType<MoveAction>(Assert.Single(moveActions));
    }

    [Theory]
    [InlineData(0, 4, true)]
    [InlineData(4, 0, true)]
    [InlineData(0, -4, false)]
    public void SelectBestRangedTarget_JogFiringArcIncludesNinetyDegreesButNotBehind(
        int targetX,
        int targetY,
        bool expectsTarget)
    {
        BattleSquad shooters = CreateSquad("Jog Arc Shooter", 90_031);
        BattleSquad enemies = CreateSquad("Jog Arc Target", 90_032);
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, targetX, targetY);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, enemies);

        BattleSquadPlanner.RangedTargetEvaluation target = planner.SelectBestRangedTarget(
            shooter,
            useBulk: true,
            movementDirection: new ValueTuple<int, int>(0, 1));

        Assert.Equal(expectsTarget, target != null);
    }

    [Fact]
    public void PrepareActions_EngagedSquadUsesInMeleeTierAndClosesSeparatedMembers()
    {
        BattleSquad attackers = CreateSquad("Engaging Squad", 90_021);
        BattleSquad enemies = CreateSquad("Melee Target", 90_022);
        BattleSoldier attacker = attackers.Soldiers[0];
        BattleGridManager grid = new();
        Place(grid, attacker, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 3, 0);
        attackers.IsInMelee = true;
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, [], moveActions, [], attackers, enemies);

        planner.PrepareActions(attackers);

        Assert.Equal(SquadMovementTier.InMelee, attackers.MovementTier);
        Assert.Equal(attacker.GetMoveSpeed(), attacker.CurrentSpeed);
        Assert.IsType<MoveAction>(Assert.Single(moveActions));
    }

    private static EngagedDecisionScenario CreateEngagedDecisionScenario(int attackerCount)
    {
        BattleSquad shooterSquad = CreateSquad("Engaged Shooter", 500, battleValue: 10);
        BattleSoldier shooter = shooterSquad.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 17;

        RangedWeapon pointBlankWeapon = new(new RangedWeaponTemplate(
            99_100,
            "Compact Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 3,
            maxDistance: 50,
            rof: 1,
            ammo: 5,
            recoil: 0,
            bulk: 1,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        MeleeWeapon projectedMeleeWeapon = new(new MeleeWeaponTemplate(
            99_101,
            "Parrying Knife",
            EquipLocation.OneHand,
            TestSkills.Melee,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            strengthMultiplier: 0.2f,
            parryMod: 4,
            attackSpeedMultiplier: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(pointBlankWeapon);
        shooter.ReadyWeapon(pointBlankWeapon);
        shooter.MeleeWeapons.Clear();
        shooter.ClearReadiedMeleeWeapons();
        shooter.MeleeWeapons.Add(projectedMeleeWeapon);

        MeleeWeaponTemplate attackerWeaponTemplate = new(
            99_102,
            "Light Claws",
            EquipLocation.OneHand,
            TestSkills.Melee,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            strengthMultiplier: 0.2142857f,
            parryMod: 0,
            attackSpeedMultiplier: 1);

        List<BattleSquad> squads = [shooterSquad];
        List<BattleSoldier> attackers = [];
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        (int X, int Y)[] attackerPositions = [(1, 0), (0, 1), (-1, 0)];
        for (int index = 0; index < attackerCount; index++)
        {
            BattleSquad attackerSquad = CreateSquad(
                $"Attacker {index + 1}",
                510 + index,
                battleValue: 10);
            BattleSoldier attacker = attackerSquad.Soldiers[0];
            MeleeWeapon attackerWeapon = new(attackerWeaponTemplate);
            attacker.RangedWeapons.Clear();
            attacker.ClearReadiedRangedWeapons();
            attacker.MeleeWeapons.Clear();
            attacker.ClearReadiedMeleeWeapons();
            attacker.MeleeWeapons.Add(attackerWeapon);
            attacker.ReadyWeapon(attackerWeapon);
            Place(
                grid,
                attacker,
                false,
                attackerPositions[index].X,
                attackerPositions[index].Y);
            squads.Add(attackerSquad);
            attackers.Add(attacker);
        }

        Dictionary<int, BattleSoldier> soldierMap = squads
            .SelectMany(squad => squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        List<IAction> shootActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = new(
            grid,
            soldierMap,
            shootActions,
            new List<IAction>(),
            meleeActions,
            null,
            CreateMeleeTemplateMap(soldierMap.Values),
            new SeededRNG(12345));
        shooterSquad.IsInMelee = true;
        shooter.IsInMelee = true;

        return new EngagedDecisionScenario
        {
            ShooterSquad = shooterSquad,
            Shooter = shooter,
            Attackers = attackers,
            ProjectedMeleeWeapon = projectedMeleeWeapon,
            Planner = planner,
            ShootActions = shootActions,
            MeleeActions = meleeActions
        };
    }

    private static IReadOnlyDictionary<int, MeleeWeaponTemplate> CreateMeleeTemplateMap(
        IEnumerable<BattleSoldier> soldiers)
    {
        return soldiers
            .SelectMany(soldier => soldier.MeleeWeapons
                .Concat(soldier.EquippedMeleeWeapons)
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }

    [Fact]
    public void SelectBestRangedTarget_PrefersCleanFartherTargetOverEntangledNearTarget()
    {
        BattleSquad shooters = CreateSquad("Shooter", 1);
        BattleSquad allySquad = CreateSquad("Ally", 2, battleValue: 20);
        BattleSquad entangledEnemy = CreateSquad("Entangled", 10);
        BattleSquad cleanEnemy = CreateSquad("Clean", 20);
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier ally = allySquad.Soldiers[0];
        BattleSoldier entangled = entangledEnemy.Soldiers[0];
        BattleSoldier clean = cleanEnemy.Soldiers[0];

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, ally, true, 4, 1);
        Place(grid, entangled, false, 4, 0);
        Place(grid, clean, false, 8, 0);
        BattleSquadPlanner planner = CreatePlanner(
            grid,
            shooters,
            allySquad,
            entangledEnemy,
            cleanEnemy);

        shooter.TargetId = entangled.Soldier.Id;
        BattleSquadPlanner.RangedTargetEvaluation entangledScore = planner.EvaluateRangedTarget(
            shooter,
            entangled,
            shooter.EquippedRangedWeapons[0],
            4,
            0);
        BattleSquadPlanner.RangedTargetEvaluation cleanScore = planner.EvaluateRangedTarget(
            shooter,
            clean,
            shooter.EquippedRangedWeapons[0],
            8,
            0);
        BattleSquadPlanner.RangedTargetEvaluation selected = planner.SelectBestRangedTarget(
            shooter,
            useBulk: false);

        Assert.True(entangledScore.ExpectedFriendlyBattleValueLost > 0);
        Assert.Equal(0, cleanScore.ExpectedFriendlyBattleValueLost);
        Assert.True(cleanScore.Score > entangledScore.Score);
        Assert.Equal(clean.Soldier.Id, selected.Target.Soldier.Id);
    }

    [Fact]
    public void SelectBestRangedTarget_StillShootsLargeHighValueMonsterInMelee()
    {
        BattleSquad shooters = CreateSquad("Shooter", 101);
        BattleSquad allySquad = CreateSquad("Ally", 102, battleValue: 20);
        BattleSquad monsterSquad = CreateSquad("Monster", 110, battleValue: 30, size: 12);
        BattleSquad cleanEnemy = CreateSquad("Clean", 120);
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier ally = allySquad.Soldiers[0];
        BattleSoldier monster = monsterSquad.Soldiers[0];
        BattleSoldier clean = cleanEnemy.Soldiers[0];

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, ally, true, 4, 1);
        Place(grid, monster, false, 4, 0);
        Place(grid, clean, false, 8, 0);
        BattleSquadPlanner planner = CreatePlanner(
            grid,
            shooters,
            allySquad,
            monsterSquad,
            cleanEnemy);

        BattleSquadPlanner.RangedTargetEvaluation monsterScore = planner.EvaluateRangedTarget(
            shooter,
            monster,
            shooter.EquippedRangedWeapons[0],
            4,
            0);
        BattleSquadPlanner.RangedTargetEvaluation selected = planner.SelectBestRangedTarget(
            shooter,
            useBulk: false);

        Assert.True(monsterScore.ExpectedFriendlyBattleValueLost > 0);
        Assert.Equal(monster.Soldier.Id, selected.Target.Soldier.Id);
    }

    [Fact]
    public void SelectBestRangedTarget_ConsidersOnlyThreeNearestInRangeEnemySquads()
    {
        BattleSquad shooters = CreateSquad("Shooter", 201);
        BattleSquad first = CreateSquad("First", 210);
        BattleSquad second = CreateSquad("Second", 220);
        BattleSquad third = CreateSquad("Third", 230);
        BattleSquad fourth = CreateSquad("Fourth", 240, battleValue: 10_000, size: 20);
        BattleSoldier shooter = shooters.Soldiers[0];

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, first.Soldiers[0], false, 2, 0);
        Place(grid, second.Soldiers[0], false, 3, 0);
        Place(grid, third.Soldiers[0], false, 4, 0);
        Place(grid, fourth.Soldiers[0], false, 5, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, first, second, third, fourth);

        BattleSquadPlanner.RangedTargetEvaluation selected = planner.SelectBestRangedTarget(
            shooter,
            useBulk: false);

        Assert.NotEqual(fourth.Soldiers[0].Soldier.Id, selected.Target.Soldier.Id);
        Assert.Contains(
            selected.Target.Soldier.Id,
            new[]
            {
                first.Soldiers[0].Soldier.Id,
                second.Soldiers[0].Soldier.Id,
                third.Soldiers[0].Soldier.Id
            });
    }

    [Fact]
    public void SquadImminence_IsCachedPerAttackerAndTargetSquadForPlannerTurn()
    {
        BattleSquad shooters = CreateSquad("Shooter", 301);
        BattleSquad meleeEnemy = CreateSquad("Melee Enemy", 310);
        meleeEnemy.Soldiers[0].ClearReadiedRangedWeapons();
        meleeEnemy.Soldiers[0].RangedWeapons.Clear();

        BattleGridManager grid = new();
        Place(grid, shooters.Soldiers[0], true, 0, 0);
        Place(grid, meleeEnemy.Soldiers[0], false, 13, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, meleeEnemy);

        float first = planner.GetSquadImminence(shooters, meleeEnemy);
        float second = planner.GetSquadImminence(shooters, meleeEnemy);

        Assert.InRange(first, 0.01f, 0.99f);
        Assert.Equal(first, second);
        Assert.Equal(1, planner.CachedSquadImminenceCount);
    }

    [Fact]
    public void EvaluateRangedTarget_CarriesShotCountUsedByHitProbability()
    {
        BattleSquad shooters = CreateSquad("Shooter", 401);
        BattleSquad enemy = CreateSquad("Enemy", 410);
        BattleSoldier shooter = shooters.Soldiers[0];
        RangedWeapon burstWeapon = new(new RangedWeaponTemplate(
            99_001,
            "Burst Weapon",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 2,
            maxDistance: 50,
            rof: 12,
            ammo: 12,
            recoil: 1,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        shooter.ClearReadiedRangedWeapons();
        shooter.ReadyWeapon(burstWeapon);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemy.Soldiers[0], false, 4, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, enemy);

        BattleSquadPlanner.RangedTargetEvaluation evaluation = planner.EvaluateRangedTarget(
            shooter,
            enemy.Soldiers[0],
            burstWeapon,
            4,
            0);
        float preRollTotal = shooter.Soldier.GetTotalSkillValue(TestSkills.Ranged)
            + BattleModifiersUtil.CalculateRateOfFireModifier(evaluation.ShotsToFire)
            + BattleModifiersUtil.CalculateRangeModifier(4, 0)
            + BattleModifiersUtil.CalculateSizeModifier(enemy.Soldiers[0].Soldier.Size);
        float expectedProbability = GaussianCalculator.ApproximateNormalCDF(
            (preRollTotal - 10.5f) / 3f);

        Assert.InRange(evaluation.ShotsToFire, 1, burstWeapon.LoadedAmmo);
        Assert.Equal(expectedProbability, evaluation.HitProbability, precision: 5);
    }

    [Fact]
    public void EvaluateRangedTarget_ReusesIdenticalEvaluationButSeparatesChangedTargetSpeed()
    {
        BattleSquad shooters = CreateSquad("Shooter", 450);
        BattleSquad enemy = CreateSquad("Enemy", 460);
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier target = enemy.Soldiers[0];
        RangedWeapon weapon = shooter.EquippedRangedWeapons[0];
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, target, false, 4, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, enemy);

        BattleSquadPlanner.RangedTargetEvaluation first = planner.EvaluateRangedTarget(
            shooter,
            target,
            weapon,
            4,
            0);
        BattleSquadPlanner.RangedTargetEvaluation repeated = planner.EvaluateRangedTarget(
            shooter,
            target,
            weapon,
            4,
            0);
        target.CurrentSpeed = 3;
        BattleSquadPlanner.RangedTargetEvaluation movingTarget = planner.EvaluateRangedTarget(
            shooter,
            target,
            weapon,
            4,
            0);

        Assert.Same(first, repeated);
        Assert.NotSame(first, movingTarget);
        Assert.Equal(2, planner.CachedRangedEvaluationCount);
    }

    [Fact]
    public void EngagedShooter_ShootsAgainstOneAttacker_ButReadiesMeleeAgainstThree()
    {
        EngagedDecisionScenario singleAttacker = CreateEngagedDecisionScenario(1);
        singleAttacker.Planner.PrepareActions(singleAttacker.ShooterSquad);

        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(singleAttacker.ShootActions));
        Assert.True(shot.UseBulk);
        Assert.Equal(singleAttacker.Attackers[0].Soldier.Id, shot.TargetId);
        Assert.Empty(singleAttacker.MeleeActions);

        EngagedDecisionScenario threeAttackers = CreateEngagedDecisionScenario(3);
        threeAttackers.Planner.PrepareActions(threeAttackers.ShooterSquad);

        Assert.IsType<ReadyMeleeWeaponAction>(Assert.Single(threeAttackers.ShootActions));
        Assert.Empty(threeAttackers.MeleeActions);
    }

    [Fact]
    public void TemplateWeaponBearer_EmitsAreaAttackWithoutAimingOrShooting()
    {
        BattleSquad shooters = CreateSquad("Flamer Bearer", 600);
        BattleSquad enemies = CreateSquad("Enemy", 610);
        BattleSoldier shooter = shooters.Soldiers[0];
        RangedWeapon flamer = EquipTemplateWeapon(shooter);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 10, 0);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid,
            shootActions,
            moveActions,
            meleeActions,
            shooters,
            enemies);

        planner.PrepareActions(shooters);

        AreaAttackAction action = Assert.IsType<AreaAttackAction>(Assert.Single(shootActions));
        Assert.Equal(shooter.Soldier.Id, action.ShooterId);
        Assert.Equal(enemies.Soldiers[0].Soldier.Id, action.TargetId);
        Assert.Equal(flamer.Template.Id, action.WeaponId);
        Assert.DoesNotContain(shootActions, candidate => candidate is AimAction or ShootAction);
        Assert.Empty(moveActions);
        Assert.Empty(meleeActions);
    }

    [Fact]
    public void TemplateWeaponBearer_PrefersFiringLineThroughDenseEnemyCluster()
    {
        BattleSquad shooters = CreateSquad("Flamer Bearer", 620);
        BattleSquad sparseEnemies = CreateSquad("Sparse Enemy", 630);
        BattleSquad denseEnemies = CreateSquad(
            "Dense Enemies",
            (640, 2),
            (641, 2),
            (642, 2));
        BattleSoldier shooter = shooters.Soldiers[0];
        EquipTemplateWeapon(shooter);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, sparseEnemies.Soldiers[0], false, 10, 0);
        Place(grid, denseEnemies.Soldiers.Single(soldier => soldier.Soldier.Id == 640), false, 0, 10);
        Place(grid, denseEnemies.Soldiers.Single(soldier => soldier.Soldier.Id == 641), false, -1, 10);
        Place(grid, denseEnemies.Soldiers.Single(soldier => soldier.Soldier.Id == 642), false, 1, 10);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid,
            shootActions,
            new List<IAction>(),
            new List<IAction>(),
            shooters,
            sparseEnemies,
            denseEnemies);

        planner.PrepareActions(shooters);

        AreaAttackAction action = Assert.IsType<AreaAttackAction>(Assert.Single(shootActions));
        Assert.Equal(640, action.TargetId);
        BattleSquadPlanner.TemplateFiringLineEvaluation evaluation =
            planner.SelectBestTemplateFiringLine(shooter);
        Assert.Equal(3, evaluation.VictimIds.Count);
        Assert.All(evaluation.VictimIds, victimId =>
            Assert.Contains(victimId, new[] { 640, 641, 642 }));
    }

    [Fact]
    public void TemplateWeaponBearer_ClosesInsteadOfFiringWhenFriendlyCostIsGreater()
    {
        BattleSquad shooters = CreateSquad("Flamer Bearer", 650);
        BattleSquad allies = CreateSquad("Valuable Ally", 651, battleValue: 100);
        BattleSquad enemies = CreateSquad("Enemy", 660, battleValue: 2);
        BattleSoldier shooter = shooters.Soldiers[0];
        EquipTemplateWeapon(shooter);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, allies.Soldiers[0], true, 5, 0);
        Place(grid, enemies.Soldiers[0], false, 10, 0);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid,
            shootActions,
            moveActions,
            new List<IAction>(),
            shooters,
            allies,
            enemies);

        planner.PrepareActions(shooters);

        Assert.Empty(shootActions);
        Assert.IsType<MoveAction>(Assert.Single(moveActions));
        Assert.Null(planner.SelectBestTemplateFiringLine(shooter));
    }

    [Fact]
    public void EngagedTemplateWeaponBearer_UsesAreaAttackForPointBlankShot()
    {
        BattleSquad shooters = CreateSquad("Engaged Flamer Bearer", 670, battleValue: 2);
        BattleSquad enemies = CreateSquad("Adjacent Enemy", 680, battleValue: 10);
        BattleSoldier shooter = shooters.Soldiers[0];
        EquipTemplateWeapon(shooter);
        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 1, 0);
        shooters.IsInMelee = true;
        shooter.IsInMelee = true;
        List<IAction> shootActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid,
            shootActions,
            new List<IAction>(),
            meleeActions,
            shooters,
            enemies);

        planner.PrepareActions(shooters);

        AreaAttackAction action = Assert.IsType<AreaAttackAction>(Assert.Single(shootActions));
        Assert.Equal(enemies.Soldiers[0].Soldier.Id, action.TargetId);
        Assert.Empty(meleeActions);
    }

    [Fact]
    public void ForfeitedParryRisk_AccumulatesAcrossEveryAdjacentAttacker()
    {
        EngagedDecisionScenario singleAttacker = CreateEngagedDecisionScenario(1);
        float singleRisk = singleAttacker.Planner.EstimateForfeitedParryRisk(
            singleAttacker.Shooter,
            singleAttacker.Attackers,
            [singleAttacker.ProjectedMeleeWeapon]);
        EngagedDecisionScenario threeAttackers = CreateEngagedDecisionScenario(3);
        float tripleRisk = threeAttackers.Planner.EstimateForfeitedParryRisk(
            threeAttackers.Shooter,
            threeAttackers.Attackers,
            [threeAttackers.ProjectedMeleeWeapon]);

        Assert.True(singleRisk > 0);
        Assert.Equal(singleRisk * 3, tripleRisk, precision: 4);
    }

    [Fact]
    public void PrepareCoverActions_ParallelPlanningMatchesSerialPlanning()
    {
        var serial = RunParallelPlanningScenario(1);

        for (int repetition = 0; repetition < 20; repetition++)
        {
            var parallel = RunParallelPlanningScenario(8);
            Assert.Equal(serial.Actions, parallel.Actions);
            Assert.Equal(serial.SoldierState, parallel.SoldierState);
        }
    }

    [Fact]
    public void PrepareFollowingActions_ParallelMovingFireMatchesSerialPlanning()
    {
        var serial = RunParallelPlanningScenario(1, pursuit: true);

        for (int repetition = 0; repetition < 20; repetition++)
        {
            var parallel = RunParallelPlanningScenario(8, pursuit: true);
            Assert.Equal(serial.Actions, parallel.Actions);
            Assert.Equal(serial.SoldierState, parallel.SoldierState);
        }
    }

    private static (string[] Actions, string[] SoldierState) RunParallelPlanningScenario(
        int maxDegreeOfParallelism,
        bool pursuit = false)
    {
        BattleSquad shooters = CreateSquad(
            "Parallel Shooters",
            Enumerable.Range(0, 12)
                .Select(index => (80_000 + index, 5))
                .ToArray());
        BattleSquad enemies = CreateSquad(
            "Parallel Targets",
            Enumerable.Range(0, 12)
                .Select(index => (81_000 + index, 5 + index))
                .ToArray());
        BattleGridManager grid = new();
        for (int index = 0; index < shooters.Soldiers.Count; index++)
        {
            BattleSoldier shooter = shooters.Soldiers[index];
            BattleSoldier enemy = enemies.Soldiers[index];
            EquipTemplateWeapon(shooter, areaRadius: 3, maximumRange: 30);
            Place(grid, shooter, true, index * 2, 0);
            Place(grid, enemy, false, index * 2, 10);
            shooter.PrepareForParallelPlanning();
            enemy.PrepareForParallelPlanning();
        }
        shooters.PrepareForParallelPlanning();
        enemies.PrepareForParallelPlanning();

        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        Dictionary<int, BattleSoldier> soldierMap = shooters.Soldiers
            .Concat(enemies.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        BattleSquadPlanner planner = new(
            grid,
            soldierMap,
            shootActions,
            moveActions,
            [],
            null,
            CreateMeleeTemplateMap(soldierMap.Values),
            new SeededRNG(12_345),
            maxDegreeOfParallelism);

        if (pursuit)
        {
            planner.PreparePursuitActions(shooters, PursuitPosture.Follow, [enemies]);
        }
        else
        {
            planner.PrepareCoverActions(shooters);
        }

        string[] actions = shootActions.Select(action => action switch
        {
            AreaAttackAction area =>
                $"Area:{area.ActorId}:{area.TargetId}:{area.WeaponId}",
            ShootAction shot =>
                $"Shoot:{shot.ActorId}:{shot.TargetId}:{shot.WeaponId}:{shot.NumberOfShots}",
            _ => $"{action.GetType().Name}:{action.ActorId}"
        })
            .Concat(moveActions.Select(action => action is MoveAction move
                ? $"Move:{move.ActorId}:{move.Origin}:{move.Destination}"
                : $"{action.GetType().Name}:{action.ActorId}"))
            .ToArray();
        string[] state = shooters.Soldiers.Select(soldier =>
            $"{soldier.Soldier.Id}:{soldier.CurrentSpeed}:{soldier.TargetId}:{soldier.Aim}")
            .ToArray();
        return (actions, state);
    }
}
