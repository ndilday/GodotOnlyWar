using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models;
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

    private static BattleSquad CreateBattleSquad(string name, int id, bool isPlayerSquad = false)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(name: name);
        soldier.Id = id;
        return new BattleSquad(isPlayerSquad, TestModelFactory.CreateSquad(name, soldier));
    }

    private static BattleSquad CreateBattleSquadWithSoldiers(string name, int firstId, int count, bool isPlayerSquad)
    {
        Soldier[] soldiers = Enumerable.Range(0, count)
            .Select(index =>
            {
                Soldier soldier = TestModelFactory.CreateSoldier(name: $"{name} {index + 1}");
                soldier.Id = firstId + index;
                return soldier;
            })
            .ToArray();
        return new BattleSquad(isPlayerSquad, TestModelFactory.CreateSquad(name, soldiers));
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
    public void TryMoveSoldier_ReturnsFalseWhenTargetOccupiedByAnother()
    {
        BattleGridManager grid = new();
        BattleSoldier mover = CreateBattleSoldier(1);
        grid.PlaceSoldier(mover, true, Cell(0, 0));
        grid.PlaceSoldier(CreateBattleSoldier(2), false, Cell(1, 0));

        bool moved = grid.TryMoveSoldier(mover, new Tuple<int, int>(1, 0), 0);

        Assert.False(moved);
        Assert.Equal(new Tuple<int, int>(0, 0), grid.GetSoldierPosition(1)[0]);
    }

    [Fact]
    public void PlaceBattleSquad_VerticalPlacementUsesRotatedFootprintForNonSquareSoldiers()
    {
        BattleGridManager grid = new();
        SoldierTemplate template = CreateNonSquareTemplate();
        Soldier first = TestModelFactory.CreateSoldier(template: template, name: "Ravener One");
        first.Id = 1;
        Soldier second = TestModelFactory.CreateSoldier(template: template, name: "Ravener Two");
        second.Id = 2;
        BattleSquad squad = new(false, TestModelFactory.CreateSquad("Raveners", first, second));

        BattleSquadPlacer.PlaceBattleSquad(
            grid,
            squad,
            new Tuple<int, int>(0, 0),
            longHorizontal: false,
            tacticalSide: false,
            formationSide: false);

        Assert.Equal(new[] { new Tuple<int, int>(1, 0), new Tuple<int, int>(2, 0) },
            grid.GetSoldierPosition(1));
        Assert.Equal(new[] { new Tuple<int, int>(1, 1), new Tuple<int, int>(2, 1) },
            grid.GetSoldierPosition(2));
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
    public void IsAdjacentToEnemy_UsesTacticalSide()
    {
        BattleGridManager grid = new();
        grid.PlaceSoldier(CreateBattleSoldier(1), true, Cell(0, 0));
        grid.PlaceSoldier(CreateBattleSoldier(2), true, Cell(1, 0));
        grid.PlaceSoldier(CreateBattleSoldier(3), false, Cell(0, 1));

        Assert.True(grid.IsAdjacentToEnemy(1));
        Assert.False(grid.IsAdjacentToEnemy(2));
    }

    [Fact]
    public void AnnihilationPlacer_PlacesNpcForcesAsOpposingTacticalSides()
    {
        BattleGridManager grid = new();
        BattleSquad attackers = CreateBattleSquad("NPC Attackers", 10, isPlayerSquad: false);
        BattleSquad defenders = CreateBattleSquad("NPC Defenders", 11, isPlayerSquad: false);
        AnnihilationPlacer placer = new(grid, range: 10);

        placer.PlaceSquads([attackers], [defenders]);
        grid.GetNearestEnemy(attackers.Soldiers[0].Soldier.Id, out int closestEnemyId);

        Assert.Equal(defenders.Soldiers[0].Soldier.Id, closestEnemyId);
    }

    [Fact]
    public void AnnihilationPlacer_CentersForcesAcrossTheEngagementGap()
    {
        BattleGridManager grid = new();
        BattleSquad bottom = CreateBattleSquadWithSoldiers("Bottom", 100, 9, true);
        BattleSquad top = CreateBattleSquadWithSoldiers("Top", 200, 10, false);
        AnnihilationPlacer placer = new(grid, range: 12);

        placer.PlaceSquads([bottom], [top]);

        double bottomCenterX = bottom.Soldiers.Average(soldier => grid.GetSoldierPosition(soldier.Soldier.Id)[0].Item1);
        double topCenterX = top.Soldiers.Average(soldier => grid.GetSoldierPosition(soldier.Soldier.Id)[0].Item1);
        int bottomFrontY = bottom.Soldiers.Max(soldier => grid.GetSoldierPosition(soldier.Soldier.Id)[0].Item2);
        int topFrontY = top.Soldiers.Min(soldier => grid.GetSoldierPosition(soldier.Soldier.Id)[0].Item2);

        Assert.InRange(System.Math.Abs(bottomCenterX - topCenterX), 0, 0.6);
        Assert.True(topFrontY > bottomFrontY);
    }

    [Fact]
    public void PlaceBattleSquad_PutsIncompleteRankBehindFullFrontRank()
    {
        BattleGridManager grid = new();
        BattleSquad squad = CreateBattleSquadWithSoldiers("Nine", 300, 9, true);

        BattleSquadPlacer.PlaceBattleSquad(grid, squad, new Tuple<int, int>(0, 0),
            longHorizontal: true, tacticalSide: true, formationSide: false);

        int frontY = squad.Soldiers.Max(soldier => grid.GetSoldierPosition(soldier.Soldier.Id)[0].Item2);
        int soldiersInFrontRank = squad.Soldiers.Count(soldier =>
            grid.GetSoldierPosition(soldier.Soldier.Id)[0].Item2 == frontY);

        Assert.Equal(5, soldiersInFrontRank);
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

    private static SoldierTemplate CreateNonSquareTemplate()
    {
        static NormalizedValueTemplate Value(float value) => new()
        {
            BaseValue = value,
            StandardDeviation = 0
        };

        Species species = new(
            99,
            "Test Non-Square",
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(10),
            Value(0),
            Value(10),
            Value(6),
            Value(1),
            width: 1,
            depth: 2,
            rangedEvasion: 0f,
            meleeEvasion: 0f,
            abilities: SpeciesAbilities.None,
            bodyTemplate: HumanBodyTemplate.Instance);

        return new SoldierTemplate(
            99,
            species,
            "Test Non-Square",
            1,
            1,
            false,
            0,
            Array.Empty<Tuple<BaseSkill, float>>());
    }
}
