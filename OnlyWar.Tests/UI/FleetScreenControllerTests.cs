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
        Assert.Equal(["Boreas Squad", "Ardent Squad"], shipNode.Children[0].Children.Select(node => node.Name));
        Assert.Equal(["Aquila Squad"], shipNode.Children[1].Children.Select(node => node.Name));
    }

    [Fact]
    public void CreateFleetNode_SortsShipsByTroopCapacityDescending()
    {
        Ship escort = CreateShip(1, "Escort", 20);
        Ship battleBarge = CreateShip(2, "Battle Barge", 100);
        Ship strikeCruiser = CreateShip(3, "Strike Cruiser", 50);
        TaskForce taskForce = new(1, CreateFaction(), null, null, null, [escort, battleBarge, strikeCruiser]);

        TreeNode fleetNode = FleetScreenController.CreateFleetNode(taskForce);

        Assert.Equal(
            ["Battle Barge (0/100)", "Strike Cruiser (0/50)", "Escort (0/20)"],
            fleetNode.Children.Select(node => node.Name));
    }

    [Fact]
    public void CreateLoadedUnitNodes_UsesOrderOfBattleAndSquadOrderBeforeNames()
    {
        Unit chapter = CreateUnit(1, "Chapter");
        Unit secondCompany = CreateUnit(2, "Second Company");
        Unit firstCompany = CreateUnit(3, "First Company");
        AddChildUnit(chapter, secondCompany);
        AddChildUnit(chapter, firstCompany);

        Squad betaSquad = CreateSquad(11, "Beta Squad", secondCompany);
        Squad alphaSquad = CreateSquad(12, "Alpha Squad", secondCompany);
        Squad firstCompanySquad = CreateSquad(21, "First Company Squad", firstCompany);
        Ship ship = CreateShip();

        ship.LoadSquad(firstCompanySquad);
        ship.LoadSquad(alphaSquad);
        ship.LoadSquad(betaSquad);

        IReadOnlyList<TreeNode> unitNodes = FleetScreenController.CreateLoadedUnitNodes(ship);

        Assert.Equal(["Second Company", "First Company"], unitNodes.Select(node => node.Name));
        Assert.Equal(["Beta Squad", "Alpha Squad"], unitNodes[0].Children.Select(node => node.Name));
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
        return CreateShip(1, "Bellum", 100);
    }

    private static Ship CreateShip(int id, string name, ushort capacity)
    {
        ShipTemplate template = new(id, name, capacity, 0, 0);
        return new Ship(id, name, template);
    }

    private static Unit CreateUnit(int id, string name)
    {
        UnitTemplate template = new(id, $"{name} Template", false, new List<SquadTemplate>(), []);
        return new Unit(id, name, template, []);
    }

    private static void AddChildUnit(Unit parent, Unit child)
    {
        parent.ChildUnits.Add(child);
        child.ParentUnit = parent;
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
