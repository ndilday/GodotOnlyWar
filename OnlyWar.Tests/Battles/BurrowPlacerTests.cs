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
/// squad placed at range should be pulled directly up against the nearest enemy, while
/// a non-burrowing squad is left where it was.
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
}
