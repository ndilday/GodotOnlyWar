using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Helpers;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;

using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleForcePlannerTests
{
    [Fact]
    public void SelectWithdrawalHeading_PointsFromEnemyCentroidToFriendlyCentroid()
    {
        BattleSquad friendly = CreateSquad("Friendly", 71_001);
        BattleSquad enemy = CreateSquad("Enemy", 71_002);
        friendly.Soldiers[0].TopLeft = (10, 0);
        enemy.Soldiers[0].TopLeft = (0, 0);

        ushort heading = BattleForcePlanner.SelectWithdrawalHeading([friendly], [enemy]);

        Assert.Equal((ushort)2, heading);
    }

    [Fact]
    public void SelectWithdrawalHeading_OverlappingCentroidsUsesLowestHeadingOnExactTie()
    {
        BattleSquad friendly = CreateSquad("Friendly", 71_011);
        BattleSquad enemy = CreateSquad("Enemy", 71_012);
        friendly.Soldiers[0].TopLeft = (0, 0);
        enemy.Soldiers[0].TopLeft = (0, 0);

        ushort heading = BattleForcePlanner.SelectWithdrawalHeading([friendly], [enemy]);

        Assert.Equal((ushort)0, heading);
    }

    [Fact]
    public void SelectCover_InitiallyChoosesFarthestEligibleWithLowestIdTieBreak()
    {
        BattleForcePlanner.CoverAssignment assignment = BattleForcePlanner.SelectCover(
        [
            new(30, 12, true),
            new(20, 12, true),
            new(10, 4, true)
        ], null);

        Assert.Equal(20, assignment.SquadId);
        Assert.False(assignment.Rotated);
        Assert.Equal("farthest_eligible", assignment.Reason);
    }

    [Fact]
    public void SelectCover_KeepsIncumbentUntilItBecomesClosestThenRotates()
    {
        BattleForcePlanner.CoverAssignment held = BattleForcePlanner.SelectCover(
        [
            new(10, 8, true),
            new(20, 4, true)
        ], 10);
        BattleForcePlanner.CoverAssignment rotated = BattleForcePlanner.SelectCover(
        [
            new(10, 3, true),
            new(20, 9, true)
        ], 10);

        Assert.Equal(10, held.SquadId);
        Assert.False(held.Rotated);
        Assert.Equal(20, rotated.SquadId);
        Assert.True(rotated.Rotated);
        Assert.Equal("incumbent_became_closest", rotated.Reason);
    }

    [Fact]
    public void PrepareFightingWithdrawal_FixesHeadingAndAssignsCoverAndBoundRoles()
    {
        BattleSquad near = CreateSquad("Near", 71_021);
        BattleSquad far = CreateSquad("Far", 71_022);
        BattleSquad enemy = CreateSquad("Enemy", 71_023);
        PlannerFixture fixture = CreatePlannerFixture(
            (near, true, 8, 0),
            (far, true, 14, 0),
            (enemy, false, 0, 0));
        BattleSideState side = SideState();
        BattleForcePlanner planner = new(fixture.SquadPlanner);

        BattleForcePlanner.CoverAssignment result = planner.PrepareFightingWithdrawal(
            side, [near, far], [enemy]);

        Assert.Equal((ushort)2, side.WithdrawalHeading);
        Assert.Equal(far.Id, result.SquadId);
        Assert.Equal(WithdrawalRole.Cover, far.WithdrawalRole);
        Assert.Equal(SquadMovementTier.Stationary, far.MovementTier);
        Assert.Equal(WithdrawalRole.Bound, near.WithdrawalRole);
        Assert.Equal(SquadMovementTier.Run, near.MovementTier);
        Assert.Single(fixture.MoveActions);
        Assert.DoesNotContain(fixture.ShootActions, action => action.ActorId == near.Soldiers[0].Soldier.Id);

        side.WithdrawalHeading = 4;
        planner.PrepareFightingWithdrawal(side, [near, far], [enemy]);
        Assert.Equal((ushort)4, side.WithdrawalHeading);
    }

    [Fact]
    public void PrepareFightingWithdrawal_SingleSquadRunsInsteadOfCovering()
    {
        // A lone squad has no main body to cover. Designating it as cover would make it stand
        // and fire while nobody withdraws, even though the enemy has already begun pursuit off
        // the withdrawal order. It falls back to the unsupported withdrawal: it Runs.
        BattleSquad lone = CreateSquad("Lone", 71_061);
        BattleSquad enemy = CreateSquad("Enemy", 71_062);
        PlannerFixture fixture = CreatePlannerFixture(
            (lone, true, 10, 0),
            (enemy, false, 0, 0));
        BattleSideState side = SideState();
        BattleForcePlanner planner = new(fixture.SquadPlanner);

        BattleForcePlanner.CoverAssignment result = planner.PrepareFightingWithdrawal(
            side, [lone], [enemy]);

        Assert.Null(result.SquadId);
        Assert.Equal("single_squad_unsupported", result.Reason);
        Assert.Null(side.CoveringSquadId);
        Assert.Equal(WithdrawalRole.Bound, lone.WithdrawalRole);
        Assert.Equal(SquadMovementTier.Run, lone.MovementTier);
        Assert.Single(fixture.MoveActions);
        Assert.DoesNotContain(
            fixture.ShootActions,
            action => action.ActorId == lone.Soldiers[0].Soldier.Id);
    }

    [Fact]
    public void PrepareBoundActions_RunsAlongHeadingWithoutAttacking()
    {
        BattleSquad bound = CreateSquad("Bound", 71_031);
        BattleSquad enemy = CreateSquad("Enemy", 71_032);
        PlannerFixture fixture = CreatePlannerFixture(
            (bound, true, 0, 0),
            (enemy, false, 0, 10));

        fixture.SquadPlanner.PrepareBoundActions(bound, withdrawalHeading: 2);

        Assert.Equal(WithdrawalRole.Bound, bound.WithdrawalRole);
        Assert.Equal(SquadMovementTier.Run, bound.MovementTier);
        Assert.Empty(fixture.ShootActions);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        Assert.Matches(@"to \([1-9]\d*, -?\d+\)", move.Description());
    }

    [Fact]
    public void PrepareCoverActions_HoldsPositionAndUsesStandingFirePlanner()
    {
        BattleSquad cover = CreateSquad("Cover", 71_041);
        BattleSquad enemy = CreateSquad("Enemy", 71_042);
        PlannerFixture fixture = CreatePlannerFixture(
            (cover, true, 0, 0),
            (enemy, false, 0, 8));

        fixture.SquadPlanner.PrepareCoverActions(cover);

        Assert.Equal(WithdrawalRole.Cover, cover.WithdrawalRole);
        Assert.Equal(SquadMovementTier.Stationary, cover.MovementTier);
        Assert.Empty(fixture.MoveActions);
        Assert.NotEmpty(fixture.ShootActions);
    }

    [Fact]
    public void PrepareRearGuardActions_HoldsUnlessAlreadyInMelee()
    {
        BattleSquad rearGuard = CreateSquad("Rear Guard", 71_051);
        BattleSquad enemy = CreateSquad("Enemy", 71_052);
        PlannerFixture fixture = CreatePlannerFixture(
            (rearGuard, true, 0, 0),
            (enemy, false, 0, 8));

        fixture.SquadPlanner.PrepareRearGuardActions(rearGuard);

        Assert.Equal(WithdrawalRole.RearGuard, rearGuard.WithdrawalRole);
        Assert.Equal(SquadMovementTier.Stationary, rearGuard.MovementTier);
        Assert.Empty(fixture.MoveActions);
        Assert.NotEmpty(fixture.ShootActions);
    }

    private static BattleSideState SideState() => new(
        new BattleSideProfile(Aggression.Normal, BattleRole.Defender),
        startingBattleValue: 10,
        startingSoldierCount: 2);

    private static BattleSquad CreateSquad(string name, int soldierId)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name);
        soldier.Id = soldierId;
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldier));
    }

    private static PlannerFixture CreatePlannerFixture(
        params (BattleSquad Squad, bool Side, int X, int Y)[] placements)
    {
        BattleGridManager grid = new();
        foreach ((BattleSquad squad, bool side, int x, int y) in placements)
        {
            BattleSoldier soldier = squad.Soldiers[0];
            soldier.TopLeft = new ValueTuple<int, int>(x, y);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft.Value]);
        }

        Dictionary<int, BattleSoldier> soldiers = placements
            .SelectMany(placement => placement.Squad.Soldiers)
            .ToDictionary(soldier => soldier.Soldier.Id);
        List<IAction> shootActions = [];
        List<IAction> moveActions = [];
        List<IAction> meleeActions = [];
        BattleSquadPlanner planner = new(
            grid,
            soldiers,
            shootActions,
            moveActions,
            meleeActions,
            null,
            CreateMeleeTemplateMap(soldiers.Values),
            new SeededRNG(71_000));
        return new PlannerFixture(planner, shootActions, moveActions, meleeActions);
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

    private sealed record PlannerFixture(
        BattleSquadPlanner SquadPlanner,
        List<IAction> ShootActions,
        List<IAction> MoveActions,
        List<IAction> MeleeActions);
}
