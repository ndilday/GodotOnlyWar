using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class MoveActionTests
{
    [Fact]
    public void Execute_SuccessfulMoveBanksBudgetMinusEuclideanDisplacement()
    {
        BattleGridManager grid = new();
        BattleSoldier soldier = CreatePlacedSoldier(grid, id: 1, x: 0, y: 0);
        MoveAction action = new(
            soldier,
            grid,
            soldier.TopLeft,
            new Tuple<int, int>(1, 1),
            orientation: 0,
            movementBudget: 2.25f);

        action.Execute(state: null);

        Assert.Equal(new Tuple<int, int>(1, 1), soldier.TopLeft);
        Assert.Equal(2.25f - MathF.Sqrt(2), soldier.LeftoverMovement, precision: 4);
    }

    [Fact]
    public void Execute_BlockedMoveBanksFullBudgetWithoutMoving()
    {
        BattleGridManager grid = new();
        BattleSoldier soldier = CreatePlacedSoldier(grid, id: 1, x: 0, y: 0);
        CreatePlacedSoldier(grid, id: 2, x: 1, y: 0);
        MoveAction action = new(
            soldier,
            grid,
            soldier.TopLeft,
            new Tuple<int, int>(1, 0),
            orientation: 0,
            movementBudget: 2.75f);

        action.Execute(state: null);

        Assert.Equal(new Tuple<int, int>(0, 0), soldier.TopLeft);
        Assert.Equal(2.75f, soldier.LeftoverMovement);
        Assert.Equal(new Tuple<int, int>(0, 0), grid.GetSoldierPosition(soldier.Soldier.Id)[0]);
    }

    [Fact]
    public void Execute_SubsequentMoveCanSpendAccumulatedFractionalBudget()
    {
        BattleGridManager grid = new();
        BattleSoldier soldier = CreatePlacedSoldier(grid, id: 1, x: 0, y: 0);

        ExecuteMove(soldier, grid, new Tuple<int, int>(1, 0), movementBudget: 1.2f);
        Assert.Equal(0.2f, soldier.LeftoverMovement, precision: 4);

        ExecuteMove(soldier, grid, new Tuple<int, int>(2, 0), movementBudget: 1.2f + soldier.LeftoverMovement);
        Assert.Equal(0.4f, soldier.LeftoverMovement, precision: 4);

        ExecuteMove(soldier, grid, new Tuple<int, int>(3, 1), movementBudget: 1.2f + soldier.LeftoverMovement);

        Assert.Equal(new Tuple<int, int>(3, 1), soldier.TopLeft);
        Assert.Equal(1.6f - MathF.Sqrt(2), soldier.LeftoverMovement, precision: 4);
    }

    private static void ExecuteMove(
        BattleSoldier soldier,
        BattleGridManager grid,
        Tuple<int, int> destination,
        float movementBudget)
    {
        MoveAction action = new(
            soldier,
            grid,
            soldier.TopLeft,
            destination,
            orientation: 0,
            movementBudget);
        action.Execute(state: null);
    }

    private static BattleSoldier CreatePlacedSoldier(BattleGridManager grid, int id, int x, int y)
    {
        Soldier model = TestModelFactory.CreateSoldier(name: $"Mover {id}");
        model.Id = id;
        BattleSoldier soldier = new(model, squad: null)
        {
            TopLeft = new Tuple<int, int>(x, y),
            Orientation = 0
        };
        grid.PlaceSoldier(soldier, side: true, [soldier.TopLeft]);
        return soldier;
    }
}
