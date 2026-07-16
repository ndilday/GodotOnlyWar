using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BlastTemplateTests
{
    [Fact]
    public void GetVictims_CatchesCenterAndRimButNotJustOutside()
    {
        BattleGridManager grid = new();
        Place(grid, 1, false, (0, 0));
        Place(grid, 2, false, (6, 0));
        Place(grid, 3, false, (7, 0));

        IReadOnlyList<BlastTemplate.BlastVictim> victims =
            BlastTemplate.GetVictims(grid, new Tuple<int, int>(0, 0), 6);

        Assert.Equal(new[] { 1, 2 }, victims.Select(victim => victim.SoldierId));
    }

    [Fact]
    public void GetVictims_CatchesSoldierWhenAnyFootprintCellIsInside()
    {
        BattleGridManager grid = new();
        Place(grid, 1, false, (6, 0), (8, 0));
        Place(grid, 2, false, (7, 1), (8, 1));

        IReadOnlyList<BlastTemplate.BlastVictim> victims =
            BlastTemplate.GetVictims(grid, new Tuple<int, int>(0, 0), 6);

        Assert.Equal(new[] { 1 }, victims.Select(victim => victim.SoldierId));
    }

    [Fact]
    public void GetVictims_DoesNotExcludeTheThrower()
    {
        BattleGridManager grid = new();
        Place(grid, 1, true, (0, 0));
        Place(grid, 2, false, (4, 0));

        IReadOnlyList<BlastTemplate.BlastVictim> victims =
            BlastTemplate.GetVictims(grid, new Tuple<int, int>(4, 0), 6);

        Assert.Equal(new[] { 1, 2 }, victims.Select(victim => victim.SoldierId));
    }

    [Fact]
    public void GetVictims_OrdersVictimsById()
    {
        BattleGridManager grid = new();
        Place(grid, 20, false, (1, 0));
        Place(grid, 7, false, (2, 0));
        Place(grid, 13, true, (0, 1));

        IReadOnlyList<BlastTemplate.BlastVictim> victims =
            BlastTemplate.GetVictims(grid, new Tuple<int, int>(0, 0), 6);

        Assert.Equal(new[] { 7, 13, 20 }, victims.Select(victim => victim.SoldierId));
    }

    [Fact]
    public void GetVictims_ReturnsDistanceToNearestCaughtCell()
    {
        BattleGridManager grid = new();
        Place(grid, 1, false, (3, 0), (5, 0));
        Place(grid, 2, false, (0, 0));

        IReadOnlyList<BlastTemplate.BlastVictim> victims =
            BlastTemplate.GetVictims(grid, new Tuple<int, int>(0, 0), 6);

        Assert.Equal(0f, victims.Single(victim => victim.SoldierId == 2).DistanceFromImpact);
        Assert.Equal(3f, victims.Single(victim => victim.SoldierId == 1).DistanceFromImpact);
    }

    [Fact]
    public void ResolveImpactCell_LandsOnTargetsNearestCellWhenMarginIsNonNegative()
    {
        BattleGridManager grid = new();
        Place(grid, 1, true, (0, 0));
        Place(grid, 2, false, (10, 0), (11, 0));

        Tuple<int, int> impact = BlastTemplate.ResolveImpactCell(grid, 1, 2, 0f, 0.75);

        Assert.Equal(new Tuple<int, int>(10, 0), impact);
    }

    [Fact]
    public void ResolveImpactCell_ScattersOneCellPerPointOfFailureMargin()
    {
        BattleGridManager grid = new();
        Place(grid, 1, true, (0, 0));
        Place(grid, 2, false, (10, 0));

        Tuple<int, int> impact = BlastTemplate.ResolveImpactCell(grid, 1, 2, -4f, 0.0);

        Assert.Equal(new Tuple<int, int>(14, 0), impact);
    }

    [Theory]
    [InlineData(0.25, 10, 4)]
    [InlineData(0.5, 6, 0)]
    [InlineData(0.75, 10, -4)]
    public void ResolveImpactCell_TakesDirectionFromTheUniformRoll(
        double directionRoll,
        int expectedX,
        int expectedY)
    {
        BattleGridManager grid = new();
        Place(grid, 1, true, (0, 0));
        Place(grid, 2, false, (10, 0));

        Tuple<int, int> impact = BlastTemplate.ResolveImpactCell(grid, 1, 2, -4f, directionRoll);

        Assert.Equal(new Tuple<int, int>(expectedX, expectedY), impact);
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
