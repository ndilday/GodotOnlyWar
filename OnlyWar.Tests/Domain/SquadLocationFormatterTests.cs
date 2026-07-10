using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class SquadLocationFormatterTests
{
    [Fact]
    public void Format_ReturnsRegionAndPlanetForLandedSquad()
    {
        Planet planet = CreatePlanet("Calth");
        Region region = new(10, planet, 0, "Arcology Primus", new RegionCoordinate(1, 1), 0);
        Squad squad = TestModelFactory.CreateSquad("Test Squad");
        squad.CurrentRegion = region;

        string location = SquadLocationFormatter.Format(squad);

        Assert.Equal("Arcology Primus, Calth", location);
    }

    [Fact]
    public void Format_ReturnsShipAndOrbitedPlanetForOrbitingSquad()
    {
        Planet planet = CreatePlanet("Macragge");
        Ship ship = CreateShip("Glory of Hera");
        _ = new TaskForce(1, null, planet.Position, planet, null, [ship]);
        Squad squad = TestModelFactory.CreateSquad("Test Squad");
        squad.BoardedLocation = ship;

        string location = SquadLocationFormatter.Format(squad);

        Assert.Equal("Glory of Hera, orbiting Macragge", location);
    }

    [Fact]
    public void Format_ReturnsShipAndTransitForMovingSquad()
    {
        Planet origin = CreatePlanet("Prandium");
        Planet destination = CreatePlanet("Espandor");
        Ship ship = CreateShip("Sword of Illyrium");
        _ = new TaskForce(2, null, origin.Position, null, destination, [ship],
            travelPhase: FleetTravelPhase.InWarp,
            travelWeeksRemaining: 3);
        Squad squad = TestModelFactory.CreateSquad("Test Squad");
        squad.BoardedLocation = ship;

        string location = SquadLocationFormatter.Format(squad);

        Assert.Equal("Sword of Illyrium, in transit", location);
    }

    private static Planet CreatePlanet(string name)
    {
        return new Planet(1, name, new Coordinate(1, 2), 1, null, 1, 0);
    }

    private static Ship CreateShip(string name)
    {
        return new Ship(1, name, new ShipTemplate(1, "Strike Cruiser", 20, 0, 0));
    }
}
