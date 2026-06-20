using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class FactionStrategyControllerTests
{
    [Fact]
    public void GenerateFactionOrders_ReturnsEmptyWhenFactionAbsentFromPlanet()
    {
        Faction enemy = CreateNonPlayerFaction();
        Sector sector = BuildSectorWithSingleRegionFaction(
            CreateNonPlayerFaction(id: 99, name: "Other"), population: 1000, organization: 100, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_ReturnsEmptyWhenRegionFactionIsHidden()
    {
        Faction enemy = CreateNonPlayerFaction();
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 100, isPublic: false);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_ReturnsEmptyWhenNoSpareTroops()
    {
        Faction enemy = CreateNonPlayerFaction();
        // Organization 0 => no organized troops => no spare troops => nothing to do
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 0, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.Empty(orders);
    }

    [Fact]
    public void GenerateFactionOrders_SpendsSpareTroopsOnDefensiveConstruction()
    {
        Faction enemy = CreateNonPlayerFaction();
        // No adjacent enemy => zero required garrison => the full organized force is spare.
        // The faction has no squad templates, so the only achievable orders are construction.
        Sector sector = BuildSectorWithSingleRegionFaction(enemy, population: 1000, organization: 100, isPublic: true);

        List<Order> orders = new FactionStrategyController().GenerateFactionOrders(enemy, sector);

        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.IsType<ConstructionMission>(o.Mission));
        Assert.All(orders, o => Assert.Empty(o.AssignedSquads));
    }

    private static Sector BuildSectorWithSingleRegionFaction(
        Faction faction, long population, int organization, bool isPublic)
    {
        Planet planet = CreatePlanet();
        PlanetFaction planetFaction = new(faction) { IsPublic = isPublic };
        planet.PlanetFactionMap[faction.Id] = planetFaction;

        RegionFaction regionFaction = new(planetFaction, planet.Regions[0])
        {
            Population = population,
            Organization = organization,
            IsPublic = isPublic
        };
        planet.Regions[0].RegionFactionMap[faction.Id] = regionFaction;

        return new Sector(CreatePlayerForce(), [], [planet], []);
    }

    private static PlayerForce CreatePlayerForce()
    {
        Faction playerFaction = CreatePlayerFaction();
        Fleet fleet = new("Test Fleet", null, null);
        Army army = new("Test Army", null, null, null, []);
        return new PlayerForce(playerFaction, army, fleet);
    }

    private static Planet CreatePlanet()
    {
        PlanetTemplate template = new(
            1,
            "Strategy Test World",
            1,
            new NormalizedValueTemplate { BaseValue = 1000, StandardDeviation = 0 },
            new NormalizedValueTemplate { BaseValue = 1, StandardDeviation = 0 },
            new LinearValueTemplate { MinValue = 0, MaxValue = 0 });
        Planet planet = new(1, "Strategy Test World", new Coordinate(1, 1), 1, template, 1, 0);

        for (int i = 0; i < planet.Regions.Length; i++)
        {
            planet.Regions[i] = new Region(
                i,
                planet,
                0,
                $"Region {i}",
                RegionExtensions.GetCoordinatesFromRegionNumber(i),
                0);
        }

        return planet;
    }

    private static Faction CreateNonPlayerFaction(int id = 2, string name = "Test Cult")
    {
        return BuildFaction(id, name, isPlayer: false, isDefault: false);
    }

    private static Faction CreatePlayerFaction()
    {
        return BuildFaction(1, "Test Chapter", isPlayer: true, isDefault: false);
    }

    private static Faction BuildFaction(int id, string name, bool isPlayer, bool isDefault)
    {
        return new Faction(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefault,
            false,
            GrowthType.Conversion,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }
}
