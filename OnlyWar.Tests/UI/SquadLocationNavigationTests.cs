using System.Collections.Generic;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class SquadLocationNavigationTests
{
    [Fact]
    public void Resolve_ReturnsRegionTargetForLandedSquad()
    {
        Region region = CreateRegion();
        Squad squad = TestModelFactory.CreateSquad("Landed Squad");
        squad.CurrentRegion = region;

        SquadLocationNavigationTarget target = SquadLocationNavigation.Resolve(squad);

        Assert.Equal(SquadLocationNavigationKind.Region, target.Kind);
        Assert.Same(squad, target.Squad);
        Assert.Same(region, target.Region);
        Assert.Null(target.Ship);
    }

    [Theory]
    [InlineData(FleetTravelPhase.InOrbit)]
    [InlineData(FleetTravelPhase.OutboundSystemTransit)]
    [InlineData(FleetTravelPhase.InboundSystemTransit)]
    public void Resolve_ReturnsShipTargetForAnyRealspaceFleetPhase(FleetTravelPhase travelPhase)
    {
        Planet planet = CreatePlanet();
        Squad squad = TestModelFactory.CreateSquad("Embarked Squad");
        Ship ship = CreateShip();
        ship.LoadSquad(squad);
        squad.BoardedLocation = ship;
        TaskForce fleet = new(1, null, null, planet, null, new List<Ship> { ship });
        fleet.TravelPhase = travelPhase;

        SquadLocationNavigationTarget target = SquadLocationNavigation.Resolve(squad);

        Assert.Equal(SquadLocationNavigationKind.Ship, target.Kind);
        Assert.Same(ship, target.Ship);
        Assert.Same(squad, target.Squad);
    }

    [Fact]
    public void Resolve_ReturnsUnavailableForWarpAndLostSquads()
    {
        Planet planet = CreatePlanet();
        Squad warpSquad = TestModelFactory.CreateSquad("Warp Squad");
        Ship warpShip = CreateShip();
        warpShip.LoadSquad(warpSquad);
        warpSquad.BoardedLocation = warpShip;
        TaskForce warpFleet = new(2, null, null, planet, null, new List<Ship> { warpShip });
        warpFleet.TravelPhase = FleetTravelPhase.InWarp;

        Squad lostSquad = TestModelFactory.CreateSquad("Lost Squad");

        Assert.Null(SquadLocationNavigation.Resolve(warpSquad));
        Assert.Null(SquadLocationNavigation.Resolve(lostSquad));
    }

    private static Planet CreatePlanet() =>
        new(1, "Test Planet", new Coordinate(1, 1), 1, null, 1, 0);

    private static Region CreateRegion() =>
        new(5, CreatePlanet(), 1, "Test Region", new RegionCoordinate(0, 0), 0);

    private static Ship CreateShip() =>
        new(1, "Bellum", new ShipTemplate(1, "Strike Cruiser", 100, 0, 0));
}
