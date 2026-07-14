using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class ConeTemplateTests
{
    [Fact]
    public void GetVictimIds_IncludesNozzleAndFarEdgeButExcludesBeyondRange()
    {
        BattleGridManager grid = CreateAimedGrid();
        Place(grid, 3, false, (1, 0));
        Place(grid, 4, false, (10, 3));
        Place(grid, 5, false, (11, 0));

        IReadOnlyList<int> victims = ConeTemplate.GetVictimIds(grid, 1, 2, 10, 3);

        Assert.Contains(3, victims);
        Assert.Contains(4, victims);
        Assert.DoesNotContain(5, victims);
    }

    [Fact]
    public void GetVictimIds_HalfWidthGrowsLinearlyAlongCone()
    {
        BattleGridManager grid = CreateAimedGrid();
        Place(grid, 3, false, (1, 1));
        Place(grid, 4, false, (8, 2));

        IReadOnlyList<int> victims = ConeTemplate.GetVictimIds(grid, 1, 2, 10, 3);

        Assert.DoesNotContain(3, victims);
        Assert.Contains(4, victims);
    }

    [Fact]
    public void GetVictimIds_CatchesSoldierWhenAnyFootprintCellIsInside()
    {
        BattleGridManager grid = CreateAimedGrid();
        Place(grid, 3, false, (4, 2), (5, 1));
        Place(grid, 4, false, (5, 2), (6, 3));

        IReadOnlyList<int> victims = ConeTemplate.GetVictimIds(grid, 1, 2, 10, 3);

        Assert.Contains(3, victims);
        Assert.DoesNotContain(4, victims);
    }

    [Fact]
    public void GetVictimIds_IncludesBothSidesExcludesShooterAndSortsById()
    {
        BattleGridManager grid = new();
        Place(grid, 10, true, (0, 0));
        Place(grid, 20, false, (5, 0));
        Place(grid, 7, false, (4, 0));
        Place(grid, 3, true, (3, 0));

        IReadOnlyList<int> victims = ConeTemplate.GetVictimIds(grid, 10, 20, 10, 3);

        Assert.Equal(new[] { 3, 7, 20 }, victims);
        Assert.DoesNotContain(10, victims);
    }

    [Fact]
    public void GetVictimIds_UsesAxialAndLateralDistancesOnDiagonalAimLine()
    {
        BattleGridManager grid = new();
        Place(grid, 1, true, (0, 0));
        Place(grid, 2, false, (5, 5));
        Place(grid, 3, false, (6, 6));
        Place(grid, 4, false, (4, 6));
        Place(grid, 5, false, (3, 7));
        Place(grid, 6, false, (8, 8));

        IReadOnlyList<int> victims = ConeTemplate.GetVictimIds(grid, 1, 2, 10, 3);

        Assert.Contains(3, victims);
        Assert.Contains(4, victims);
        Assert.DoesNotContain(5, victims);
        Assert.DoesNotContain(6, victims);
    }

    private static BattleGridManager CreateAimedGrid()
    {
        BattleGridManager grid = new();
        Place(grid, 1, true, (0, 0));
        Place(grid, 2, false, (5, 0));
        return grid;
    }

    private static void Place(
        BattleGridManager grid,
        int id,
        bool side,
        params (int X, int Y)[] cells)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: $"Soldier {id}");
        soldier.Id = id;
        BattleSoldier battleSoldier = new(soldier, null);
        List<Tuple<int, int>> footprint = [];
        foreach ((int x, int y) in cells)
        {
            footprint.Add(new Tuple<int, int>(x, y));
        }

        grid.PlaceSoldier(battleSoldier, side, footprint);
    }
}
