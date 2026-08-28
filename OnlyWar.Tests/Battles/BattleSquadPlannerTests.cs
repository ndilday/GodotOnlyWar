using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class BattleSquadPlannerTests
{
    public BattleSquadPlannerTests()
    {
        LegacyWeaponSetBattleFixture.UseIntrinsicBattleValues();
    }

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
        float maximumRange = 30,
        float baseDamage = 5)
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
            baseDamage: baseDamage,
            maxDistance: maximumRange,
            rof: 1,
            ammo: 50,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 3,
            templateType: 1,
            areaRadius: areaRadius));
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

    // PHASE 6 replacement for EstimateKillDistance_MultiHitWeaponRetainsStandoffRange. The
    // property is unchanged and still worth pinning -- a degrading weapon that cannot take its
    // target out in ONE hit still has a useful standoff range, and must not be confused with a
    // weapon that cannot wound it at all. What changed is that the old test asked
    // EstimateKillDistance, a function that answered it with a hand-placed one-third
    // armor-penetration quantile; the curve answers it from the removal it actually produces.
    [Theory]
    [InlineData(7.5f, 1600f)]
    [InlineData(10.5f, 1000f)]
    public void OptimalDistance_MultiHitDegradingWeaponRetainsStandoffRange(
        float damageMultiplier,
        float maximumRange)
    {
        BattleSquad squad = CreateSquad("Multi Hit Gunner", 91_005);
        BattleSoldier shooter = squad.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon weapon = new(new RangedWeaponTemplate(
            99_280,
            "Degrading Test Weapon",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 9,
            armorMultiplier: 0.5f,
            penetrationMultiplier: damageMultiplier == 7.5f ? 2 : 1,
            requiredStrength: 0,
            baseDamage: damageMultiplier,
            maxDistance: maximumRange,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 4,
            doesDamageDegradeWithRange: true,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(weapon);
        shooter.ReadyWeapon(weapon);

        float distance = BattleModifiersUtil.CalculateOptimalDistance(
            shooter, targetSize: 1f, targetArmor: 0f, targetCon: 100f);

        Assert.True(distance > 0, $"expected a useful standoff range, got {distance}");
    }

    // PHASE 6 DELETED EstimateKillDistance_OneShotCaseKeepsExistingQuantileRange. It asserted the
    // distance fell in 1040-1060 yards, a number produced entirely by EstimateKillDistance's
    // hand-placed "1/3 chance of a killshot" quantile (the 4.25f divisor). That quantile was the
    // approximation Phase 6 removed; there is no behaviour left underneath the assertion to
    // re-express, only the constant it pinned. The property that survives -- a degrading weapon's
    // standoff shrinks as the target gets tougher -- is covered by
    // OptimalDistance_DegradingWeaponStandsCloserAgainstAToughterTarget below.

    [Fact]
    public void OptimalDistance_DegradingWeaponStandsCloserAgainstAToughterTarget()
    {
        BattleSquad squad = CreateSquad("Sniper", 91_006);
        BattleSoldier shooter = squad.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon sniper = new(new RangedWeaponTemplate(
            99_281,
            "Sniper Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 9,
            armorMultiplier: 0.5f,
            penetrationMultiplier: 2,
            requiredStrength: 0,
            baseDamage: 7.5f,
            maxDistance: 1600,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 4,
            doesDamageDegradeWithRange: true,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(sniper);
        shooter.ReadyWeapon(sniper);

        float soft = BattleModifiersUtil.CalculateOptimalDistance(
            shooter, targetSize: 1f, targetArmor: 10f, targetCon: 12f);
        float tough = BattleModifiersUtil.CalculateOptimalDistance(
            shooter, targetSize: 1f, targetArmor: 30f, targetCon: 40f);

        Assert.True(soft > 0, $"expected a standoff against the soft target, got {soft}");
        Assert.True(
            tough < soft,
            $"expected a degrading weapon to close against a tougher target ({soft} -> {tough})");
    }

    [Fact]
    public void OptimalDistance_WeaponThatCannotPenetrateHasNoStandoffRange()
    {
        BattleSquad squad = CreateSquad("Popgunner", 91_007);
        BattleSoldier shooter = squad.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon popgun = new(new RangedWeaponTemplate(
            99_282,
            "Popgun",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 9,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 2,
            maxDistance: 300,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 1,
            doesDamageDegradeWithRange: true,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(popgun);
        shooter.ReadyWeapon(popgun);

        // PHASE 6. The old EstimateKillDistance signalled this with a magic -1 that
        // CalculateOptimalDistance then swallowed via min(). The invariant is the same and is now
        // stated directly: a target that cannot be penetrated buys no standoff at all.
        Assert.Equal(
            0f,
            BattleModifiersUtil.CalculateOptimalDistance(
                shooter, targetSize: 1f, targetArmor: 20f, targetCon: 10f));
    }

    [Fact]
    public void PrepareActions_UsesBestShootableTargetForStandoffAgainstToughScreen()
    {
        BattleSquad shooters = CreateSquad("Sniper", 91_010);
        BattleSquad toughScreen = CreateSquad("Tough Screen", 91_011);
        BattleSquad softMass = CreateSquad(
            "Soft Mass",
            91_012,
            battleValue: 1_000);
        BattleSoldier shooter = shooters.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 20;
        RangedWeapon sniper = new(new RangedWeaponTemplate(
            99_283,
            "Sniper Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 9,
            armorMultiplier: 0.5f,
            penetrationMultiplier: 2,
            requiredStrength: 0,
            baseDamage: 7.5f,
            maxDistance: 1600,
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 8,
            doesDamageDegradeWithRange: true,
            reloadTime: 1));
        shooter.RangedWeapons.Clear();
        shooter.ClearReadiedRangedWeapons();
        shooter.RangedWeapons.Add(sniper);
        shooter.ReadyWeapon(sniper);

        BattleSoldier screen = toughScreen.Soldiers[0];
        ((Soldier)screen.Soldier).Constitution = 100;
        screen.Armor = new Armor(new ArmorTemplate(99_284, "Heavy Screen", 60, 0));
        BattleSoldier softTarget = softMass.Soldiers[0];
        softTarget.Armor = new Armor(new ArmorTemplate(99_285, "No Armor", 0, 0));
        shooter.Aim = new ValueTuple<int, RangedWeapon, int>(
            softTarget.Soldier.Id, sniper, 0);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, screen, false, 300, 0);
        Place(grid, softTarget, false, 301, 0);
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], shooters, toughScreen, softMass);

        planner.PrepareActions(shooters);

        Assert.Equal(SquadMovementTier.Stationary, shooters.MovementTier);
        IAction rangedAction = Assert.Single(shootActions);
        Assert.True(rangedAction is AimAction or ShootAction);
        if (rangedAction is ShootAction shot)
        {
            Assert.Equal(softTarget.Soldier.Id, shot.TargetId);
        }
        Assert.Equal(
            softTarget.Soldier.Id,
            planner.SelectBestRangedTarget(shooter, useBulk: false).Target.Soldier.Id);
    }

    [Fact]
    public void TakeOutProbability_ReadsAccumulatedLocationWounds()
    {
        BattleSquad targets = CreateSquad("Wounded Target", 91_020);
        BattleSoldier target = targets.Soldiers[0];
        HitLocation location = target.Soldier.Body.HitLocations
            .First(candidate => candidate.Template.IsMotive || candidate.Template.IsVital);
        float fresh = RemovalMath.CalculateTakeOutProbabilityOnHit(
            target, damageCoefficient: 2f, effectiveArmor: 0f, weaponWoundMultiplier: 1f);

        uint setupWounds = location.Template.CrippleWound switch
        {
            (uint)WoundLevel.Moderate => 5u * (uint)WoundLevel.Minor,
            (uint)WoundLevel.Major => 5u * (uint)WoundLevel.Moderate,
            (uint)WoundLevel.Critical => 5u * (uint)WoundLevel.Major,
            (uint)WoundLevel.Massive => 5u * (uint)WoundLevel.Critical,
            _ => 0u
        };
        Assert.True(setupWounds > 0);
        location.Wounds = new Wounds(setupWounds, 0);

        float wounded = RemovalMath.CalculateTakeOutProbabilityOnHit(
            target, damageCoefficient: 2f, effectiveArmor: 0f, weaponWoundMultiplier: 1f);

        Assert.True(
            wounded > fresh,
            $"expected accumulated wounds to increase take-out chance ({fresh} -> {wounded})");
    }

    [Fact]
    public void OpeningDistance_HitLimitedHeavyWeaponStillOpensFar()
    {
        // PHASE 6 REPLACES CalculateOpeningDistance_HitLimitedHeavyWeaponOpensFarWhileOptimalIsZero.
        // The scenario is unchanged: a single-shot heavy weapon (missile-launcher-like) can wound
        // at any range but rarely hits a small target at range in ordinary hands. What changed is
        // that the OLD assertion had two halves and one of them was an artifact. It asserted
        // optimal == 0 exactly, which happened only because EstimateHitDistance returned a hard 0
        // whenever the to-hit total failed to clear 10.5 -- and that cliff was the entire reason
        // CalculateOpeningDistance had to exist, to disambiguate the 0 by cause. There is no cliff
        // now: a 20%-at-400-yards hit chance scores 20%, so the standoff and the opening range are
        // the same nonzero number and the surviving half of the property -- this weapon opens far
        // rather than being dragged to a close start -- is asserted directly.
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

        // PHASE 7 DROPPED THE GetPreferredOpeningRange HALF. Phase 6 had already reduced it to the
        // same un-opposed saturation range CalculateOptimalDistance returns, so the second
        // assertion restated the first; Phase 7 then re-pointed opening range at the derived band,
        // which needs an opposing FORCE and is therefore no longer a property of this weapon alone.
        // The surviving property -- a weapon that can wound at any range but rarely hits a small
        // target still wants a standoff rather than being dragged to a close start -- is exactly
        // what CalculateOptimalDistance answers, and it is asserted here.
        float optimal = BattleModifiersUtil.CalculateOptimalDistance(shooter, 1f, 15f, 30f);

        Assert.True(optimal > 0f, $"expected hit-limited weapon to stand off, got {optimal}");
    }

    [Fact]
    public void OpeningDistance_WoundLimitedWeaponStaysCloseLikeOptimal()
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
        // As above, the opening-range half is gone in Phase 7; the invariant it restated -- a
        // weapon that cannot wound the target at any range buys no standoff -- is the assertion
        // that remains.
        float optimal = BattleModifiersUtil.CalculateOptimalDistance(shooter, 1f, 30f, 30f);

        Assert.Equal(0f, optimal);
    }

    [Fact]
    public void PrepareActions_FlamerInRangeUsesTemplateAttackAtItsScoredPosture()
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

        // Phase 1's take-out-rate melee currency can make the scored posture Jog while the
        // template attack remains the best action. The behavior under test is that an in-range
        // flamer still fires, not that the old contact-threat baseline remains unchanged.
        Assert.Contains(
            shooters.MovementTier,
            new[] { SquadMovementTier.Stationary, SquadMovementTier.Walk, SquadMovementTier.Jog });
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

        // DIAGNOSTIC (2026-08-10, Design/Active/EngagementHorizonModel.md §0/§4). This posture
        // failure is one of only two surviving pieces of evidence about the derived exchange
        // horizon, and it is only evidence if the derivation actually ran. A standalone planner
        // builds its own BattlePlanningContext; if PrepareActions never calls
        // SetEngagementHorizon, ExpectedExchangeTurnsFor returns its dictionary-miss default of
        // MaximumExchangeTurns and this fixture has been measuring the 183-turn fallback -- the
        // same value whose over-calibration prompted the whole horizon investigation.
        Assert.True(
            planner.EngagementHorizonInitialized,
            "the engagement horizon was never derived for this planning turn, so "
                + $"squad {shooters.Id} scored at the fallback horizon "
                + $"{planner.ExpectedExchangeTurnsFor(shooters.Id)} rather than a derived one");

        Assert.True(
            shooters.MovementTier == SquadMovementTier.Run,
            $"expected Run, got {shooters.MovementTier} at exchange horizon "
                + $"{planner.ExpectedExchangeTurnsFor(shooters.Id):0.##} "
                + $"(cap {EngagementHorizonModel.MaximumExchangeTurns:0.##})");
        Assert.InRange(shooter.CurrentSpeed, 0.001f, shooter.GetMoveSpeed());
        Assert.Empty(shootActions.OfType<AreaAttackAction>());
        Assert.IsType<MoveAction>(Assert.Single(moveActions));
    }

    [Fact]
    public void PrepareActions_HeavyWeaponInsidePreferredRangeHoldsInsteadOfWalking()
    {
        // A step-back applies half Bulk to every shot that turn. For a Bulk-8 heavy weapon
        // that guts the soldier's firepower, so he should plant and fire (Stationary) rather
        // than kite back the way a light-weapon squad would. Same geometry as
        // PrepareActions_EnemyInsidePreferredRangeDoesNotAdvance, only the weapon is heavier.
        //
        // RESTORED 2026-08-10. This expectation was inverted to Run during the potential work,
        // justified as contact geometry outweighing "one current heavy shot" over the remaining
        // battle -- an argument that only holds at the 183-turn horizon fallback. The Bulk rule
        // above is a real mechanic and was not superseded by anything.
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
    public void PrepareActions_EnemyInsidePreferredRangeDoesNotAdvance()
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

        // RESTORED 2026-08-10, with the original name. A rifle that reaches 100 yards, against a
        // lone enemy at 10, has nothing to gain by advancing: the shot is already available and
        // undegraded. The expectation was inverted to Run during the potential work while the
        // test was renamed to UsesStatePotential -- a name that describes the mechanism rather
        // than any behaviour, and so could not be contradicted by any outcome.
        Assert.Contains(
            shooters.MovementTier,
            new[] { SquadMovementTier.Stationary, SquadMovementTier.Walk });
        Assert.DoesNotContain(
            shooters.MovementTier,
            new[] { SquadMovementTier.Jog, SquadMovementTier.Run, SquadMovementTier.InMelee });
    }

    [Fact]
    public void PrepareActions_ShooterAtAimCapFiresInsteadOfContinuingToAim()
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

        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(shootActions));
        Assert.Equal(target.Soldier.Id, shot.TargetId);
        Assert.Equal(rifle.Template.Id, shot.WeaponId);
        Assert.DoesNotContain(shootActions, action => action is AimAction);
    }

    [Fact]
    public void CoverRoleHold_AimPastCapStillForcesShot()
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
        Dictionary<int, EngagementRoleConstraint> constraints = new()
        {
            [shooters.Id] = new EngagementRoleConstraint(
                EngagementSquadRole.Cover, FixedHeading: 0)
        };
        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([shooters], [enemies], constraints);

        SquadEngagementDecision scored = planner.ChooseEngagementOption(
            shooters,
            paired.Frames[shooters.Id],
            paired.Profiles,
            paired.Frames,
            [shooters],
            [enemies]);
        SquadEngagementDecision selected = scored with
        {
            Chosen = scored.Candidates.Single(candidate =>
                candidate.Kind == EngagementOptionKind.Hold)
        };
        planner.DeclareEngagementDecision(selected);
        planner.BuildEngagementActions(selected);

        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(shootActions));
        Assert.Equal(target.Soldier.Id, shot.TargetId);
        Assert.Equal(rifle.Template.Id, shot.WeaponId);
    }

    [Fact]
    public void ChooseEngagementOption_WithdrawingMeleeOpponentProjectsOpeningNotCharging()
    {
        // A Bound squad has been ordered to run away (see BattleEngagementFrameBuilder.BuildSide's
        // quarryRunSpeed switch), so a melee-only opponent marked Bound must NOT be projected as
        // closing to melee range. Proven indirectly: with a charging melee opponent the projection
        // should eventually see it reach contact and charge incoming BV against us; with the SAME
        // opponent marked Bound it never does, so Hold's projected future should be strictly
        // better.
        //
        // RESTORED 2026-08-10. The assertion was inverted to Assert.Equal during the potential
        // work -- i.e. to assert the distinction does NOT exist -- while the name kept promising
        // "ProjectsOpeningNotCharging". If the withdrawal distinction genuinely no longer belongs
        // in the potential, that needs an argument and a rename, not an inverted assertion under
        // an unchanged name.
        BattleSquad shooters = CreateSquad("Ranged Holder", 90_101);
        BattleSquad meleeEnemy = CreateSquad("Melee Enemy", 90_102);
        EquipAimTestRifle(shooters.Soldiers[0], 99_260);
        meleeEnemy.Soldiers[0].ClearReadiedRangedWeapons();
        meleeEnemy.Soldiers[0].RangedWeapons.Clear();

        BattleSquadCapabilityProfile enemyProfile =
            BattleEngagementFrameBuilder.BuildProfile(meleeEnemy);
        int range = (int)System.Math.Ceiling(enemyProfile.MoveSpeed) + 1;

        BattleGridManager grid = new();
        Place(grid, shooters.Soldiers[0], true, 0, 0);
        Place(grid, meleeEnemy.Soldiers[0], false, range, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, meleeEnemy);

        Dictionary<int, EngagementRoleConstraint> chargingConstraints = new()
        {
            [meleeEnemy.Id] = new EngagementRoleConstraint(EngagementSquadRole.Normal)
        };
        Dictionary<int, EngagementRoleConstraint> withdrawingConstraints = new()
        {
            [meleeEnemy.Id] = new EngagementRoleConstraint(EngagementSquadRole.Bound)
        };

        BattleEngagementFrameBuilder.PairedFrame chargingPaired =
            BattleEngagementFrameBuilder.Build([shooters], [meleeEnemy], chargingConstraints);
        BattleEngagementFrameBuilder.PairedFrame withdrawingPaired =
            BattleEngagementFrameBuilder.Build([shooters], [meleeEnemy], withdrawingConstraints);

        SquadEngagementDecision chargingDecision = planner.ChooseEngagementOption(
            shooters,
            chargingPaired.Frames[shooters.Id],
            chargingPaired.Profiles,
            chargingPaired.Frames,
            [shooters],
            [meleeEnemy]);
        SquadEngagementDecision withdrawingDecision = planner.ChooseEngagementOption(
            shooters,
            withdrawingPaired.Frames[shooters.Id],
            withdrawingPaired.Profiles,
            withdrawingPaired.Frames,
            [shooters],
            [meleeEnemy]);

        float chargingHoldFuture = chargingDecision.Candidates
            .Single(candidate => candidate.Kind == EngagementOptionKind.Hold)
            .FutureExchange[0];
        float withdrawingHoldFuture = withdrawingDecision.Candidates
            .Single(candidate => candidate.Kind == EngagementOptionKind.Hold)
            .FutureExchange[0];

        Assert.True(
            withdrawingHoldFuture > chargingHoldFuture,
            $"expected withdrawing enemy to project less incoming melee threat: "
                + $"charging={chargingHoldFuture}, withdrawing={withdrawingHoldFuture}");
    }

    private static RangedWeapon EquipLongReachRifle(BattleSoldier soldier, int templateId)
    {
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            templateId,
            "Long Reach Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 6,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 100,
            maxDistance: 1_000,
            rof: 3,
            ammo: 30,
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
    public void ChooseEngagementOption_LookaheadSeesOwnMovementInsideWeaponReach()
    {
        // Phase 2 (Design/Reference/BattleLogic.md). Reference scenario: a squad with a
        // non-degrading 1000-range rifle standing 200 yards from a melee-only enemy.
        //
        // Before: PolicyRangeDelta and the depth-0 terminal both used `desired =
        // PreferredBandUpper`, i.e. the weapon's MAXIMUM range. At 200 < 1000, `range > desired` is
        // false for every policy, so projected own motion was 0 and `turnsToAct` was 0 identically
        // across all five options -- the lookahead could not see its own movement.
        //
        // After: both sites use EffectiveEngagementRange, which is well inside reach here, so
        // closing policies project real motion and the terminal differentiates. (Phase 6 changed
        // how that range is DERIVED -- it is now the argmax of removal(r) - incoming(r) rather than
        // an accuracy/penetration limit -- but not that it is inside reach, which is all this test
        // needs.)
        //
        // The "before" arm is reconstructed exactly by forcing EffectiveEngagementRange back onto
        // PreferredBandUpper, so this test fails if either changed site regresses.
        BattleSquad shooters = CreateSquad("Reference Bolters", 90_130);
        BattleSquad meleeEnemy = CreateSquad("Melee Enemy", 90_131);
        EquipLongReachRifle(shooters.Soldiers[0], 99_262);
        meleeEnemy.Soldiers[0].ClearReadiedRangedWeapons();
        meleeEnemy.Soldiers[0].RangedWeapons.Clear();

        BattleGridManager grid = new();
        Place(grid, shooters.Soldiers[0], true, 0, 0);
        Place(grid, meleeEnemy.Soldiers[0], false, 200, 0);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([shooters], [meleeEnemy]);
        BattleSquadCapabilityProfile shooterProfile = paired.Profiles[shooters.Id];

        Assert.Equal(1_000f, shooterProfile.PreferredBandUpper, 3);
        Assert.True(
            shooterProfile.EffectiveEngagementRange > 0
                && shooterProfile.EffectiveEngagementRange < 200,
            $"expected an effective engagement range inside the 200-yard standoff, got "
                + $"{shooterProfile.EffectiveEngagementRange} (reach "
                + $"{shooterProfile.PreferredBandUpper})");

        Dictionary<int, BattleSquadCapabilityProfile> conflatedProfiles = paired.Profiles
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value with
                {
                    EffectiveEngagementRange = entry.Value.PreferredBandUpper
                });

        SquadEngagementDecision after = CreatePlanner(grid, shooters, meleeEnemy)
            .ChooseEngagementOption(
                shooters,
                paired.Frames[shooters.Id],
                paired.Profiles,
                paired.Frames,
                [shooters],
                [meleeEnemy]);
        SquadEngagementDecision before = CreatePlanner(grid, shooters, meleeEnemy)
            .ChooseEngagementOption(
                shooters,
                paired.Frames[shooters.Id],
                conflatedProfiles,
                paired.Frames,
                [shooters],
                [meleeEnemy]);

        static float Continuation(SquadEngagementDecision decision, EngagementOptionKind kind)
        {
            EngagementOptionEvaluation candidate = decision.Candidates.Single(entry =>
                entry.Kind == kind);
            return candidate.FutureExchange[0] + candidate.AccessPotentialValue;
        }

        float beforeSpread = System.Math.Abs(
            Continuation(before, EngagementOptionKind.RunToward)
                - Continuation(before, EngagementOptionKind.Hold));
        float afterSpread = System.Math.Abs(
            Continuation(after, EngagementOptionKind.RunToward)
                - Continuation(after, EngagementOptionKind.Hold));

        // PHASE 5d REVISED THIS ARM. It used to assert that Hold's future SHRINKS under the honest
        // range (0.38882 -> 0.23541), because the old terminal was
        // `attainable * 0.25 / (1 + turnsToAct)` -- a penalty applied to Hold for standing far from
        // a range Hold, by definition, never closes to. Phase 5d replaced that with a geometric
        // continuation of the real per-turn exchange, evaluated at the range the squad WILL act
        // from and discounted by the turns it takes to get there. Under that shape the honest range
        // makes Hold's terminal LARGER here (1.28e-8 -> 2.12e-8): standing 200 yards off is worth
        // almost nothing, and thirty-odd discounted turns of grinding at the effective range is
        // worth almost nothing plus a little. The direction of this one number was a property of
        // the old terminal's formula, not of Phase 2, so asserting it would now pin the shape
        // Phase 5d deliberately removed.
        //
        // The Phase 2 property itself -- the lookahead can SEE its own movement -- is entirely
        // carried by the two assertions below, which are unchanged and still discriminate: under
        // the conflated range every policy projects zero own-motion, so closing and holding are
        // indistinguishable.
        Assert.NotEqual(
            Continuation(before, EngagementOptionKind.Hold),
            Continuation(after, EngagementOptionKind.Hold));
        // PHASE 6 DROPPED A THIRD ARM. It asserted `Future(after, CloseToContact) >
        // Future(after, Hold)` -- "closing is now worth strictly more than standing still" -- and
        // it no longer holds, for a reason specific to this fixture rather than to the property
        // under test.
        //
        // Under the derived band this shooter's EffectiveEngagementRange is 1, i.e. contact. That
        // is the honest answer here: the Long Reach Rifle has accuracy 6 against a SIZE 1 target,
        // so at the fixture's 200 yards its hit probability is about 0.0005, while the lone
        // melee-only enemy threatens 0.234 BV/turn at contact. A rifleman who cannot hit at 200
        // yards should close, and the model says so. But CloseToContact is scored with zero
        // outgoing retention (you do not shoot while running) against melee incoming that switches
        // on the moment you are inside 1.5, so the LOOKAHEAD still prices closing below holding --
        // and both arms are now on the order of 1e-7, because the terminal is discounted across the
        // ~50 turns it takes to cross 200 yards at this speed. Asserting the sign of a difference
        // between two numbers that are both effectively zero pins fixture noise.
        //
        // The property this test is named for -- the lookahead can SEE its own movement -- is
        // carried entirely by the two surviving assertions, and they still discriminate sharply:
        // the spread widened from 2.15e-9 (conflated) to 1.22e-7 (derived), a factor of ~57.
        Assert.True(
            afterSpread > beforeSpread,
            $"expected movement to be more visible to the lookahead: "
                + $"before={beforeSpread}, after={afterSpread}");
    }

    [Fact]
    public void CapabilityProfile_NonDegradingWeaponEffectiveRangeIsDerivedNotReach()
    {
        // PHASE 6 FLIPPED THIS TEST. It was
        // CapabilityProfile_NonDegradingWeaponEffectiveRangeStillCollapsesOntoReach, a deliberate
        // characterization of the defect: EstimateKillDistance short-circuited to the weapon's
        // MaximumRange for a non-degrading weapon, so against a large, unarmored, non-evasive
        // target accuracy was never the binding constraint and the "effective" range degenerated
        // back to reach -- the Xibarrus Zeta bolter-vs-Carnifex case in
        // Design/Reference/BattleLogic.md. The same scenario now derives the band from
        // removal(r) - incoming(r), and it lands strictly inside reach: closing improves the hit
        // chance, but it also brings a melee-only enemy in sooner, and the derived standoff is
        // where those two stop trading evenly.
        BattleSquad crackShots = CreateSquad("Crack Shots", 90_150);
        BattleSquad bigEnemy = CreateSquad("Big Enemy", 90_151, battleValue: 30, size: 8f);
        EquipLongReachRifle(crackShots.Soldiers[0], 99_264);
        ((Soldier)crackShots.Soldiers[0].Soldier).Dexterity = 20;
        bigEnemy.Soldiers[0].ClearReadiedRangedWeapons();
        bigEnemy.Soldiers[0].RangedWeapons.Clear();
        BattleGridManager grid = new();
        Place(grid, crackShots.Soldiers[0], true, 0, 0);
        Place(grid, bigEnemy.Soldiers[0], false, 200, 0);

        BattleEngagementFrameBuilder.PairedFrame paired =
            BattleEngagementFrameBuilder.Build([crackShots], [bigEnemy]);
        BattleSquadCapabilityProfile profile = paired.Profiles[crackShots.Id];

        Assert.True(
            profile.EffectiveEngagementRange > 0,
            "a penetrable target at reach must still buy a standoff");
        Assert.True(
            profile.EffectiveEngagementRange < profile.PreferredBandUpper,
            "expected the derived band to separate from reach: "
                + $"{profile.EffectiveEngagementRange} vs reach {profile.PreferredBandUpper}");
    }

    [Fact]
    public void PrepareActions_ClosingSquadSelectsAForwardPosture()
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

        Assert.Contains(shooters.MovementTier, new[] { SquadMovementTier.Jog, SquadMovementTier.Run, SquadMovementTier.InMelee });
        Assert.True(shooter.CurrentSpeed > 0);
        IAction movement = Assert.Single(moveActions);
        Assert.True(movement is MoveAction or SquadChargeIntentAction);
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

        RangedTargetEvaluation target = planner.SelectBestRangedTarget(
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

    [Fact]
    public void PrepareActions_AdjacentMeleeCombatantDiscardsCarriedMovement()
    {
        BattleSquad attackers = CreateSquad("Engaged Attacker", 90_023);
        BattleSquad enemies = CreateSquad("Adjacent Target", 90_024);
        BattleSoldier attacker = attackers.Soldiers[0];
        attacker.LeftoverMovement = 40.5f;
        BattleGridManager grid = new();
        Place(grid, attacker, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 1, 0);
        attackers.IsInMelee = true;
        BattleSquadPlanner planner = CreatePlanner(grid, attackers, enemies);

        planner.PrepareActions(attackers);

        Assert.Equal(SquadMovementTier.InMelee, attackers.MovementTier);
        Assert.Equal(0, attacker.CurrentSpeed);
        Assert.Equal(0, attacker.LeftoverMovement);
    }

    private static EngagedDecisionScenario CreateEngagedDecisionScenario(int attackerCount)
    {
        BattleSquad shooterSquad = CreateSquad("Engaged Shooter", 500, battleValue: 10);
        BattleSoldier shooter = shooterSquad.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 17;

        // This is a crossover scenario: one adjacent attacker leaves enough net value to fire,
        // while the accumulated parry risk from three makes readying melee the better action.
        // Both sides are now quoted in take-out probability, so the margin is produced by the
        // actual threshold curve rather than a linear-damage/probability mismatch.
        RangedWeapon pointBlankWeapon = new(new RangedWeaponTemplate(
            99_100,
            "Compact Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            // PHASE 5 RETUNED THIS DIAL, 2.5 -> 4. This fixture exists to STRADDLE the shoot/parry
            // crossover (1 attacker -> shoot, 3 -> ready melee), and the graded removal metric moved
            // the whole curve: both the shot AND the forfeited parry are now credited for the
            // wounding they do rather than only for disabling, and melee at point-blank hits far
            // more often than a bulky rifle does, so parry risk grew faster than shot value. At 2.5
            // damage the shooter drops the rifle even against a SINGLE attacker and the test stops
            // discriminating at all. A rifle that can actually threaten its target restores the
            // straddle at exactly the original 1-versus-3 counts, so the property under test is
            // unchanged -- only the point on the damage axis where it lives.
            baseDamage: 4f,
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
        RangedTargetEvaluation entangledScore = planner.EvaluateRangedTarget(
            shooter,
            entangled,
            shooter.EquippedRangedWeapons[0],
            4,
            0);
        RangedTargetEvaluation cleanScore = planner.EvaluateRangedTarget(
            shooter,
            clean,
            shooter.EquippedRangedWeapons[0],
            8,
            0);
        RangedTargetEvaluation selected = planner.SelectBestRangedTarget(
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

        RangedTargetEvaluation monsterScore = planner.EvaluateRangedTarget(
            shooter,
            monster,
            shooter.EquippedRangedWeapons[0],
            4,
            0);
        RangedTargetEvaluation selected = planner.SelectBestRangedTarget(
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

        RangedTargetEvaluation selected = planner.SelectBestRangedTarget(
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

        RangedTargetEvaluation evaluation = planner.EvaluateRangedTarget(
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

        RangedTargetEvaluation first = planner.EvaluateRangedTarget(
            shooter,
            target,
            weapon,
            4,
            0);
        RangedTargetEvaluation repeated = planner.EvaluateRangedTarget(
            shooter,
            target,
            weapon,
            4,
            0);
        target.CurrentSpeed = 3;
        RangedTargetEvaluation movingTarget = planner.EvaluateRangedTarget(
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

    private static EngagedDecisionScenario CreateGunAndBladeScenario(byte? attackerArmor = null)
    {
        BattleSquad shooterSquad = CreateSquad("Gun And Blade", 520, battleValue: 10);
        BattleSoldier shooter = shooterSquad.Soldiers[0];
        ((Soldier)shooter.Soldier).Dexterity = 17;

        RangedWeapon sidearm = new(new RangedWeaponTemplate(
            99_110,
            "Service Pistol",
            EquipLocation.OneHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: 2.5f,
            maxDistance: 50,
            rof: 1,
            ammo: 5,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: false,
            reloadTime: 1));
        MeleeWeapon blade = new(new MeleeWeaponTemplate(
            99_111,
            "Combat Blade",
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
        shooter.RangedWeapons.Add(sidearm);
        shooter.ReadyWeapon(sidearm);
        shooter.MeleeWeapons.Clear();
        shooter.ClearReadiedMeleeWeapons();
        shooter.MeleeWeapons.Add(blade);
        shooter.ReadyWeapon(blade);

        BattleSquad attackerSquad = CreateSquad("Blade Attacker", 530, battleValue: 10);
        BattleSoldier attacker = attackerSquad.Soldiers[0];
        MeleeWeapon attackerWeapon = new(new MeleeWeaponTemplate(
            99_112,
            "Light Claws",
            EquipLocation.OneHand,
            TestSkills.Melee,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            strengthMultiplier: 0.2142857f,
            parryMod: 0,
            attackSpeedMultiplier: 1));
        attacker.RangedWeapons.Clear();
        attacker.ClearReadiedRangedWeapons();
        attacker.MeleeWeapons.Clear();
        attacker.ClearReadiedMeleeWeapons();
        attacker.MeleeWeapons.Add(attackerWeapon);
        attacker.ReadyWeapon(attackerWeapon);
        if (attackerArmor.HasValue)
        {
            attacker.Armor = new Armor(new ArmorTemplate(
                99_113, "Impenetrable Plate", attackerArmor.Value, 0));
        }

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, attacker, false, 1, 0);

        Dictionary<int, BattleSoldier> soldierMap = new()
        {
            [shooter.Soldier.Id] = shooter,
            [attacker.Soldier.Id] = attacker
        };
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
            Attackers = [attacker],
            ProjectedMeleeWeapon = blade,
            Planner = planner,
            ShootActions = shootActions,
            MeleeActions = meleeActions
        };
    }

    [Fact]
    public void EngagedGunAndBladeSoldier_StrikesAndShootsTheSameTarget()
    {
        EngagedDecisionScenario scenario = CreateGunAndBladeScenario();

        scenario.Planner.PrepareActions(scenario.ShooterSquad);

        MeleeAttackAction strike = Assert.IsType<MeleeAttackAction>(
            Assert.Single(scenario.MeleeActions));
        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(scenario.ShootActions));
        int attackerId = scenario.Attackers[0].Soldier.Id;
        Assert.Equal(attackerId, strike.StrikePlans[0].TargetId);
        Assert.Equal(attackerId, shot.TargetId);
        Assert.True(shot.UseBulk);
    }

    [Fact]
    public void EngagedGunAndBladeSoldier_HoldsFireWhenShotValueIsNegative()
    {
        // The pistol cannot penetrate the attacker's plate, so the shot removes nothing from
        // the enemy while a stray can still wound the shooter himself: net value is negative
        // and only the blade should be used.
        EngagedDecisionScenario scenario = CreateGunAndBladeScenario(attackerArmor: byte.MaxValue);

        scenario.Planner.PrepareActions(scenario.ShooterSquad);

        Assert.IsType<MeleeAttackAction>(Assert.Single(scenario.MeleeActions));
        Assert.Empty(scenario.ShootActions);
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
        TemplateFiringLineEvaluation evaluation =
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
        EquipTemplateWeapon(shooter, baseDamage: 20);
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

    // NOTE (2026-08-07): these two were named ...ParallelPlanningMatchesSerialPlanning and took a
    // maxDegreeOfParallelism argument, but BattleSquadPlanner never read that argument -- the
    // Parallel.For over squad decisions lives in BattleTurnResolver, and this fixture plans ONE
    // squad through a directly-constructed planner. Both halves of the comparison were therefore
    // the same serial path, and the tests could not fail for the reason their names claimed.
    // Renamed to the property they do genuinely cover: planning is deterministic, so a fixed seed
    // and fixed inputs produce byte-identical actions and soldier state on every run. Real
    // parallel-vs-serial equivalence needs a resolver-level fixture; it is not covered here.
    [Fact]
    public void CoverRoleHoldPlan_RepeatedPlanningIsDeterministic()
    {
        var first = RunPlanningScenario();

        for (int repetition = 0; repetition < 20; repetition++)
        {
            var repeated = RunPlanningScenario();
            Assert.Equal(first.Actions, repeated.Actions);
            Assert.Equal(first.SoldierState, repeated.SoldierState);
        }
    }

    [Fact]
    public void PursuitJogPlan_RepeatedMovingFirePlanningIsDeterministic()
    {
        var first = RunPlanningScenario(pursuit: true);

        for (int repetition = 0; repetition < 20; repetition++)
        {
            var repeated = RunPlanningScenario(pursuit: true);
            Assert.Equal(first.Actions, repeated.Actions);
            Assert.Equal(first.SoldierState, repeated.SoldierState);
        }
    }

    private static (string[] Actions, string[] SoldierState) RunPlanningScenario(
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
            new SeededRNG(12_345));

        EngagementRoleConstraint constraint = pursuit
            ? new EngagementRoleConstraint(
                EngagementSquadRole.Pursuit,
                QuarryRunSpeed: enemies.GetSquadMove(),
                RoleTargets: [enemies])
            : new EngagementRoleConstraint(EngagementSquadRole.Cover, FixedHeading: 0);
        BattleEngagementFrameBuilder.PairedFrame paired = BattleEngagementFrameBuilder.Build(
            [shooters],
            [enemies],
            new Dictionary<int, EngagementRoleConstraint> { [shooters.Id] = constraint });
        SquadEngagementDecision scored = planner.ChooseEngagementOption(
            shooters,
            paired.Frames[shooters.Id],
            paired.Profiles,
            paired.Frames,
            [enemies],
            constraint.RoleTargets);
        SquadEngagementDecision selected = scored with
        {
            Chosen = scored.Candidates.Single(candidate => candidate.Kind == (pursuit
                ? EngagementOptionKind.JogToward
                : EngagementOptionKind.Hold))
        };
        planner.DeclareEngagementDecision(selected);
        planner.BuildEngagementActions(selected);

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

    [Fact]
    public void SeedAmbushAim_RangedAmbushers_OpenAtFullAimAndSpreadAcrossTargets()
    {
        // West-leg ambush: three ambushers stacked along Y at x=0, firing east at a parallel
        // enemy column at x=10. Each ambusher's own firing lane lines up with a different enemy.
        BattleSquad ambushers = CreateSquad(
            "Ambushers", (70_001, 2), (70_002, 2), (70_003, 2));
        BattleSquad enemies = CreateSquad(
            "Column", (70_101, 2), (70_102, 2), (70_103, 2));
        BattleGridManager grid = new();
        for (int i = 0; i < ambushers.Soldiers.Count; i++)
        {
            BattleSoldier ambusher = ambushers.Soldiers[i];
            ((Soldier)ambusher.Soldier).Dexterity = 20;
            EquipAimTestRifle(ambusher, 99_501 + i);
            Place(grid, ambusher, true, 0, i * 2);
        }
        for (int i = 0; i < enemies.Soldiers.Count; i++)
        {
            Place(grid, enemies.Soldiers[i], false, 10, i * 2);
        }
        List<IAction> shootActions = [];
        BattleSquadPlanner planner = CreatePlanner(
            grid, shootActions, [], [], ambushers, enemies);

        planner.SeedAmbushAim(ambushers);

        HashSet<int> enemyIds = enemies.Soldiers.Select(s => s.Soldier.Id).ToHashSet();
        foreach (BattleSoldier ambusher in ambushers.Soldiers)
        {
            Assert.NotNull(ambusher.Aim);
            // Seeded to the planner's "aim can no longer be improved" ceiling.
            Assert.Equal(3, ambusher.Aim.Value.Item3);
            Assert.Equal(0f, ambusher.CurrentSpeed);
            Assert.Contains(ambusher.Aim.Value.Item1, enemyIds);
            Assert.Contains(ambusher.Aim.Value.Item2, ambusher.EquippedRangedWeapons);
        }
        // Lane-spread acquisition distributes the opening volley instead of piling every rifle
        // onto the nearest man.
        int distinctSeededTargets = ambushers.Soldiers
            .Select(s => s.Aim.Value.Item1)
            .Distinct()
            .Count();
        Assert.True(distinctSeededTargets >= 2,
            $"expected ambushers to spread aim; all pointed at {distinctSeededTargets} target(s)");

        // Turn one: the seeded ambushers open fire rather than spending the turn lining up.
        planner.PrepareActions(ambushers);
        Assert.Empty(shootActions.OfType<AimAction>());
        List<ShootAction> shots = shootActions.OfType<ShootAction>().ToList();
        Assert.True(shots.Count >= 2, $"expected an opening volley, got {shots.Count} shots");
        Assert.True(shots.Select(shot => shot.TargetId).Distinct().Count() >= 2,
            "expected the opening volley to hit more than one target");
    }

    [Fact]
    public void SeedAmbushAim_PlayerFactionAmbushers_CreditAimingExperience()
    {
        Faction player = BuildFaction(1, "Test Chapter", isPlayer: true);
        BattleSquad ambushers = CreateFactionSquad(
            "Player Ambushers", player, (71_001, 2), (71_002, 2));
        BattleSquad enemies = CreateSquad("Targets", (71_101, 2), (71_102, 2));
        BattleGridManager grid = new();
        for (int i = 0; i < ambushers.Soldiers.Count; i++)
        {
            BattleSoldier ambusher = ambushers.Soldiers[i];
            ((Soldier)ambusher.Soldier).Dexterity = 20;
            EquipAimTestRifle(ambusher, 99_601 + i);
            Place(grid, ambusher, true, 0, i * 2);
        }
        for (int i = 0; i < enemies.Soldiers.Count; i++)
        {
            Place(grid, enemies.Soldiers[i], false, 10, i * 2);
        }
        BattleSquadPlanner planner = CreatePlanner(grid, ambushers, enemies);

        planner.SeedAmbushAim(ambushers);

        // Ambushers open the battle already aimed: SeedAmbushAim credits pre-trap aim turns so the
        // opening volley fires at full aim bonus. (Aim no longer grants skill XP directly — battle
        // skill XP is roll-based now — but the seeded TurnsAiming still drives the aimed opening shot.)
        Assert.All(ambushers.Soldiers,
            soldier => Assert.Equal((ushort)3, soldier.TurnsAiming));
    }

    [Fact]
    public void SeedAmbushAim_NonPlayerAmbushers_DoNotAccrueAimingExperience()
    {
        BattleSquad ambushers = CreateSquad("Enemy Ambushers", (72_001, 2), (72_002, 2));
        BattleSquad enemies = CreateSquad("Prey", (72_101, 2), (72_102, 2));
        BattleGridManager grid = new();
        for (int i = 0; i < ambushers.Soldiers.Count; i++)
        {
            BattleSoldier ambusher = ambushers.Soldiers[i];
            ((Soldier)ambusher.Soldier).Dexterity = 20;
            EquipAimTestRifle(ambusher, 99_701 + i);
            Place(grid, ambusher, true, 0, i * 2);
        }
        for (int i = 0; i < enemies.Soldiers.Count; i++)
        {
            Place(grid, enemies.Soldiers[i], false, 10, i * 2);
        }
        BattleSquadPlanner planner = CreatePlanner(grid, ambushers, enemies);

        planner.SeedAmbushAim(ambushers);

        // Aim is still seeded (they fire fully-aimed), but no aftermath policy pays out the
        // counter for a non-player faction, so it stays clean.
        Assert.All(ambushers.Soldiers, soldier => Assert.NotNull(soldier.Aim));
        Assert.All(ambushers.Soldiers,
            soldier => Assert.Equal((ushort)0, soldier.TurnsAiming));
    }

    [Fact]
    public void SeedAmbushAim_MeleeOnlyAmbusher_KeepsNullAim()
    {
        BattleSquad ambushers = CreateSquad("Melee Ambushers", 73_001);
        BattleSquad enemies = CreateSquad("Prey", 73_101);
        BattleSoldier ambusher = ambushers.Soldiers[0];
        ambusher.RangedWeapons.Clear();
        ambusher.ClearReadiedRangedWeapons();
        BattleGridManager grid = new();
        Place(grid, ambusher, true, 0, 0);
        Place(grid, enemies.Soldiers[0], false, 5, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, ambushers, enemies);

        planner.SeedAmbushAim(ambushers);

        Assert.Null(ambusher.Aim);
        Assert.Equal((ushort)0, ambusher.TurnsAiming);
    }

    // Builds an ambushing squad on a dedicated template carrying the given faction, so
    // SeedAmbushAim can read squad.Squad.Faction.IsPlayerFaction. Reuses the fixture's default
    // weapon set and armor (AllocateEquipment requires both) before the aim-test rifle is swapped
    // in by the caller.
    private static BattleSquad CreateFactionSquad(
        string name,
        Faction faction,
        params (int SoldierId, int BattleValue)[] members)
    {
        SquadTemplate template = new(
            40_000 + faction.Id,
            $"{name} Template",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(name, null, template);
        foreach ((int soldierId, int battleValue) in members)
        {
            SoldierTemplate soldierTemplate = new(
                40_000 + soldierId,
                TestModelFactory.HumanSpecies,
                $"{name} {soldierId} Template",
                1,
                1,
                false,
                0,
                Array.Empty<ValueTuple<BaseSkill, float>>(),
                battleValue: battleValue);
            Soldier soldier = TestModelFactory.CreateSoldier(soldierTemplate, $"{name} {soldierId}");
            soldier.Id = soldierId;
            squad.AddSquadMember(soldier);
        }
        return new BattleSquad(false, squad);
    }

    private static Faction BuildFaction(int id, string name, bool isPlayer)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefaultFaction: false,
            behavior: FactionBehavior.None,
            GrowthType.Logistic,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
