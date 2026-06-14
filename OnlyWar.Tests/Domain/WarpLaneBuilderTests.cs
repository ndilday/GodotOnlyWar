using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OnlyWar.Builders;
using OnlyWar.Models.Planets;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class WarpLaneBuilderTests
{
    [Fact]
    public void BuildWarpLanes_LinksEveryNonCapitalPlanetToItsSubsectorCapital()
    {
        Planet capital = CreatePlanet(1, 0, 0, importance: 10);
        Planet outpostA = CreatePlanet(2, 2, 0, importance: 1);
        Planet outpostB = CreatePlanet(3, 0, 3, importance: 1);
        Subsector subsector = CreateSubsector(1, capital, outpostA, outpostB);

        List<WarpLane> lanes = WarpLaneBuilder.BuildWarpLanes([subsector], adjacencyThreshold: 50);

        Assert.Equal(2, lanes.Count);
        Assert.Contains(lanes, lane => Connects(lane, capital, outpostA));
        Assert.Contains(lanes, lane => Connects(lane, capital, outpostB));
        // No second subsector means no inter-capital lanes.
        Assert.All(lanes, lane => Assert.True(lane.Path.Item1 == capital || lane.Path.Item2 == capital));
    }

    [Fact]
    public void BuildWarpLanes_ConnectsCapitalsOfAdjoiningSubsectors()
    {
        Planet capitalOne = CreatePlanet(1, 0, 0, importance: 10);
        Planet memberOne = CreatePlanet(2, 1, 0, importance: 1);
        Planet capitalTwo = CreatePlanet(3, 5, 0, importance: 10);
        Planet memberTwo = CreatePlanet(4, 6, 0, importance: 1);
        Subsector subsectorOne = CreateSubsector(1, capitalOne, memberOne);
        Subsector subsectorTwo = CreateSubsector(2, capitalTwo, memberTwo);

        List<WarpLane> lanes = WarpLaneBuilder.BuildWarpLanes([subsectorOne, subsectorTwo], adjacencyThreshold: 50);

        Assert.Contains(lanes, lane => Connects(lane, capitalOne, memberOne));
        Assert.Contains(lanes, lane => Connects(lane, capitalTwo, memberTwo));
        Assert.Contains(lanes, lane => Connects(lane, capitalOne, capitalTwo));
        Assert.Equal(3, lanes.Count);
    }

    [Fact]
    public void BuildWarpLanes_SpanningTreeStillConnectsCapitalsBeyondAdjacencyThreshold()
    {
        Planet capitalOne = CreatePlanet(1, 0, 0, importance: 10);
        Planet capitalTwo = CreatePlanet(2, 100, 0, importance: 10);
        Subsector subsectorOne = CreateSubsector(1, capitalOne);
        Subsector subsectorTwo = CreateSubsector(2, capitalTwo);

        // The capitals are far outside the adjacency threshold, but the spanning tree
        // must still link them so the whole sector remains reachable.
        List<WarpLane> lanes = WarpLaneBuilder.BuildWarpLanes([subsectorOne, subsectorTwo], adjacencyThreshold: 5);

        Assert.Single(lanes);
        Assert.Contains(lanes, lane => Connects(lane, capitalOne, capitalTwo));
    }

    [Fact]
    public void BuildWarpLanes_AssignsUniqueLaneIds()
    {
        Planet capital = CreatePlanet(1, 0, 0, importance: 10);
        Planet outpostA = CreatePlanet(2, 2, 0, importance: 1);
        Planet outpostB = CreatePlanet(3, 0, 3, importance: 1);
        Subsector subsector = CreateSubsector(1, capital, outpostA, outpostB);

        List<WarpLane> lanes = WarpLaneBuilder.BuildWarpLanes([subsector], adjacencyThreshold: 50);

        Assert.Equal(lanes.Count, lanes.Select(lane => lane.Id).Distinct().Count());
    }

    private static bool Connects(WarpLane lane, Planet a, Planet b)
    {
        return (lane.Path.Item1 == a && lane.Path.Item2 == b)
            || (lane.Path.Item1 == b && lane.Path.Item2 == a);
    }

    private static Subsector CreateSubsector(ushort id, params Planet[] planets)
    {
        return new Subsector(id.ToString(), id, planets.ToList(), new List<Vector2I>());
    }

    private static Planet CreatePlanet(int id, ushort x, ushort y, int importance)
    {
        // Population is 0 for every planet here (regions carry no factions), so capital
        // selection falls through to the Importance tie-break that these tests drive.
        Planet planet = new(id, $"Planet {id}", new Tuple<ushort, ushort>(x, y), 1, null, importance, 0);
        for (int i = 0; i < planet.Regions.Length; i++)
        {
            planet.Regions[i] = new Region(i + (id * 100), planet, 0, $"Region {i}", new Tuple<int, int>(0, 0), 0);
        }
        return planet;
    }
}
