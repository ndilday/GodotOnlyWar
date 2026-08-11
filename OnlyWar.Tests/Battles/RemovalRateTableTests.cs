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

/// <summary>
/// Phase 4 of Design/Reference/BattleLogic.md: the per-(shooter squad, target squad)
/// removal-rate table and its closed-form range rescaling. These tests cover the table's
/// direct contract and caching behavior; engagement-planning tests cover its use by the planner.
/// </summary>
public class RemovalRateTableTests
{
    private static BattleSquad CreateSquad(
        string name,
        params (int SoldierId, int BattleValue)[] members)
    {
        List<Soldier> soldiers = members
            .Select(member =>
            {
                SoldierTemplate template = new(
                    70_000 + member.SoldierId,
                    TestModelFactory.HumanSpecies,
                    $"{name} {member.SoldierId} Template",
                    1,
                    1,
                    false,
                    0,
                    Array.Empty<ValueTuple<BaseSkill, float>>(),
                    battleValue: member.BattleValue);
                Soldier soldier = TestModelFactory.CreateSoldier(
                    template, $"{name} {member.SoldierId}");
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
            new List<IAction>(),
            new List<IAction>(),
            new List<IAction>(),
            null,
            meleeTemplates,
            new SeededRNG(12345));
    }

    private static RangedWeapon EquipRifle(
        BattleSoldier soldier,
        int templateId,
        bool degrades,
        float maximumRange,
        float baseDamage)
    {
        RangedWeapon rifle = new(new RangedWeaponTemplate(
            templateId,
            degrades ? "Degrading Table Rifle" : "Flat Table Rifle",
            EquipLocation.TwoHand,
            TestSkills.Ranged,
            accuracy: 0,
            armorMultiplier: 1,
            penetrationMultiplier: 1,
            requiredStrength: 0,
            baseDamage: baseDamage,
            maxDistance: maximumRange,
            // Rate of fire 1 pins CalculateShotsToFire, so the rate-of-fire modifier baked into
            // the cached to-hit total cannot change with range -- see the documented
            // fixed-shot-count approximation on PairRemovalTerm.
            rof: 1,
            ammo: 10,
            recoil: 0,
            bulk: 0,
            doesDamageDegradeWithRange: degrades,
            reloadTime: 1));
        soldier.RangedWeapons.Clear();
        soldier.ClearReadiedRangedWeapons();
        soldier.RangedWeapons.Add(rifle);
        soldier.ReadyWeapon(rifle);
        return rifle;
    }

    [Fact]
    public void RescaledHitProbability_MatchesDirectEvaluationForNonDegradingWeapon()
    {
        BattleSquad shooters = CreateSquad("Shooter", (80_100, 10));
        BattleSquad enemies = CreateSquad("Enemy", (80_200, 10));
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier target = enemies.Soldiers[0];
        RangedWeapon rifle = EquipRifle(
            shooter, 99_400, degrades: false, maximumRange: 1_000, baseDamage: 6);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, target, false, 20, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, enemies);

        SquadPairRemovalRate rate = planner.GetPairRemovalRates(shooters)[enemies.Id];
        PairRemovalTerm term = Assert.Single(rate.Terms);
        float referenceSpeed = term.ReferenceTargetSpeed;

        foreach (float range in new[] { 3f, 10f, 20f, 75f, 250f, 900f })
        {
            RangedTargetEvaluation direct = planner.EvaluateRangedTarget(
                shooter, target, rifle, range, 0f, referenceSpeed);

            Assert.Equal(direct.HitProbability, term.HitProbabilityAt(range, referenceSpeed), 5);
            // Non-degrading: CalculateDamageAtRange is a flat DamageMultiplier, so the cached
            // take-out is not an approximation of anything -- it is the value at every range.
            Assert.Equal(direct.TakeOutProbabilityOnHit, term.TakeOutProbabilityAt(range), 6);
            Assert.Equal(direct.ExpectedEnemyBattleValueRemoved, term.RemovalAt(range), 5);
        }
    }

    [Fact]
    public void RescaledHitProbability_IsExactlyTheDirectValueAtTheReferenceRange()
    {
        BattleSquad shooters = CreateSquad("Shooter", (80_110, 10));
        BattleSquad enemies = CreateSquad("Enemy", (80_210, 10));
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier target = enemies.Soldiers[0];
        RangedWeapon rifle = EquipRifle(
            shooter, 99_401, degrades: false, maximumRange: 1_000, baseDamage: 6);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, target, false, 37, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, enemies);

        SquadPairRemovalRate rate = planner.GetPairRemovalRates(shooters)[enemies.Id];
        PairRemovalTerm term = Assert.Single(rate.Terms);
        RangedTargetEvaluation direct = planner.EvaluateRangedTarget(
            shooter, target, rifle, term.ReferenceRange, 0f, term.ReferenceTargetSpeed);

        // ln(1) is exactly 0, so the rescaling adds nothing at r0 and the two paths coincide bitwise.
        Assert.Equal(
            direct.HitProbability,
            term.HitProbabilityAt(term.ReferenceRange, term.ReferenceTargetSpeed));
    }

    [Fact]
    public void KLocVector_ReproducesTakeOutProbabilityExactlyAtEveryRange()
    {
        BattleSquad shooters = CreateSquad("Shooter", (80_120, 10));
        BattleSquad enemies = CreateSquad("Enemy", (80_220, 10));
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier target = enemies.Soldiers[0];
        RangedWeapon rifle = EquipRifle(
            shooter, 99_402, degrades: true, maximumRange: 200, baseDamage: 6);
        target.Armor = new Armor(TestModelFactory.TestArmor);

        float effectiveArmor = target.Armor.Template.ArmorProvided
            * rifle.Template.ArmorMultiplier;
        IReadOnlyList<TakeOutLocationTerm> terms =
            RemovalMath.BuildTakeOutLocationTerms(
                target, effectiveArmor, rifle.Template.WoundMultiplier);
        Assert.NotEmpty(terms);

        float previous = float.MaxValue;
        foreach (float range in new[] { 0f, 10f, 40f, 90f, 150f, 199f })
        {
            float damageCoefficient = BattleModifiersUtil.CalculateDamageAtRange(
                rifle.Template, range);
            float direct = RemovalMath.CalculateTakeOutProbabilityOnHit(
                target, damageCoefficient, effectiveArmor, rifle.Template.WoundMultiplier);
            float viaVector = RemovalMath.EvaluateTakeOutProbability(
                terms, damageCoefficient);

            // Same loop, same accumulation order: this is bitwise identity, not a tolerance.
            Assert.Equal(direct, viaVector);
            Assert.True(
                viaVector <= previous,
                $"take-out should not rise with range: {viaVector} > {previous} at {range}");
            previous = viaVector;
        }
        Assert.True(
            RemovalMath.EvaluateTakeOutProbability(
                terms,
                BattleModifiersUtil.CalculateDamageAtRange(rifle.Template, 0f)) > 0,
            "the degrading weapon should be able to take this target out at point blank");
    }

    [Fact]
    public void PairRemovalTerm_TracksDirectEvaluationForDegradingWeapon()
    {
        BattleSquad shooters = CreateSquad("Shooter", (80_130, 10));
        BattleSquad enemies = CreateSquad("Enemy", (80_230, 10));
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier target = enemies.Soldiers[0];
        RangedWeapon rifle = EquipRifle(
            shooter, 99_403, degrades: true, maximumRange: 200, baseDamage: 6);

        BattleGridManager grid = new();
        Place(grid, shooter, true, 0, 0);
        Place(grid, target, false, 30, 0);
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, enemies);

        SquadPairRemovalRate rate = planner.GetPairRemovalRates(shooters)[enemies.Id];
        PairRemovalTerm term = Assert.Single(rate.Terms);

        foreach (float range in new[] { 5f, 30f, 80f, 150f })
        {
            RangedTargetEvaluation direct = planner.EvaluateRangedTarget(
                shooter, target, rifle, range, 0f, term.ReferenceTargetSpeed);
            Assert.Equal(
                direct.TakeOutProbabilityOnHit, term.TakeOutProbabilityAt(range), 6);
            Assert.Equal(
                direct.ExpectedEnemyBattleValueRemoved, term.RemovalAt(range), 5);
        }
    }

    [Fact]
    public void PairRemovalRate_ReferenceRangeReproducesTheReferenceRateAndFallsOffWithRange()
    {
        BattleSquad shooters = CreateSquad(
            "Shooter", (80_140, 10), (80_141, 10), (80_142, 10));
        BattleSquad enemies = CreateSquad("Enemy", (80_240, 10), (80_241, 10));
        foreach (BattleSoldier shooter in shooters.Soldiers)
        {
            EquipRifle(shooter, 99_404, degrades: false, maximumRange: 1_000, baseDamage: 6);
        }

        BattleGridManager grid = new();
        for (int index = 0; index < shooters.Soldiers.Count; index++)
        {
            Place(grid, shooters.Soldiers[index], true, 0, index * 2);
        }
        for (int index = 0; index < enemies.Soldiers.Count; index++)
        {
            Place(grid, enemies.Soldiers[index], false, 60, index * 2);
        }
        BattleSquadPlanner planner = CreatePlanner(grid, shooters, enemies);

        SquadPairRemovalRate rate = planner.GetPairRemovalRates(shooters)[enemies.Id];

        Assert.Equal(shooters.Soldiers.Count, rate.Terms.Count);
        Assert.Equal(shooters.Id, rate.ShooterSquadId);
        Assert.Equal(enemies.Id, rate.TargetSquadId);
        Assert.Equal(rate.ReferenceRate, rate.RateAtRange(rate.ReferencePairRange), 5);
        Assert.True(rate.ReferenceRate > 0, "expected a positive removal rate");
        Assert.True(
            rate.RateAtRange(rate.ReferencePairRange * 4)
                < rate.RateAtRange(rate.ReferencePairRange),
            "removal should fall off as the projected separation grows");
        Assert.True(
            rate.RateAtRange(2f) > rate.RateAtRange(rate.ReferencePairRange),
            "removal should rise as the squads close");
    }

    [Fact]
    public void PairRemovalRateTable_IsMemoizedPerTurnAndSplitsByTargetSquad()
    {
        BattleSquad shooters = CreateSquad("Shooter", (80_150, 10), (80_151, 10));
        BattleSquad nearEnemies = CreateSquad("Near Enemy", (80_250, 10));
        BattleSquad farEnemies = CreateSquad("Far Enemy", (80_260, 10));
        foreach (BattleSoldier shooter in shooters.Soldiers)
        {
            EquipRifle(shooter, 99_405, degrades: false, maximumRange: 1_000, baseDamage: 6);
        }

        BattleGridManager grid = new();
        Place(grid, shooters.Soldiers[0], true, 0, 0);
        Place(grid, shooters.Soldiers[1], true, 0, 40);
        Place(grid, nearEnemies.Soldiers[0], false, 10, 0);
        Place(grid, farEnemies.Soldiers[0], false, 10, 40);
        BattleSquadPlanner planner = CreatePlanner(
            grid, shooters, nearEnemies, farEnemies);

        Assert.Equal(0, planner.CachedPairRemovalRowCount);
        IReadOnlyDictionary<int, SquadPairRemovalRate> first =
            planner.GetPairRemovalRates(shooters);
        IReadOnlyDictionary<int, SquadPairRemovalRate> second =
            planner.GetPairRemovalRates(shooters);

        Assert.Same(first, second);
        Assert.Equal(1, planner.CachedPairRemovalRowCount);
        // One cell per enemy squad a shooter is actually aimed into, and each soldier lands in
        // exactly one cell (per-soldier argmax, matching `outgoing`).
        Assert.Equal(2, planner.CachedPairRemovalRateCount);
        Assert.Equal(2, first.Count);
        Assert.Single(first[nearEnemies.Id].Terms);
        Assert.Single(first[farEnemies.Id].Terms);
        Assert.Same(first[nearEnemies.Id], second[nearEnemies.Id]);
    }

}
