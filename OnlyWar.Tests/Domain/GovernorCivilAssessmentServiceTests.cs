using System.Collections.Generic;
using System.Drawing;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using Xunit;

namespace OnlyWar.Tests.Domain;

public class GovernorCivilAssessmentServiceTests
{
    [Fact]
    public void Assessment_IsQualitativeAndParanoiaCanReportTreasonOnSameWorld()
    {
        (Planet planet, Character governor) = BuildWorld(70);
        governor.Investigation = 1;
        governor.Paranoia = 0;
        Assert.Equal("The population is steadfastly loyal",
            GovernorCivilAssessmentService.Assess(planet, governor));

        governor.Paranoia = 1;
        Assert.Equal("Isolated sedition is suspected",
            GovernorCivilAssessmentService.Assess(planet, governor));
    }

    [Fact]
    public void OpenUnrestOverridesGovernorBias()
    {
        (Planet planet, Character governor) = BuildWorld(100);
        Faction rebels = Faction(2, false, GrowthType.Unrest);
        PlanetFaction rebelPlanet = new(rebels) { IsPublic = true };
        planet.PlanetFactionMap[rebels.Id] = rebelPlanet;
        planet.Regions[0].RegionFactionMap[rebels.Id] = new RegionFaction(rebelPlanet, planet.Regions[0])
        {
            IsPublic = true,
            Population = 10
        };

        Assert.Equal("Organized rebellion underway",
            GovernorCivilAssessmentService.Assess(planet, governor));
    }

    private static (Planet, Character) BuildWorld(float contentment)
    {
        Faction imperial = Faction(1, true, GrowthType.None);
        Planet planet = new(1, "Test", new Coordinate(0, 0), 1, null, 0, 0);
        Region region = new(0, planet, 0, "Capital", new RegionCoordinate(0, 0), 0);
        planet.Regions[0] = region;
        PlanetFaction planetFaction = new(imperial) { IsPublic = true };
        planet.PlanetFactionMap[imperial.Id] = planetFaction;
        region.RegionFactionMap[imperial.Id] = new RegionFaction(planetFaction, region)
        {
            IsPublic = true,
            Population = 1_000,
            Contentment = contentment
        };
        Character governor = new() { Investigation = 1, Paranoia = 0 };
        planetFaction.Leader = governor;
        return (planet, governor);
    }

    private static Faction Faction(int id, bool isDefault, GrowthType growthType) => new(
        id, $"Faction {id}", Color.Red, false, isDefault, growthType == GrowthType.Unrest,
        growthType, null, null, null, null, null, null, null);
}
