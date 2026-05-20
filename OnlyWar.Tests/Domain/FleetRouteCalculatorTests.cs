using OnlyWar.Helpers.Fleets;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using System;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class FleetRouteCalculatorTests
{
    [Theory]
    [InlineData(FleetRouteScope.SameSubsector, 1)]
    [InlineData(FleetRouteScope.AdjacentSubsector, 3)]
    [InlineData(FleetRouteScope.DistantSubsector, 7)]
    public void CalculateBaseWarpWeeks_UsesSubsectorTravelBands(FleetRouteScope scope, int expectedWeeks)
    {
        Assert.Equal(expectedWeeks, FleetRouteCalculator.CalculateBaseWarpWeeks(scope));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.5, 0.5)]
    [InlineData(-0.5, 2)]
    [InlineData(1, 1.0 / 3.0)]
    [InlineData(-1, 3)]
    public void CalculateSubjectiveWarpMultiplier_MapsGaussianToTableThreeStyleDurations(double zValue, double expected)
    {
        Assert.Equal(expected, FleetRouteCalculator.CalculateSubjectiveWarpMultiplier(zValue), precision: 6);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 0.1)]
    [InlineData(-5, 10)]
    public void CalculateObjectiveWarpMultiplier_ScalesSubjectiveWarpTime(double zValue, double expected)
    {
        Assert.Equal(expected, FleetRouteCalculator.CalculateObjectiveWarpMultiplier(zValue), precision: 6);
    }

    [Fact]
    public void CalculateBestRoute_UsesDirectRouteWhenNoLanePathExists()
    {
        Planet origin = CreatePlanet(1, 0, 0);
        Planet destination = CreatePlanet(2, 16, 0);
        FleetRouteCalculator calculator = new();

        FleetRoute route = calculator.CalculateBestRoute(
            origin,
            destination,
            [],
            FleetRouteScope.SameSubsector,
            subjectiveZ: 0,
            objectiveZ: 0);

        Assert.Equal(FleetRouteType.Direct, route.RouteType);
        Assert.Equal(1, route.BaseWarpWeeks);
        Assert.Equal(1, route.SubjectiveWarpWeeks);
        Assert.Equal(1, route.ObjectiveWarpWeeks);
        Assert.Equal(5, route.SubjectiveTotalWeeks);
        Assert.Equal(5, route.ObjectiveTotalWeeks);
        Assert.Equal(5, route.BaseTurns);
        Assert.Equal([origin, destination], route.Hops);
    }

    [Fact]
    public void CreateLaneRoute_UsesDijkstraPathWeightedByHopDistance()
    {
        Planet origin = CreatePlanet(1, 0, 0);
        Planet middle = CreatePlanet(2, 3, 4);
        Planet destination = CreatePlanet(3, 6, 8);
        Planet distant = CreatePlanet(4, 100, 100);
        FleetRouteCalculator calculator = new();
        WarpLane[] lanes =
        [
            new(1, 1, new Tuple<Planet, Planet>(origin, middle)),
            new(2, 1, new Tuple<Planet, Planet>(middle, destination)),
            new(3, 1, new Tuple<Planet, Planet>(origin, distant)),
            new(4, 1, new Tuple<Planet, Planet>(distant, destination))
        ];

        FleetRoute route = calculator.CreateLaneRoute(
            origin,
            destination,
            lanes,
            FleetRouteScope.AdjacentSubsector,
            subjectiveZ: 0,
            objectiveZ: 0);

        Assert.Equal(FleetRouteType.WarpLane, route.RouteType);
        Assert.Equal([origin, middle, destination], route.Hops);
        Assert.Equal(3, route.BaseWarpWeeks);
        Assert.Equal(7, route.BaseTurns);
    }

    [Fact]
    public void CalculateBestRoute_PrefersEstablishedLaneWhenPathExists()
    {
        Planet origin = CreatePlanet(1, 0, 0);
        Planet destination = CreatePlanet(2, 16, 0);
        FleetRouteCalculator calculator = new();
        WarpLane lane = new(1, 1, new Tuple<Planet, Planet>(origin, destination));

        FleetRoute route = calculator.CalculateBestRoute(
            origin,
            destination,
            [lane],
            FleetRouteScope.SameSubsector,
            subjectiveZ: 0,
            objectiveZ: 0);

        Assert.Equal(FleetRouteType.WarpLane, route.RouteType);
        Assert.Equal(5, route.BaseTurns);
    }

    [Fact]
    public void CalculateBestRoute_AppliesSubjectiveAndObjectiveGaussianMultipliersToWarpOnly()
    {
        Planet origin = CreatePlanet(1, 0, 0);
        Planet destination = CreatePlanet(2, 16, 0);
        FleetRouteCalculator calculator = new();

        FleetRoute route = calculator.CalculateBestRoute(
            origin,
            destination,
            [],
            FleetRouteScope.AdjacentSubsector,
            subjectiveZ: -0.5,
            objectiveZ: -5);

        Assert.Equal(3, route.BaseWarpWeeks);
        Assert.Equal(6, route.SubjectiveWarpWeeks);
        Assert.Equal(60, route.ObjectiveWarpWeeks);
        Assert.Equal(10, route.SubjectiveTotalWeeks);
        Assert.Equal(64, route.ObjectiveTotalWeeks);
        Assert.Equal(64, route.BaseTurns);
    }

    private static Planet CreatePlanet(int id, ushort x, ushort y)
    {
        return new Planet(id, $"Planet {id}", new Tuple<ushort, ushort>(x, y), 1, null, 1, 0);
    }
}
