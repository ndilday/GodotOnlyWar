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
            soldier.TopLeft = new Tuple<int, int>(x, y);
            grid.PlaceSoldier(soldier, side, [soldier.TopLeft]);
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
