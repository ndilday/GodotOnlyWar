using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Generation;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class SubsectorBuilderTests
{
    private const ushort MaxDiameter = 20;

    public SubsectorBuilderTests()
    {
        Directory.SetCurrentDirectory(RulesDatabaseFixture.RepositoryRoot);
        GameDataSingleton.Instance.LoadGameDataFromBlob(new GameRulesData(), new Date(41, 1, 1), null);
    }

    [Fact]
    public void BuildSubsectors_MergesNearbyPlanets()
    {
        Planet first = CreatePlanet(1, 10, 10);
        Planet second = CreatePlanet(2, 15, 10);

        List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors([first, second], new Vector2I(50, 50), MaxDiameter);

        Subsector containingFirst = Assert.Single(subsectors, s => s.Planets.Contains(first));
        Assert.Contains(second, containingFirst.Planets);
    }

    [Fact]
    public void BuildSubsectors_KeepsDistantPlanetsSeparate()
    {
        Planet first = CreatePlanet(1, 10, 10);
        Planet second = CreatePlanet(2, 40, 40);

        List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors([first, second], new Vector2I(60, 60), MaxDiameter);

        Assert.Equal(2, subsectors.Count);
        Assert.All([first, second], planet => Assert.Single(subsectors, s => s.Planets.Contains(planet)));
    }

    [Fact]
    public void BuildSubsectors_AssignsEachPlanetToExactlyOneSubsector()
    {
        Planet[] planets =
        [
            CreatePlanet(1, 10, 10),
            CreatePlanet(2, 15, 10),
            CreatePlanet(3, 40, 40),
            CreatePlanet(4, 45, 40)
        ];

        List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors(planets, new Vector2I(60, 60), MaxDiameter);

        foreach (Planet planet in planets)
        {
            Assert.Single(subsectors, s => s.Planets.Contains(planet));
        }
    }

    [Fact]
    public void BuildSubsectors_AssignsCellsAroundSubsectorPlanets()
    {
        Planet planet = CreatePlanet(1, 10, 10);

        Subsector subsector = Assert.Single(SubsectorBuilder.BuildSubsectors([planet], new Vector2I(30, 30), MaxDiameter));

        Assert.Contains(new Vector2I(10, 10), subsector.Cells);
        Assert.NotEmpty(subsector.Cells);
    }

    private static Planet CreatePlanet(int id, ushort x, ushort y)
    {
        return new Planet(id, $"Planet {id}", new Coordinate(x, y), 1, null, 1, 1);
    }
}
