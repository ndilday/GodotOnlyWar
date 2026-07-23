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
        Assert.Equal(["Alpha Squad | 1/1 | Unassigned", "Beta Squad | 1/1 | Unassigned"], unitNodes[0].Children.Select(node => node.Text));
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
        Assert.Equal(["Alpha Squad | 1/1 | Unassigned", "Beta Squad | 1/1 | Unassigned"], unitNodes[0].Children.Select(node => node.Text));
    }

    [Theory]
    [InlineData(true, 1, "Bastion", "Land Squad in Bastion")]
    [InlineData(false, 1, "Bastion", "Land 1 Squad in Bastion")]
    [InlineData(false, 3, "Bastion", "Land 3 Squads in Bastion")]
    public void BuildLandCommandText_DescribesSelectionAndDestination(
        bool singleSquadSelected,
        int squadCount,
        string regionName,
        string expected)
    {
        Assert.Equal(
            expected,
            PlanetTacticalScreenController.BuildLandCommandText(singleSquadSelected, squadCount, regionName));
    }

    [Theory]
    [InlineData(true, 1, "Bastion", "Embark Squad From Bastion")]
    [InlineData(false, 1, "Bastion", "Embark 1 Squad From Bastion")]
    [InlineData(false, 3, "Bastion", "Embark 3 Squads From Bastion")]
    public void BuildEmbarkCommandText_DescribesSelectionAndOrigin(
        bool singleSquadSelected,
        int squadCount,
        string regionName,
        string expected)
    {
        Assert.Equal(
            expected,
            PlanetTacticalScreenController.BuildEmbarkCommandText(singleSquadSelected, squadCount, regionName));
    }

    [Theory]
    [InlineData("region:5", true)]
    [InlineData("surface-unit:5:2", true)]
    [InlineData("surface-squad:5:11", true)]
    [InlineData("ship:7", false)]
    [InlineData("loaded-unit:7:2", false)]
    [InlineData("loaded-squad:7:11", false)]
    [InlineData("group:orbit", false)]
    public void IsSurfaceRosterSelectionKey_OnlyAcceptsLandedTreeSelections(string key, bool expected)
    {
        Assert.Equal(expected, PlanetTacticalScreenController.IsSurfaceRosterSelectionKey(key));
    }

    [Fact]
    public void FindSquadById_ResolvesFromDisplayedRosterWithoutGlobalSquadMap()
    {
        Unit unit = CreateUnit(1, "Company");
        Squad expected = CreateSquad(11, "Alpha Squad", unit);

        Squad result = PlanetTacticalScreenController.FindSquadById([expected], expected.Id);

        Assert.Same(expected, result);
    }

    [Fact]
    public void FindSquadById_ReturnsNullWhenRosterOrSquadIsUnavailable()
    {
        Assert.Null(PlanetTacticalScreenController.FindSquadById(null, 11));
        Assert.Null(PlanetTacticalScreenController.FindSquadById([], 11));
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
