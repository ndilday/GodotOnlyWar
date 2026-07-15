using System.Drawing;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class RegionFactionCivilStateTests
{
    [Fact]
    public void ArmedPools_AreDisjointSubsetsOfPopulation()
    {
        RegionFaction unrest = CreateUnrest(100);

        unrest.Garrison = 70;
        unrest.ArmedCivilians = 70;

        Assert.Equal(70, unrest.Garrison);
        Assert.Equal(30, unrest.ArmedCivilians);

        unrest.Population = 50;

        Assert.Equal(50, unrest.Garrison);
        Assert.Equal(0, unrest.ArmedCivilians);
    }

    [Fact]
    public void Contentment_StaysOnCivilScaleAndNaNReturnsToSafeDefault()
    {
        RegionFaction unrest = CreateUnrest(100);

        unrest.Contentment = -1;
        Assert.Equal(0, unrest.Contentment);
        unrest.Contentment = 101;
        Assert.Equal(100, unrest.Contentment);
        unrest.Contentment = float.NaN;
        Assert.Equal(70, unrest.Contentment);
    }

    [Fact]
    public void UnrestMilitaryStrength_IncludesEmbeddedPdfAndArmedCivilians()
    {
        RegionFaction unrest = CreateUnrest(1_000);
        unrest.Garrison = 70;
        unrest.ArmedCivilians = 130;

        Assert.Equal(200, unrest.MilitaryStrength);
    }

    private static RegionFaction CreateUnrest(long population)
    {
        Faction faction = new(
            99, "Insurrectionists", Color.Red, false, false, true, GrowthType.Unrest,
            null, null, null, null, null, null, null);
        Planet planet = new(1, "Test", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(1, planet, 0, "Capital", new RegionCoordinate(0, 0), 0);
        PlanetFaction planetFaction = new(faction) { IsPublic = false };
        return new RegionFaction(planetFaction, region) { Population = population };
    }
}
