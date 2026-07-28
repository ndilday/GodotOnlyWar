using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.UI;

public class ChapterControllerTests
{
    [Fact]
    public void OrderFilteredSoldiers_SortsByRankSubrankThenSurname()
    {
        SoldierTemplate seniorRole = new(
            1001,
            TestModelFactory.HumanSpecies,
            "Senior Role",
            6,
            1,
            false,
            0,
            []);
        SoldierTemplate juniorRole = new(
            1002,
            TestModelFactory.HumanSpecies,
            "Junior Role",
            5,
            1,
            false,
            0,
            []);
        List<ISoldier> soldiers =
        [
            TestModelFactory.CreateSoldier(juniorRole, "Lucius Cassian"),
            TestModelFactory.CreateSoldier(seniorRole, "Marcus Zeth"),
            TestModelFactory.CreateSoldier(juniorRole, "Titus Aquila"),
            TestModelFactory.CreateSoldier(juniorRole, "Gaius Boreas")
        ];

        List<string> orderedNames = ChapterController.OrderFilteredSoldiers(soldiers)
            .Select(soldier => soldier.Name)
            .ToList();

        Assert.Equal(
            ["Marcus Zeth", "Titus Aquila", "Gaius Boreas", "Lucius Cassian"],
            orderedNames);
    }
}
