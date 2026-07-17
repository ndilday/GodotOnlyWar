using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Covers the fog-of-war grading the UI relies on: enemy population is hidden until recon
// raises player-visible RegionIntel, and defensive values are only ever shown as fuzzy
// descriptions (RegionFactionExtensions).
public class OpForVisibilityTests
{
    [Fact]
    public void GetPopulationDescription_HiddenFaction_RevealsNothing()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction cult = fixture.AddHiddenFaction(0, OnlyWar.Models.GrowthType.Logistic, population: 50000);
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[0], 6f);

        // a non-public faction is never described, regardless of intelligence
        Assert.Equal("None", cult.GetPopulationDescription());
    }

    [Fact]
    public void GetPopulationDescription_PublicFactionWithNoIntelligence_IsUnknown()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction enemy = fixture.AddControllingFaction(1, "Rebels", population: 12345);
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[1], 0f);

        Assert.Equal("Unknown", enemy.GetPopulationDescription());
    }

    [Fact]
    public void GetPopulationDescription_PartialIntelligence_RoundsToPrecision()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction enemy = fixture.AddControllingFaction(1, "Rebels", population: 12345);
        // intelligence 3 => divisor 10^(6-3) = 1000 => 12345 rounded down to 12000
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[1], 3f);

        Assert.Equal("12000", enemy.GetPopulationDescription());
    }

    [Fact]
    public void GetPopulationDescription_FullIntelligence_RevealsExactCount()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction enemy = fixture.AddControllingFaction(1, "Rebels", population: 12345);
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[1], 6f);

        Assert.Equal("12345", enemy.GetPopulationDescription());
    }

    [Fact]
    public void GetVisibleCivilianPopulation_HiddenDefaultFaction_RevealsNoCivilianCount()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached(defaultRegionPopulation: 20000);
        Region region = fixture.Planet.Regions[0];
        fixture.DefaultRegionFaction(0).IsPublic = false;

        Assert.True(region.HasHiddenDefaultFaction());
        Assert.Equal(0, region.GetVisibleCivilianPopulation());
    }

    [Fact]
    public void PlanetaryDefenseForces_HiddenDefaultFaction_DoesNotCountAsActiveGarrison()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached(defaultRegionPopulation: 20000);
        Region region = fixture.Planet.Regions[0];
        RegionFaction remnant = fixture.DefaultRegionFaction(0);
        remnant.Garrison = 5000;
        remnant.IsPublic = false;

        Assert.Equal(0, region.PlanetaryDefenseForces);
        Assert.Equal(5000, remnant.Garrison);
    }

    [Fact]
    public void GetVisibleEnemyRegionFaction_PublicEnemyTakesPriorityOverHiddenEnemy()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        RegionFaction hiddenCult = fixture.AddHiddenFaction(0, OnlyWar.Models.GrowthType.Conversion, population: 5000);
        RegionFaction tyranids = fixture.AddConsumptionFaction(0, population: 12000, organization: 100);

        Assert.Same(tyranids, fixture.Planet.Regions[0].GetVisibleEnemyRegionFaction());
        Assert.False(hiddenCult.IsPublic);
    }

    [Theory]
    [InlineData(0, "None")]
    [InlineData(2, "Minimal")]
    [InlineData(4, "Mediocre")]
    [InlineData(6, "Moderate")]
    [InlineData(8, "Heavy")]
    [InlineData(20, "Massive")]
    public void GetDefenseLevelDescription_NeverExposesRawValue(int level, string expected)
    {
        Assert.Equal(expected, RegionFactionExtensions.GetDefenseLevelDescription(level));
    }
}
