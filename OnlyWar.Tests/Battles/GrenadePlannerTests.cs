using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

// Phase 3 planner coverage for grenades: the grenade is a sidearm whose throw must
// beat the soldier's best conventional action, never aimed, never thrown while
// engaged, Strength-ranged, and restocked through the normal reload branches.
public class GrenadePlannerTests
{
    [Fact]
    public void GrenadeBearer_KeepsTheRifleAgainstALoneChaffTarget()
    {
        BattleSquad shooters = CreateSquad("Grenadier", 700);
        BattleSoldier shooter = shooters.Soldiers[0];
        ArmWithRifleAndBeltGrenade(shooter);
        // Tanky enough that the single-victim grenade payoff trails a confident rifle shot.
        BattleSquad enemies = CreateSquad("Lone Chaff", 710);
        MakeToughSharpshooter(enemies.Soldiers[0], constitution: 40);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 8, 0);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, enemies);

        planner.PrepareActions(shooters);

        BattleSquadPlanner.TemplateFiringLineEvaluation blastThrow =
            planner.SelectBestBlastThrow(shooter);
        BattleSquadPlanner.RangedTargetEvaluation rifleShot =
            planner.SelectBestRangedTarget(shooter, useBulk: false);
        Assert.NotNull(blastThrow);
        Assert.True(rifleShot.Score > blastThrow.Score);
        Assert.DoesNotContain(shootActions, action => action is BlastAttackAction);
        Assert.Contains(shootActions, action => action is ShootAction or AimAction);
    }

    [Fact]
    public void GrenadeBearer_ThrowsAtAClusterWithoutAiming()
    {
        BattleSquad shooters = CreateSquad("Grenadier", 720);
        BattleSoldier shooter = shooters.Soldiers[0];
        ArmWithRifleAndBeltGrenade(shooter);
        BattleSquad enemies = CreateSquad("Cluster", (730, 2), (731, 2), (732, 2), (733, 2));
        foreach (BattleSoldier enemy in enemies.Soldiers)
        {
            // Durable, but not so tanky that a single frag barely dents each: the point is that
            // catching a whole cluster clearly out-values a lone rifle shot. (At constitution 40 a
            // frag's per-victim damage is marginal enough that a confident rifle shot is a defensible
            // choice, so that no longer makes an unambiguous "throw at the cluster" fixture.)
            MakeToughSharpshooter(enemy, constitution: 20);
        }

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 730), false, 10, 0);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 731), false, 11, 0);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 732), false, 10, 1);
        Place(grid, enemies.Soldiers.Single(s => s.Soldier.Id == 733), false, 11, 1);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, enemies);

        planner.PrepareActions(shooters);

        BlastAttackAction blast = Assert.IsType<BlastAttackAction>(Assert.Single(shootActions));
        Assert.Equal(shooter.Soldier.Id, blast.ShooterId);
        Assert.Equal(TestModelFactory.FragGrenadeTemplate.Id, blast.WeaponId);
        Assert.Contains(blast.TargetId, new[] { 730, 731, 732, 733 });
        Assert.DoesNotContain(shootActions, action => action is AimAction or ShootAction);
    }

    [Fact]
    public void GrenadeBearer_RefusesADangerCloseThrowThatCostsMoreThanItRemoves()
    {
        // Same throw, two throwers in isolated scenarios: the valuable soldier's own
        // expected blast loss (danger-close, self included) sinks the score below
        // zero; the expendable one accepts the risk. Distance 2 keeps the thrower
        // well inside the blast circle.
        BattleSquad valuableShooters = CreateSquad("Valuable Grenadier", 740, battleValue: 10);
        ArmWithRifleAndBeltGrenade(valuableShooters.Soldiers[0]);
        BattleSquad firstEnemies = CreateSquad("Close Enemy", 750);
        BattleGridManager firstGrid = new();
        Place(firstGrid, valuableShooters.Soldiers[0], true, 0, 0);
        Place(firstGrid, firstEnemies.Soldiers[0], false, 2, 0);
        BattleSquadPlanner firstPlanner = CreatePlanner(
            firstGrid, [], [], [], valuableShooters, firstEnemies);

        BattleSquad expendableShooters = CreateSquad("Expendable Grenadier", 741, battleValue: 2);
        ArmWithRifleAndBeltGrenade(expendableShooters.Soldiers[0]);
        BattleSquad secondEnemies = CreateSquad("Close Enemy", 751);
        BattleGridManager secondGrid = new();
        Place(secondGrid, expendableShooters.Soldiers[0], true, 0, 0);
        Place(secondGrid, secondEnemies.Soldiers[0], false, 2, 0);
        BattleSquadPlanner secondPlanner = CreatePlanner(
            secondGrid, [], [], [], expendableShooters, secondEnemies);

        Assert.Null(firstPlanner.SelectBestBlastThrow(valuableShooters.Soldiers[0]));
        Assert.NotNull(secondPlanner.SelectBestBlastThrow(expendableShooters.Soldiers[0]));
    }

    [Fact]
    public void GrenadeBearer_RefusesAThrowThatWouldCatchAValuableAlly()
    {
        BattleSquad shooters = CreateSquad("Grenadier", 760);
        BattleSoldier shooter = shooters.Soldiers[0];
        ArmWithRifleAndBeltGrenade(shooter);
        BattleSquad allies = CreateSquad("Valuable Ally", 761, battleValue: 100);
        BattleSquad enemies = CreateSquad("Enemy", 770);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, allies.Soldiers[0], true, 11, 0);
        Place(grid, enemies.Soldiers[0], false, 10, 0);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, allies, enemies);

        planner.PrepareActions(shooters);

        Assert.Null(planner.SelectBestBlastThrow(shooter));
        Assert.DoesNotContain(shootActions, action => action is BlastAttackAction);
    }

    [Fact]
    public void EngagedGrenadeBearer_NeverThrows()
    {
        BattleSquad shooters = CreateSquad("Engaged Grenadier", 780);
        BattleSoldier shooter = shooters.Soldiers[0];
        ArmWithRifleAndBeltGrenade(shooter);
        BattleSquad adjacentEnemies = CreateSquad("Adjacent Enemy", 790);
        // A tempting cluster in easy throwing range must still be ignored while engaged.
        BattleSquad cluster = CreateSquad("Cluster", (791, 2), (792, 2), (793, 2));

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, adjacentEnemies.Soldiers[0], false, 1, 0);
        Place(grid, cluster.Soldiers.Single(s => s.Soldier.Id == 791), false, 10, 0);
        Place(grid, cluster.Soldiers.Single(s => s.Soldier.Id == 792), false, 11, 0);
        Place(grid, cluster.Soldiers.Single(s => s.Soldier.Id == 793), false, 10, 1);
        shooters.IsInMelee = true;
        shooter.IsInMelee = true;
        List<IAction> shootActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], meleeActions, shooters, adjacentEnemies, cluster);

        planner.PrepareActions(shooters);

        Assert.DoesNotContain(shootActions, action => action is BlastAttackAction);
        Assert.DoesNotContain(meleeActions, action => action is BlastAttackAction);
    }

    [Fact]
    public void ThrownRange_ScalesWithTheThrowersStrength()
    {
        BattleSquad marines = CreateSquad("Strong Thrower", 800);
        BattleSquad troopers = CreateSquad("Weak Thrower", 801);
        ((Soldier)marines.Soldiers[0].Soldier).Strength = 15;   // 15 × 3.0 = 45m reach
        ((Soldier)troopers.Soldiers[0].Soldier).Strength = 10;  // 10 × 3.0 = 30m reach
        // Skilled enough arms that the long delivery check has real success odds;
        // an untrained thrower would rightly score a 40-cell lob as worthless.
        ((Soldier)marines.Soldiers[0].Soldier).Dexterity = 22;
        ((Soldier)troopers.Soldiers[0].Soldier).Dexterity = 22;
        AddBeltGrenade(marines.Soldiers[0]);
        AddBeltGrenade(troopers.Soldiers[0]);
        BattleSquad enemies = CreateSquad("Distant Enemy", 810);

        BattleGridManager grid = new();
        Place(grid, marines.Soldiers[0], true, 0, 0);
        Place(grid, troopers.Soldiers[0], true, 0, 1);
        Place(grid, enemies.Soldiers[0], false, 40, 0);
        BattleSquadPlanner planner = CreatePlanner(
            grid, [], [], [], marines, troopers, enemies);

        BattleSquadPlanner.TemplateFiringLineEvaluation marineThrow =
            planner.SelectBestBlastThrow(marines.Soldiers[0]);
        Assert.NotNull(marineThrow);
        Assert.Equal(TestModelFactory.FragGrenadeTemplate.Id, marineThrow.Weapon.Template.Id);
        Assert.Null(planner.SelectBestBlastThrow(troopers.Soldiers[0]));
    }

    [Fact]
    public void BlastThrowScore_FallsWithDeliveryConfidence()
    {
        // Same lone target value, two ranges. The far throw is both less likely to land on
        // target (lower delivery confidence -> more of its value integrated over wider scatter)
        // and less imminent, so it must score strictly lower than the close throw. The old exact
        // "score = nominal x confidence x imminence" factorization no longer holds now that the
        // score integrates enemy value over the full scatter distribution rather than one impact.
        BattleSquad closeShooters = CreateSquad("Close Thrower", 900);
        ((Soldier)closeShooters.Soldiers[0].Soldier).Dexterity = 22;
        AddBeltGrenade(closeShooters.Soldiers[0]);
        BattleSquad closeEnemies = CreateSquad("Close Enemy", 910);
        BattleGridManager closeGrid = new();
        Place(closeGrid, closeShooters.Soldiers[0], true, 0, 0);
        Place(closeGrid, closeEnemies.Soldiers[0], false, 8, 0);
        BattleSquadPlanner closePlanner = CreatePlanner(
            closeGrid, [], [], [], closeShooters, closeEnemies);

        BattleSquad farShooters = CreateSquad("Far Thrower", 901);
        ((Soldier)farShooters.Soldiers[0].Soldier).Dexterity = 22;
        AddBeltGrenade(farShooters.Soldiers[0]);
        BattleSquad farEnemies = CreateSquad("Far Enemy", 911);
        BattleGridManager farGrid = new();
        Place(farGrid, farShooters.Soldiers[0], true, 0, 0);
        Place(farGrid, farEnemies.Soldiers[0], false, 28, 0);
        BattleSquadPlanner farPlanner = CreatePlanner(
            farGrid, [], [], [], farShooters, farEnemies);

        BattleSquadPlanner.TemplateFiringLineEvaluation closeThrow =
            closePlanner.SelectBestBlastThrow(closeShooters.Soldiers[0]);
        BattleSquadPlanner.TemplateFiringLineEvaluation farThrow =
            farPlanner.SelectBestBlastThrow(farShooters.Soldiers[0]);

        Assert.NotNull(closeThrow);
        Assert.NotNull(farThrow);
        Assert.True(
            farThrow.Score < closeThrow.Score,
            $"Far throw ({farThrow.Score}) must score below the close throw ({closeThrow.Score}).");
    }

    [Fact]
    public void BlastThrowScore_RewardsThrowingSkill()
    {
        // Perfect-impact scoring ignored the thrower entirely; the probabilistic
        // evaluation must expect more value from a skilled arm at the same range.
        BattleSquad cleverShooters = CreateSquad("Skilled Thrower", 920);
        ((Soldier)cleverShooters.Soldiers[0].Soldier).Dexterity = 22;
        AddBeltGrenade(cleverShooters.Soldiers[0]);
        BattleSquad firstEnemies = CreateSquad("Enemy", 930);
        BattleGridManager firstGrid = new();
        Place(firstGrid, cleverShooters.Soldiers[0], true, 0, 0);
        Place(firstGrid, firstEnemies.Soldiers[0], false, 20, 0);
        BattleSquadPlanner firstPlanner = CreatePlanner(
            firstGrid, [], [], [], cleverShooters, firstEnemies);

        BattleSquad clumsyShooters = CreateSquad("Unskilled Thrower", 921);
        AddBeltGrenade(clumsyShooters.Soldiers[0]);
        BattleSquad secondEnemies = CreateSquad("Enemy", 931);
        BattleGridManager secondGrid = new();
        Place(secondGrid, clumsyShooters.Soldiers[0], true, 0, 0);
        Place(secondGrid, secondEnemies.Soldiers[0], false, 20, 0);
        BattleSquadPlanner secondPlanner = CreatePlanner(
            secondGrid, [], [], [], clumsyShooters, secondEnemies);

        BattleSquadPlanner.TemplateFiringLineEvaluation skilledThrow =
            firstPlanner.SelectBestBlastThrow(cleverShooters.Soldiers[0]);
        BattleSquadPlanner.TemplateFiringLineEvaluation unskilledThrow =
            secondPlanner.SelectBestBlastThrow(clumsyShooters.Soldiers[0]);

        Assert.NotNull(skilledThrow);
        float unskilledScore = unskilledThrow?.Score ?? 0;
        Assert.True(
            skilledThrow.Score > unskilledScore,
            $"Skilled throw ({skilledThrow.Score}) must out-score unskilled ({unskilledScore}).");
    }

    [Fact]
    public void GrenadeOnlySoldierWithEmptyGrenade_ReloadsInsteadOfDeadlocking()
    {
        BattleSquad shooters = CreateSquad("Empty Grenadier", 820);
        BattleSoldier shooter = shooters.Soldiers[0];
        RangedWeapon grenade = new(TestModelFactory.FragGrenadeTemplate) { LoadedAmmo = 0 };
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(grenade);
        shooter.ReadyWeapon(grenade);
        BattleSquad enemies = CreateSquad("Enemy", 830);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 20, 0);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, moveActions, [], shooters, enemies);

        planner.PrepareActions(shooters);

        ReloadRangedWeaponAction reload = shootActions
            .OfType<ReloadRangedWeaponAction>()
            .Single();
        reload.Execute(null);
        // ReloadTime 1: the next grenade is in hand for the following turn.
        Assert.Equal(1, grenade.LoadedAmmo);
        Assert.DoesNotContain(shootActions, action => action is BlastAttackAction);
    }

    [Fact]
    public void RifleBearerWithNoTargetInRange_RestocksTheSpentBeltGrenade()
    {
        BattleSquad shooters = CreateSquad("Restocking Grenadier", 840);
        BattleSoldier shooter = shooters.Soldiers[0];
        RangedWeapon grenade = ArmWithRifleAndBeltGrenade(shooter);
        grenade.LoadedAmmo = 0;
        BattleSquad enemies = CreateSquad("Far Enemy", 850);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        // Outside the rifle's 100m reach: no shot exists, so the turn restocks the belt.
        Place(grid, enemies.Soldiers[0], false, 150, 0);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, moveActions, [], shooters, enemies);

        planner.PrepareActions(shooters);

        ReloadRangedWeaponAction reload = shootActions
            .OfType<ReloadRangedWeaponAction>()
            .Single();
        reload.Execute(null);
        Assert.Equal(1, grenade.LoadedAmmo);
    }

    [Fact]
    public void FlamerBearerWithABeltGrenade_StillFiresTheConeOnAnEvenTrade()
    {
        BattleSquad shooters = CreateSquad("Flamer Bearer", 860);
        BattleSoldier shooter = shooters.Soldiers[0];
        EquipFlamer(shooter);
        AddBeltGrenade(shooter);
        BattleSquad enemies = CreateSquad("Enemy", 870);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 10, 0);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, enemies);

        planner.PrepareActions(shooters);

        // Cone and blast both promise the same single kill here; the sidearm rule
        // requires the grenade to strictly beat the conventional option, so ties
        // stay on the flamer.
        AreaAttackAction cone = Assert.IsType<AreaAttackAction>(Assert.Single(shootActions));
        Assert.Equal(enemies.Soldiers[0].Soldier.Id, cone.TargetId);
    }

    private static BattleSquad CreateSquad(
        string name,
        int soldierId,
        int battleValue = 2)
    {
        return CreateSquad(name, (soldierId, battleValue));
    }

    private static BattleSquad CreateSquad(
        string name,
        params (int SoldierId, int BattleValue)[] members)
    {
        List<Soldier> soldiers = members
            .Select(member =>
            {
                SoldierTemplate template = new(
                    30_000 + member.SoldierId,
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

    /// <summary>
    /// A confident marksman's rifle in hand plus the frag grenade on the belt (the
    /// third weapon-set slot: owned, loaded, but not occupying a hand).
    /// </summary>
    private static RangedWeapon ArmWithRifleAndBeltGrenade(BattleSoldier soldier)
    {
        ((Soldier)soldier.Soldier).Dexterity = 22;
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            99_300,
            "Test Marksman Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 12,
            maxDistance: 100,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(rifle);
        soldier.ReadyWeapon(rifle);
        return AddBeltGrenade(soldier);
    }

    private static RangedWeapon AddBeltGrenade(BattleSoldier soldier)
    {
        RangedWeapon grenade = new(TestModelFactory.FragGrenadeTemplate);
        soldier.RangedWeapons.Add(grenade);
        return grenade;
    }

    private static void EquipFlamer(BattleSoldier soldier)
    {
        RangedWeapon flamer = new(new RangedWeaponTemplate(
            99_301,
            "Test Flamer",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 5,
            maxDistance: 30,
            rof: 1,
            ammo: 50,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 3,
            templateType: 1,
            areaRadius: 5,
            fuelPerBurst: 10));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(flamer);
        soldier.ReadyWeapon(flamer);
    }

    /// <summary>
    /// A hard-to-kill enemy who shoots well enough that his squad's preferred
    /// engagement range comfortably covers the test distances, pinning the planner's
    /// imminence factor at 1 so the rifle-vs-grenade comparison is stable.
    /// </summary>
    private static void MakeToughSharpshooter(BattleSoldier soldier, float constitution)
    {
        Soldier raw = (Soldier)soldier.Soldier;
        raw.Constitution = constitution;
        raw.Dexterity = 22;
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = new ValueTuple<int, int>(x, y);
        grid.PlaceSoldier(soldier, side, [new ValueTuple<int, int>(x, y)]);
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
        IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeTemplates = soldiers.Values
            .SelectMany(soldier => soldier.MeleeWeapons
                .Concat(soldier.EquippedMeleeWeapons)
                .Select(weapon => weapon.Template)
                .Append(soldier.Soldier.Template.Species.DefaultUnarmedWeapon))
            .GroupBy(template => template.Id)
            .ToDictionary(group => group.Key, group => group.First());
        return new BattleSquadPlanner(
            grid,
            soldiers,
            shootActions,
            moveActions,
            meleeActions,
            null,
            meleeTemplates,
            new SeededRNG(12345));
    }
}
