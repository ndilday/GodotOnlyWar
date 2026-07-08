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
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction cult = fixture.AddHiddenFaction(0, OnlyWar.Models.GrowthType.Logistic, population: 50000);
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[0], 6f);

        // a non-public faction is never described, regardless of intelligence
        Assert.Equal("None", cult.GetPopulationDescription());
    }

    [Fact]
    public void GetPopulationDescription_PublicFactionWithNoIntelligence_IsUnknown()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(1, "Rebels", population: 12345);
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[1], 0f);

        Assert.Equal("Unknown", enemy.GetPopulationDescription());
    }

    [Fact]
    public void GetPopulationDescription_PartialIntelligence_RoundsToPrecision()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(1, "Rebels", population: 12345);
        // intelligence 3 => divisor 10^(6-3) = 1000 => 12345 rounded down to 12000
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[1], 3f);

        Assert.Equal("12000", enemy.GetPopulationDescription());
    }

    [Fact]
    public void GetPopulationDescription_FullIntelligence_RevealsExactCount()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        RegionFaction enemy = fixture.AddControllingFaction(1, "Rebels", population: 12345);
        fixture.DefaultPlanetFaction.SetRegionIntel(fixture.Planet.Regions[1], 6f);

        Assert.Equal("12345", enemy.GetPopulationDescription());
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
