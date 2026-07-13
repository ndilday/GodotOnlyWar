using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Burrow-arrival placement (see Design/EvasionBurrowAndAmbush.md): a burrow-capable
/// squad placed at range picks the nearest enemy squad and erupts around that squad's
/// footprint — adjacent cells first, spilling into outer rings when the perimeter
/// fills — while a non-burrowing squad is left where it was.
/// </summary>
public class BurrowPlacerTests
{
    private static BattleSoldier Place(BattleGridManager grid, BattleSquad squad, int index,
                                       bool side, int x, int y)
    {
        BattleSoldier soldier = squad.AbleSoldiers[index];
        Tuple<int, int> cell = new(x, y);
        grid.PlaceSoldier(soldier, side, new List<Tuple<int, int>> { cell });
        soldier.TopLeft = cell;
        return soldier;
    }

    private static BattleSquad CreateSquad(string name, SoldierTemplate template, params int[] ids)
    {
        Soldier[] soldiers = new Soldier[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            Soldier soldier = TestModelFactory.CreateSoldier(template: template, name: $"{name}-{ids[i]}");
            soldier.Id = ids[i];
            soldiers[i] = soldier;
        }
        Squad squad = TestModelFactory.CreateSquad(name, soldiers);
        return new BattleSquad(false, squad);
    }

    [Fact]
    public void PlaceBurrowers_MovesBurrowingSquadAdjacentToEnemy()
    {
        BattleGridManager grid = new();
        BattleSquad enemy = CreateSquad("Enemy", TestModelFactory.MarineTemplate, 1);
        BattleSquad burrowers = CreateSquad("Burrowers", TestModelFactory.BurrowerTemplate, 2, 3);

        BattleSoldier enemySoldier = Place(grid, enemy, 0, side: false, x: 0, y: 0);
        Place(grid, burrowers, 0, side: true, x: 10, y: 10);
        Place(grid, burrowers, 1, side: true, x: 11, y: 10);

        BurrowPlacer.PlaceBurrowers(grid, new[] { burrowers, enemy });

        // both burrowers should now be orthogonally adjacent to the lone enemy
        foreach (BattleSoldier burrower in burrowers.AbleSoldiers)
        {
            float distance = grid.GetDistanceBetweenSoldiers(burrower.Soldier.Id, enemySoldier.Soldier.Id);
            Assert.True(distance <= 1.001f, $"burrower ended up {distance} from the enemy");
        }
    }

    [Fact]
    public void PlaceBurrowers_LeavesNonBurrowingSquadInPlace()
    {
        BattleGridManager grid = new();
        BattleSquad enemy = CreateSquad("Enemy", TestModelFactory.MarineTemplate, 1);
        BattleSquad marines = CreateSquad("Marines", TestModelFactory.MarineTemplate, 2);

        Place(grid, enemy, 0, side: false, x: 0, y: 0);
        BattleSoldier marine = Place(grid, marines, 0, side: true, x: 10, y: 10);

        BurrowPlacer.PlaceBurrowers(grid, new[] { marines, enemy });

        Assert.Equal(new Tuple<int, int>(10, 10), grid.GetSoldierPosition(marine.Soldier.Id)[0]);
    }

    [Fact]
    public void PlaceBurrowers_SpillsIntoOuterRingsWhenPerimeterFills()
    {
        // a lone enemy has only 4 orthogonally adjacent cells; 6 burrowers must
        // overflow into the second ring instead of being left at their spawn
        BattleGridManager grid = new();
        BattleSquad enemy = CreateSquad("Enemy", TestModelFactory.MarineTemplate, 1);
        BattleSquad burrowers = CreateSquad("Burrowers", TestModelFactory.BurrowerTemplate, 2, 3, 4, 5, 6, 7);

        BattleSoldier enemySoldier = Place(grid, enemy, 0, side: false, x: 0, y: 0);
        for (int i = 0; i < 6; i++)
        {
            Place(grid, burrowers, i, side: true, x: 20 + i, y: 20);
        }

        BurrowPlacer.PlaceBurrowers(grid, new[] { burrowers, enemy });

        foreach (BattleSoldier burrower in burrowers.AbleSoldiers)
        {
            float distance = grid.GetDistanceBetweenSoldiers(burrower.Soldier.Id, enemySoldier.Soldier.Id);
            Assert.True(distance <= 2.001f, $"burrower ended up {distance} from the enemy");
        }
    }

    [Fact]
    public void PlaceBurrowers_WholeSquadEruptsAroundSameEnemySquad()
    {
        // the squad's nearest enemy squad (min pairwise distance) is B, so even the
        // burrower that is individually closer to A must erupt around B
        BattleGridManager grid = new();
        BattleSquad enemyA = CreateSquad("EnemyA", TestModelFactory.MarineTemplate, 1);
        BattleSquad enemyB = CreateSquad("EnemyB", TestModelFactory.MarineTemplate, 2);
        BattleSquad burrowers = CreateSquad("Burrowers", TestModelFactory.BurrowerTemplate, 3, 4);

        Place(grid, enemyA, 0, side: false, x: 0, y: 0);
        BattleSoldier enemyBSoldier = Place(grid, enemyB, 0, side: false, x: 20, y: 0);
        Place(grid, burrowers, 0, side: true, x: 5, y: 0);  // 5 from A, 15 from B
        Place(grid, burrowers, 1, side: true, x: 16, y: 0); // 16 from A, 4 from B

        BurrowPlacer.PlaceBurrowers(grid, new[] { burrowers, enemyA, enemyB });

        foreach (BattleSoldier burrower in burrowers.AbleSoldiers)
        {
            float distance = grid.GetDistanceBetweenSoldiers(burrower.Soldier.Id, enemyBSoldier.Soldier.Id);
            Assert.True(distance <= 1.001f, $"burrower ended up {distance} from enemy squad B");
        }
    }
}
