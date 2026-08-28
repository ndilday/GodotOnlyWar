using OnlyWar.Helpers;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Domain;

public sealed class FleetCapacityPlanServiceTests
{
    private readonly FleetCapacityPlanService _service = new();

    [Fact]
    public void DirectPlacement_IsDistinctFromRebalance()
    {
        Squad target = SquadWithMembers("Target", 5);
        Ship source = Ship(1, 10);
        source.LoadSquad(target);
        target.BoardedLocation = source;
        Ship destination = Ship(2, 8);

        FleetCapacitySolution result = _service.PlanPlacement(target, [destination]);

        Assert.Equal(FleetCapacityPlanKind.Direct, result.Kind);
        FleetSquadMove move = Assert.Single(result.Moves);
        Assert.Equal(target.Id, move.SquadId);
        Assert.Equal(destination.Id, move.TargetShipId);
    }

    [Fact]
    public void RebalanceMovesWholeUnrelatedSquadAndTarget()
    {
        Squad target = SquadWithMembers("Target", 5);
        Ship source = Ship(1, 10);
        source.LoadSquad(target);
        target.BoardedLocation = source;
        Ship a = Ship(2, 10);
        Ship b = Ship(3, 10);
        Load(a, SquadWithMembers("A5", 5), SquadWithMembers("A3", 3));
        Load(b, SquadWithMembers("B5", 5), SquadWithMembers("B1", 1));

        FleetCapacitySolution result = _service.PlanPlacement(target, [a, b]);

        Assert.Equal(FleetCapacityPlanKind.Rebalance, result.Kind);
        Assert.Equal(2, result.Moves.Count);
        Assert.Contains(result.Moves, move => move.SquadId == target.Id);
        Assert.All(result.Moves, move => Assert.True(move.Headcount > 0));
    }

    [Fact]
    public void ImpossibleReportsConcreteAggregateCapacityBlocker()
    {
        Squad target = SquadWithMembers("Target", 7);
        Ship source = Ship(1, 10);
        source.LoadSquad(target);
        target.BoardedLocation = source;
        Ship destination = Ship(2, 10);
        Load(destination, SquadWithMembers("Occupants", 8));

        FleetCapacitySolution result = _service.PlanPlacement(target, [destination]);

        Assert.Equal(FleetCapacityPlanKind.Impossible, result.Kind);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("aggregate"));
    }

    [Fact]
    public void StrengthFormattingOrdersOutgoingBeforeIncomingAndOmitsZeroes()
    {
        Assert.Equal("10 -2 +1 / 10", ChapterMusterViewModelBuilder.FormatStrength(10, 2, 1, 10));
        Assert.Equal("0 / 10", ChapterMusterViewModelBuilder.FormatStrength(0, 0, 0, 10));
    }

    private static Ship Ship(int id, ushort capacity) =>
        new(id, $"Ship {id}", new ShipTemplate(id, "Test", capacity, 0, 0));

    private static Squad SquadWithMembers(string name, int count)
    {
        Squad squad = new(name, null, TestModelFactory.SquadTemplate);
        for (int i = 0; i < count; i++)
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} {i}"));
        return squad;
    }

    private static void Load(Ship ship, params Squad[] squads)
    {
        foreach (Squad squad in squads)
        {
            ship.LoadSquad(squad);
            squad.BoardedLocation = ship;
        }
    }
}
