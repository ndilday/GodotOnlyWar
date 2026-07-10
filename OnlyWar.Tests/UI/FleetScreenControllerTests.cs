using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
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

    [Fact]
    public void CanTransferSquadToShip_AllowsShipsInSameFleetWithCapacity()
    {
        Unit secondCompany = CreateUnit(2, "Second Company");
        Ship sourceShip = CreateShip(1, "Source", 20);
        Ship destinationShip = CreateShip(2, "Destination", 20);
        _ = new TaskForce(1, CreateFaction(), null, CreatePlanet(1), null, [sourceShip, destinationShip]);
        Squad squad = CreateSquad(11, "Boreas Squad", secondCompany, memberCount: 5);
        sourceShip.LoadSquad(squad);
        squad.BoardedLocation = sourceShip;

        bool canTransfer = FleetScreenController.CanTransferSquadToShip(squad, destinationShip);

        Assert.True(canTransfer);
    }

    [Fact]
    public void CanTransferSquadToShip_RejectsShipWithoutCapacity()
    {
        Unit secondCompany = CreateUnit(2, "Second Company");
        Ship sourceShip = CreateShip(1, "Source", 20);
        Ship destinationShip = CreateShip(2, "Destination", 6);
        _ = new TaskForce(1, CreateFaction(), null, CreatePlanet(1), null, [sourceShip, destinationShip]);
        Squad squad = CreateSquad(11, "Boreas Squad", secondCompany, memberCount: 5);
        Squad occupyingSquad = CreateSquad(12, "Ardent Squad", secondCompany, memberCount: 2);
        sourceShip.LoadSquad(squad);
        squad.BoardedLocation = sourceShip;
        destinationShip.LoadSquad(occupyingSquad);
        occupyingSquad.BoardedLocation = destinationShip;

        bool canTransfer = FleetScreenController.CanTransferSquadToShip(squad, destinationShip);

        Assert.False(canTransfer);
    }

    [Fact]
    public void CanTransferSquadToShip_RejectsShipAtDifferentLocation()
    {
        Unit secondCompany = CreateUnit(2, "Second Company");
        Ship sourceShip = CreateShip(1, "Source", 20);
        Ship destinationShip = CreateShip(2, "Destination", 20);
        Faction faction = CreateFaction();
        _ = new TaskForce(1, faction, null, CreatePlanet(1), null, [sourceShip]);
        _ = new TaskForce(2, faction, null, CreatePlanet(2), null, [destinationShip]);
        Squad squad = CreateSquad(11, "Boreas Squad", secondCompany, memberCount: 5);
        sourceShip.LoadSquad(squad);
        squad.BoardedLocation = sourceShip;

        bool canTransfer = FleetScreenController.CanTransferSquadToShip(squad, destinationShip);

        Assert.False(canTransfer);
    }

    [Fact]
    public void TransferSquadToShip_MovesSquadAndRefreshesCapacity()
    {
        Unit secondCompany = CreateUnit(2, "Second Company");
        Ship sourceShip = CreateShip(1, "Source", 20);
        Ship destinationShip = CreateShip(2, "Destination", 20);
        _ = new TaskForce(1, CreateFaction(), null, CreatePlanet(1), null, [sourceShip, destinationShip]);
        Squad squad = CreateSquad(11, "Boreas Squad", secondCompany, memberCount: 5);
        sourceShip.LoadSquad(squad);
        squad.BoardedLocation = sourceShip;

        FleetScreenController.TransferSquadToShip(squad, destinationShip);

        Assert.DoesNotContain(squad, sourceShip.LoadedSquads);
        Assert.Contains(squad, destinationShip.LoadedSquads);
        Assert.Equal(destinationShip, squad.BoardedLocation);
        Assert.Equal(0, sourceShip.LoadedSoldierCount);
        Assert.Equal(5, destinationShip.LoadedSoldierCount);
    }

    [Fact]
    public void LoadedSoldierCount_TracksMembersAddedAfterSquadEmbarks()
    {
        Unit secondCompany = CreateUnit(2, "Second Company");
        Ship ship = CreateShip(1, "Source", 20);
        Squad squad = new(11, "Boreas Squad", secondCompany, TestModelFactory.SquadTemplate);

        ship.LoadSquad(squad);
        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: "Boreas Marine"));

        Assert.Equal(1, ship.LoadedSoldierCount);
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

    private static Squad CreateSquad(int id, string name, Unit unit, int memberCount = 1)
    {
        Squad squad = new(id, name, unit, TestModelFactory.SquadTemplate);
        for (int i = 0; i < memberCount; i++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"{name} Marine {i + 1}"));
        }
        unit.AddSquad(squad);
        return squad;
    }

    private static Planet CreatePlanet(int id)
    {
        return new Planet(id, $"Planet {id}", new Coordinate((ushort)id, (ushort)id), 1, null, 1, 0);
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
