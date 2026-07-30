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

public class BattlePursuitActionPlannerTests
{
    [Fact]
    public void Follow_JogsTowardNearestWithdrawerAndFiresOnlyWithinJogArc()
    {
        BattleSquad pursuer = CreateSquad("Pursuer", 72_001);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_002);
        BattleSquad closerNonTarget = CreateSquad("Non-target", 72_003);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 10, 0),
            (closerNonTarget, false, -3, 0));

        fixture.Planner.PreparePursuitActions(
            pursuer,
            PursuitPosture.Follow,
            [withdrawing]);

        Assert.Equal(SquadMovementTier.Jog, pursuer.MovementTier);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        Assert.Matches(@"to \([1-9]\d*, -?\d+\)", move.Description());
        ShootAction shot = Assert.IsType<ShootAction>(Assert.Single(fixture.ShootActions));
        Assert.Equal(withdrawing.Soldiers[0].Soldier.Id, shot.TargetId);
    }

    [Fact]
    public void Follow_RunsToRegainRange_WhenNoWorthwhileShotExists()
    {
        // The withdrawer is far beyond the test rifle's 100-yd maximum range: a jog-and-fire
        // follow would just fall further behind, so the squad sprints to regain effective
        // range instead of shooting at nothing.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_031);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_032);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 400, 0));

        fixture.Planner.PreparePursuitActions(
            pursuer,
            PursuitPosture.Follow,
            [withdrawing]);

        Assert.Equal(SquadMovementTier.Run, pursuer.MovementTier);
        Assert.Empty(fixture.ShootActions);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        Assert.Matches(@"to \([1-9]\d*, -?\d+\)", move.Description());
    }

    [Fact]
    public void Standoff_HoldsPositionAndBringsTheGunsToBearWithoutMoving()
    {
        // The chase is unwinnable, so the squad plants itself and works the target with
        // stationary fire — no Bulk penalty, and the aim bonus is allowed to accumulate —
        // rather than jogging after a quarry it cannot catch.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_041);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_042);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 10, 0));

        fixture.Planner.PreparePursuitActions(
            pursuer,
            PursuitPosture.Standoff,
            [withdrawing]);

        Assert.Equal(SquadMovementTier.Stationary, pursuer.MovementTier);
        Assert.Empty(fixture.MoveActions);
        Assert.Empty(fixture.MeleeActions);
        // Aiming and firing both land in the shoot segment; which one the planner picks this
        // turn is its own business, but it must be engaging the withdrawer either way.
        Assert.NotEmpty(fixture.ShootActions);
    }

    [Fact]
    public void Press_RunsTowardNearestWithdrawerWithoutShooting()
    {
        BattleSquad pursuer = CreateSquad("Pursuer", 72_011);
        BattleSquad farther = CreateSquad("Far", 72_012);
        BattleSquad nearerRearGuard = CreateSquad("Rear Guard", 72_013);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (farther, false, 20, 0),
            (nearerRearGuard, false, 8, 0));

        fixture.Planner.PreparePursuitActions(
            pursuer,
            PursuitPosture.Press,
            [farther, nearerRearGuard]);

        Assert.Equal(SquadMovementTier.Run, pursuer.MovementTier);
        Assert.Empty(fixture.ShootActions);
        MoveAction move = Assert.IsType<MoveAction>(Assert.Single(fixture.MoveActions));
        Assert.Matches(@"to \([1-9]\d*, -?\d+\)", move.Description());
    }

    [Fact]
    public void Press_AttacksTheWithdrawerItHasCaughtInsteadOfRunningOnTheSpot()
    {
        // Press exists to convert contact into damage. Adjacent to the quarry it swings; without
        // this the posture closed the distance and then jogged in place beside the enemy forever,
        // so a fast element could never pin anyone for the slow element to catch up to.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_051);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_052);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 1, 0));

        fixture.Planner.PreparePursuitActions(
            pursuer,
            PursuitPosture.Press,
            [withdrawing]);

        // Which attack it makes is the engaged-soldier decision's business — a rifleman standing
        // on his quarry may well shoot point blank rather than club him. What matters is that
        // reaching the enemy produces an attack at all instead of another stride.
        Assert.NotEmpty(fixture.MeleeActions.Concat(fixture.ShootActions));
        Assert.Empty(fixture.MoveActions);
    }

    [Fact]
    public void Press_ChargesIntoContactWhenTheWithdrawerIsWithinOneMove()
    {
        // Just out of contact but inside a Run: the pursuer closes and gets stuck in the same
        // turn rather than stopping politely one pace short.
        BattleSquad pursuer = CreateSquad("Pursuer", 72_061);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_062);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 4, 0));

        fixture.Planner.PreparePursuitActions(
            pursuer,
            PursuitPosture.Press,
            [withdrawing]);

        // It closes and gets stuck in on the same turn: a move plus an attack, not a bare stride
        // that stops one pace short and repeats forever.
        Assert.NotEmpty(fixture.MoveActions);
        Assert.NotEmpty(fixture.MeleeActions.Concat(fixture.ShootActions));
    }

    [Fact]
    public void BreakOff_HoldsAndCreatesNoCombatActions()
    {
        BattleSquad pursuer = CreateSquad("Pursuer", 72_021);
        BattleSquad withdrawing = CreateSquad("Withdrawer", 72_022);
        Fixture fixture = CreateFixture(
            (pursuer, true, 0, 0),
            (withdrawing, false, 8, 0));

        fixture.Planner.PreparePursuitActions(
            pursuer,
            PursuitPosture.BreakOff,
            [withdrawing]);

        Assert.Equal(SquadMovementTier.Stationary, pursuer.MovementTier);
        Assert.Empty(fixture.MoveActions);
        Assert.Empty(fixture.ShootActions);
        Assert.Empty(fixture.MeleeActions);
    }

    private static BattleSquad CreateSquad(string name, int soldierId)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(
            name: name,
            dexterity: 18,
            skills: [new Skill(TestSkills.Ranged, 12)]);
        soldier.Id = soldierId;
        return new BattleSquad(false, TestModelFactory.CreateSquad(name, soldier));
    }

    private static Fixture CreateFixture(
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
            new SeededRNG(72_000));
        return new Fixture(planner, shootActions, moveActions, meleeActions);
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

    private sealed record Fixture(
        BattleSquadPlanner Planner,
        List<IAction> ShootActions,
        List<IAction> MoveActions,
        List<IAction> MeleeActions);
}
