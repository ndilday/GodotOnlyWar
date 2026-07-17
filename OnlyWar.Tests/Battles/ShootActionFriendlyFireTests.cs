using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class ShootActionFriendlyFireTests
{
    [Fact]
    public void CalculateToHitModifiers_FiringIntoMeleeAppliesSharedFlatPenalty()
    {
        BattleSquad shooters = CreateSquad(true, (1, "Shooter"));
        BattleSquad targets = CreateSquad(false, (2, "Target"));
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier target = targets.Soldiers[0];
        ShootAction action = new(
            1,
            2,
            shooter.EquippedRangedWeapons[0].Template.Id,
            5,
            1,
            false,
            grid: null,
            new SeededRNG(1));
        float skill = shooter.Soldier.GetTotalSkillValue(shooter.EquippedRangedWeapons[0].Template.RelatedSkill);

        float cleanModifier = action.CalculateToHitModifiers(
            shooter, target, shooter.EquippedRangedWeapons[0], skill, false);
        float meleeModifier = action.CalculateToHitModifiers(
            shooter, target, shooter.EquippedRangedWeapons[0], skill, true);

        Assert.Equal(
            RangedFriendlyFireRules.FiringIntoMeleePenalty,
            meleeModifier - cleanModifier,
            precision: 4);
    }

    [Fact]
    public void CalculateToHitModifiers_WalkHalvesBulkAndAppliedAimBonus()
    {
        BattleSquad shooters = CreateSquad(true, (1, "Shooter"));
        BattleSquad targets = CreateSquad(false, (2, "Target"));
        BattleSoldier shooter = shooters.Soldiers[0];
        BattleSoldier target = targets.Soldiers[0];
        var weapon = shooter.EquippedRangedWeapons[0];
        shooter.Aim = new Tuple<int, Models.Equippables.RangedWeapon, int>(
            target.Soldier.Id,
            weapon,
            0);
        float skill = shooter.Soldier.GetTotalSkillValue(weapon.Template.RelatedSkill);
        float fullAimBonus = System.Math.Min(weapon.Template.Accuracy, skill) + 1;

        ShootAction stationary = new(
            1, 2, weapon.Template.Id, 5, 1,
            bulkMultiplier: 0,
            aimMultiplier: 1,
            grid: null,
            new SeededRNG(1));
        ShootAction walking = new(
            1, 2, weapon.Template.Id, 5, 1,
            bulkMultiplier: 0.5f,
            aimMultiplier: 0.5f,
            grid: null,
            new SeededRNG(1));
        ShootAction jogging = new(
            1, 2, weapon.Template.Id, 5, 1,
            bulkMultiplier: 1,
            aimMultiplier: 0,
            grid: null,
            new SeededRNG(1));

        float stationaryModifier = stationary.CalculateToHitModifiers(
            shooter, target, weapon, skill, false);
        float walkingModifier = walking.CalculateToHitModifiers(
            shooter, target, weapon, skill, false);
        float joggingModifier = jogging.CalculateToHitModifiers(
            shooter, target, weapon, skill, false);

        Assert.Equal(
            stationaryModifier - (fullAimBonus * 0.5f) - (weapon.Template.Bulk * 0.5f),
            walkingModifier,
            precision: 4);
        Assert.Equal(
            stationaryModifier - fullAimBonus - weapon.Template.Bulk,
            joggingModifier,
            precision: 4);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-0.5, true)]
    [InlineData(-1, true)]
    [InlineData(0.01, false)]
    [InlineData(-1.01, false)]
    public void IsNearMiss_UsesNarrowBandImmediatelyBelowHit(float hitTotal, bool expected)
    {
        Assert.Equal(expected, RangedFriendlyFireRules.IsNearMiss(hitTotal));
    }

    [Fact]
    public void CalculateNearMissProbability_MatchesSharedBandAndThreeSigmaRoll()
    {
        float probability = RangedFriendlyFireRules.CalculateNearMissProbability(10.5f);

        // Roll must fall from z=0 through z=1/3.
        float expected = GaussianCalculator.ApproximateNormalCDF(1f / 3f) - 0.5f;
        Assert.Equal(expected, probability, precision: 5);
    }

    [Fact]
    public void StrayDistribution_WeightsNominalTargetAndOtherParticipantsBySize()
    {
        BattleSoldier target = CreateLooseSoldier(1, "Carnifex", size: 8);
        BattleSoldier ally = CreateLooseSoldier(2, "Marine", size: 1);
        BattleSoldier otherEnemy = CreateLooseSoldier(3, "Gaunt", size: 1);
        BattleSoldier[] participants = [target, ally, otherEnemy];

        Assert.Equal(0.8f,
            RangedFriendlyFireRules.CalculateStrayTargetProbability(target, participants),
            precision: 4);
        Assert.Same(target, RangedFriendlyFireRules.SelectStrayTarget(participants, 0.79));
        Assert.Same(ally, RangedFriendlyFireRules.SelectStrayTarget(participants, 0.80));
        Assert.Same(otherEnemy, RangedFriendlyFireRules.SelectStrayTarget(participants, 0.95));
    }

    [Fact]
    public void Execute_NearMissIntoMeleeResolvesAgainstSizeWeightedScrumParticipant()
    {
        BattleSquad shooters = CreateSquad(true, (1, "Shooter"), (2, "Friendly Marine"));
        BattleSquad targets = CreateSquad(false, (3, "Target"));
        BattleGridManager grid = new();
        Place(grid, shooters.Soldiers[0], true, 0, 0);
        Place(grid, shooters.Soldiers[1], true, 10, 0);
        Place(grid, targets.Soldiers[0], false, 11, 0);
        BattleState state = CreateState(shooters, targets);

        ShootAction nearMiss = FindNearMissAction(state, grid, shooters.Soldiers[0], targets.Soldiers[0], out int seed);
        Assert.NotNull(nearMiss.StrayTargetId);
        Assert.Contains(nearMiss.StrayTargetId.Value, new[] { 2, 3 });
        Assert.All(nearMiss.WoundResolutions,
            wound => Assert.Equal(nearMiss.StrayTargetId.Value, wound.Suffererer.Soldier.Id));

        ushort turnsShooting = state.GetSoldier(1).TurnsShooting;
        int woundCount = nearMiss.WoundResolutions.Count;
        nearMiss.Execute(state);
        Assert.Equal(turnsShooting, state.GetSoldier(1).TurnsShooting);
        Assert.Equal(woundCount, nearMiss.WoundResolutions.Count);

        // With the same attack roll but no scrum penalty, the total is three points higher
        // and is therefore a normal hit rather than a near miss.
        ShootAction cleanShot = CreateAction(
            shooters.Soldiers[0],
            targets.Soldiers[0],
            grid: null,
            new SeededRNG(seed));
        cleanShot.Execute(state);
        Assert.Null(cleanShot.StrayTargetId);

        string description = nearMiss.Description();
        Assert.Contains("misses its mark", description);
        Assert.Contains(nearMiss.StrayTargetId == 2 ? "friendly fire" : "a stray hit", description);
    }

    private static ShootAction FindNearMissAction(
        BattleState state,
        BattleGridManager grid,
        BattleSoldier shooter,
        BattleSoldier target,
        out int matchingSeed)
    {
        for (int seed = 0; seed < 10_000; seed++)
        {
            ShootAction action = CreateAction(shooter, target, grid, new SeededRNG(seed));
            action.Execute(state);
            if (action.StrayTargetId.HasValue)
            {
                matchingSeed = seed;
                return action;
            }
        }

        throw new InvalidOperationException("No deterministic near-miss seed found.");
    }

    private static ShootAction CreateAction(
        BattleSoldier shooter,
        BattleSoldier target,
        BattleGridManager grid,
        IRNG random)
    {
        return new ShootAction(
            shooter.Soldier.Id,
            target.Soldier.Id,
            shooter.EquippedRangedWeapons[0].Template.Id,
            range: 1,
            numberOfShots: 1,
            useBulk: false,
            grid: grid,
            random);
    }

    private static BattleSquad CreateSquad(bool isPlayerSquad, params (int Id, string Name)[] members)
    {
        Soldier[] soldiers = members.Select(member =>
        {
            Soldier soldier = TestModelFactory.CreateSoldier(name: member.Name);
            soldier.Id = member.Id;
            return soldier;
        }).ToArray();
        return new BattleSquad(isPlayerSquad, TestModelFactory.CreateSquad($"Squad {members[0].Id}", soldiers));
    }

    private static BattleSoldier CreateLooseSoldier(int id, string name, float size)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name);
        soldier.Id = id;
        soldier.Size = size;
        return new BattleSoldier(soldier, null);
    }

    private static BattleState CreateState(BattleSquad shooters, BattleSquad targets)
    {
        return new BattleState(
            new Dictionary<int, BattleSquad> { [1] = shooters },
            new Dictionary<int, BattleSquad> { [2] = targets });
    }

    private static void Place(
        BattleGridManager grid,
        BattleSoldier soldier,
        bool side,
        int x,
        int y)
    {
        soldier.TopLeft = new Tuple<int, int>(x, y);
        soldier.Orientation = 0;
        grid.PlaceSoldier(soldier, side, soldier.PositionList.ToList());
    }
}
