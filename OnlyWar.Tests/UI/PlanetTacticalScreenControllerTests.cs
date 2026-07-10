using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class PlanetTacticalScreenControllerTests
{
    [Fact]
    public void CreateLoadedUnitNodes_UsesFleetScreenCompanyAndSquadOrdering()
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

        IReadOnlyList<CommandTreeNode> unitNodes = PlanetTacticalScreenController.CreateLoadedUnitNodes(ship, "all");

        Assert.Equal(["Second Company | 2 aboard", "First Company | 1 aboard"], unitNodes.Select(node => node.Text));
        Assert.Equal(["Beta Squad | 1/1 | Unassigned", "Alpha Squad | 1/1 | Unassigned"], unitNodes[0].Children.Select(node => node.Text));
    }

    [Fact]
    public void CreateSurfaceUnitNodes_UsesFleetScreenCompanyAndSquadOrdering()
    {
        Unit chapter = CreateUnit(1, "Chapter");
        Unit secondCompany = CreateUnit(2, "Second Company");
        Unit firstCompany = CreateUnit(3, "First Company");
        AddChildUnit(chapter, secondCompany);
        AddChildUnit(chapter, firstCompany);

        RegionFaction regionFaction = new(new PlanetFaction(CreateFaction()), CreateRegion());
        regionFaction.LandedSquads.Add(CreateSquad(11, "Beta Squad", secondCompany));
        regionFaction.LandedSquads.Add(CreateSquad(12, "Alpha Squad", secondCompany));
        regionFaction.LandedSquads.Add(CreateSquad(21, "First Company Squad", firstCompany));

        IReadOnlyList<CommandTreeNode> unitNodes = PlanetTacticalScreenController.CreateSurfaceUnitNodes(5, regionFaction, "all");

        Assert.Equal(["Second Company | 2 on surface", "First Company | 1 on surface"], unitNodes.Select(node => node.Text));
        Assert.Equal(["Beta Squad | 1/1 | Unassigned", "Alpha Squad | 1/1 | Unassigned"], unitNodes[0].Children.Select(node => node.Text));
    }

    private static Ship CreateShip()
    {
        ShipTemplate template = new(1, "Strike Cruiser", 100, 0, 0);
        return new Ship(1, "Bellum", template);
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

    private static Region CreateRegion()
    {
        Planet planet = new(1, "Test Planet", new Coordinate(1, 1), 1, null, 1, 0);
        return new Region(5, planet, 1, "Test Region", new RegionCoordinate(0, 0), 0);
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
