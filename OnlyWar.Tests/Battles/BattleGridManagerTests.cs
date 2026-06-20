using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleGridManagerTests
{
    private static BattleSoldier CreateBattleSoldier(int id)
    {
        Soldier soldier = TestModelFactory.CreateSoldier();
        soldier.Id = id;
        return new BattleSoldier(soldier, null);
    }

    private static List<Tuple<int, int>> Cell(int x, int y) => [new Tuple<int, int>(x, y)];

    [Fact]
    public void PlaceSoldier_RecordsPositionAndOccupiesCell()
    {
        BattleGridManager grid = new();
        BattleSoldier soldier = CreateBattleSoldier(1);

        grid.PlaceSoldier(soldier, true, Cell(2, 3));

        Assert.Equal(new Tuple<int, int>(2, 3), grid.GetSoldierPosition(1)[0]);
        Assert.False(grid.IsSpaceAvailable(new Tuple<int, int>(2, 3)));
    }

    [Fact]
    public void PlaceSoldier_ThrowsWhenSoldierAlreadyPlaced()
    {
        BattleGridManager grid = new();
        BattleSoldier soldier = CreateBattleSoldier(1);
        grid.PlaceSoldier(soldier, true, Cell(0, 0));

        Assert.Throws<InvalidOperationException>(
            () => grid.PlaceSoldier(soldier, true, Cell(5, 5)));
    }

    [Fact]
    public void PlaceSoldier_ThrowsWhenCellOccupied()
    {
        BattleGridManager grid = new();
        grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(4, 4));

        Assert.Throws<InvalidOperationException>(
            () => grid.PlaceSoldier(CreateBattleSoldier(2), false, Cell(4, 4)));
    }

    [Fact]
    public void PlaceSoldier_ThrowsWhenCellReserved()
    {
        BattleGridManager grid = new();
        grid.ReserveSpace(new Tuple<int, int>(7, 7));

        Assert.Throws<InvalidOperationException>(
            () => grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(7, 7)));
    }

    [Fact]
    public void MoveSoldier_FreesOldCellAndOccupiesNew()
    {
        BattleGridManager grid = new();
        BattleSoldier soldier = CreateBattleSoldier(1);
        grid.PlaceSoldier(soldier, true, Cell(0, 0));

        grid.MoveSoldier(soldier, new Tuple<int, int>(3, 3), 0);

        Assert.True(grid.IsSpaceAvailable(new Tuple<int, int>(0, 0)));
        Assert.Equal(new Tuple<int, int>(3, 3), grid.GetSoldierPosition(1)[0]);
    }

    [Fact]
    public void MoveSoldier_ThrowsWhenTargetOccupiedByAnother()
    {
        BattleGridManager grid = new();
        BattleSoldier mover = CreateBattleSoldier(1);
        grid.PlaceSoldier(mover, true, Cell(0, 0));
        grid.PlaceSoldier(CreateBattleSoldier(2), false, Cell(1, 0));

        Assert.Throws<InvalidOperationException>(
            () => grid.MoveSoldier(mover, new Tuple<int, int>(1, 0), 0));
    }

    [Fact]
    public void RemoveSoldier_FreesItsCell()
    {
        BattleGridManager grid = new();
        grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(2, 2));

        grid.RemoveSoldier(1);

        Assert.True(grid.IsSpaceAvailable(new Tuple<int, int>(2, 2)));
    }

    [Fact]
    public void GetNearestEnemy_FindsClosestOpposingSoldier()
    {
        BattleGridManager grid = new();
        grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(0, 0));
        grid.PlaceSoldier(CreateBattleSoldier(2), false, Cell(10, 0)); // far enemy
        grid.PlaceSoldier(CreateBattleSoldier(3), false, Cell(3, 0));  // near enemy

        float distance = grid.GetNearestEnemy(1, out int closest);

        Assert.Equal(3, closest);
        Assert.Equal(3f, distance, precision: 4);
    }

    [Fact]
    public void GetNearestEnemy_IgnoresSameSide()
    {
        BattleGridManager grid = new();
        grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(0, 0));
        grid.PlaceSoldier(CreateBattleSoldier(2), true, Cell(1, 0)); // ally, closer
        grid.PlaceSoldier(CreateBattleSoldier(3), false, Cell(5, 0)); // only enemy

        grid.GetNearestEnemy(1, out int closest);

        Assert.Equal(3, closest);
    }

    [Fact]
    public void GetDistanceBetweenSoldiers_ReturnsEuclideanDistance()
    {
        BattleGridManager grid = new();
        grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(0, 0));
        grid.PlaceSoldier(CreateBattleSoldier(2), false, Cell(3, 4));

        Assert.Equal(5f, grid.GetDistanceBetweenSoldiers(1, 2), precision: 4);
    }

    [Fact]
    public void GetClosestOpenAdjacency_PicksUnoccupiedNeighborNearestStart()
    {
        BattleGridManager grid = new();
        Tuple<int, int> target = new(5, 5);
        Tuple<int, int> start = new(5, 0); // below the target

        Tuple<int, int> adjacency = grid.GetClosestOpenAdjacency(start, target);

        // the neighbor at (5,4) is closest to a start below the target
        Assert.Equal(new Tuple<int, int>(5, 4), adjacency);
    }

    [Fact]
    public void GetClosestOpenAdjacency_SkipsReservedNeighbors()
    {
        BattleGridManager grid = new();
        Tuple<int, int> target = new(5, 5);
        grid.ReserveSpace(new Tuple<int, int>(5, 4)); // would-be closest

        Tuple<int, int> adjacency = grid.GetClosestOpenAdjacency(new Tuple<int, int>(5, 0), target);

        Assert.NotEqual(new Tuple<int, int>(5, 4), adjacency);
        Assert.NotNull(adjacency);
    }

    [Fact]
    public void Clone_ReproducesPlacementsAndReservations()
    {
        BattleGridManager grid = new();
        grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(1, 1));
        grid.ReserveSpace(new Tuple<int, int>(9, 9));

        BattleGridManager clone = (BattleGridManager)grid.Clone();

        Assert.Equal(new Tuple<int, int>(1, 1), clone.GetSoldierPosition(1)[0]);
        Assert.False(clone.IsSpaceAvailable(new Tuple<int, int>(9, 9)));
    }
}
