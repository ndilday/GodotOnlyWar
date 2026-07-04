using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class FleetScreenControllerTests
{
    [Fact]
    public void CreateFleetNode_GroupsLoadedSquadsByOwningUnit()
    {
        Unit secondCompany = CreateUnit(2, "Second Company");
        Unit thirdCompany = CreateUnit(3, "Third Company");
        Ship ship = CreateShip();

        ship.LoadSquad(CreateSquad(11, "Boreas Squad", secondCompany));
        ship.LoadSquad(CreateSquad(21, "Aquila Squad", thirdCompany));
        ship.LoadSquad(CreateSquad(12, "Ardent Squad", secondCompany));

        TaskForce taskForce = new(1, CreateFaction(), null, null, null, [ship]);

        TreeNode fleetNode = FleetScreenController.CreateFleetNode(taskForce);
        TreeNode shipNode = fleetNode.Children.Single();

        Assert.Equal(["Second Company", "Third Company"], shipNode.Children.Select(node => node.Name));
        Assert.False(shipNode.Children[0].Selectable);
        Assert.Equal(["Ardent Squad", "Boreas Squad"], shipNode.Children[0].Children.Select(node => node.Name));
        Assert.Equal(["Aquila Squad"], shipNode.Children[1].Children.Select(node => node.Name));
    }

    [Fact]
    public void CreateFleetNode_InWarpHidesLoadedSquads()
    {
        Unit secondCompany = CreateUnit(2, "Second Company");
        Ship ship = CreateShip();
        ship.LoadSquad(CreateSquad(11, "Boreas Squad", secondCompany));
        TaskForce taskForce = new(1, CreateFaction(), null, null, null, [ship])
        {
            TravelPhase = FleetTravelPhase.InWarp
        };

        TreeNode fleetNode = FleetScreenController.CreateFleetNode(taskForce);
        TreeNode shipNode = fleetNode.Children.Single();

        Assert.Empty(shipNode.Children);
        Assert.False(fleetNode.Selectable);
        Assert.False(shipNode.Selectable);
    }

    private static Ship CreateShip()
    {
        ShipTemplate template = new(1, "Battle Barge", 100, 0, 0);
        return new Ship(1, "Bellum", template);
    }

    private static Unit CreateUnit(int id, string name)
    {
        UnitTemplate template = new(id, $"{name} Template", false, new List<SquadTemplate>(), []);
        return new Unit(id, name, template, []);
    }

    private static Squad CreateSquad(int id, string name, Unit unit)
    {
        Squad squad = new(id, name, unit, TestModelFactory.SquadTemplate);
        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} Marine"));
        unit.AddSquad(squad);
        return squad;
    }

    private static Faction CreateFaction()
    {
        return new Faction(
            1,
            "Test Chapter",
            Color.Blue,
            true,
            false,
            false,
            GrowthType.None,
            new Dictionary<int, OnlyWar.Models.Soldiers.Species>(),
            new Dictionary<int, OnlyWar.Models.Soldiers.SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
