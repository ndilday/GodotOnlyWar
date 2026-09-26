using Godot;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Domain;

// Covers the fog-of-war grading the UI relies on: intelligence and defensive values are only
// ever shown as fuzzy descriptions, and hidden factions do not leak into visible counts.
public class OpForVisibilityTests
{
    [Theory]
    [InlineData(0f, "None")]
    [InlineData(0.99f, "Basic")]
    [InlineData(1f, "Limited")]
    [InlineData(1.99f, "Limited")]
    [InlineData(2f, "Partial")]
    [InlineData(2.99f, "Partial")]
    [InlineData(3f, "Reliable")]
    [InlineData(3.99f, "Reliable")]
    [InlineData(4f, "Detailed")]
    [InlineData(5.99f, "Detailed")]
    [InlineData(6f, "Comprehensive")]
    [InlineData(20f, "Comprehensive")]
    public void GetIntelligenceLevelDescription_UsesPlayerFacingBands(
        float intelligence,
        string expected)
    {
        Assert.Equal(
            expected,
            RegionFactionDescriptionExtensions.GetIntelligenceLevelDescription(intelligence));
    }

    [Theory]
    [InlineData("None", 211, 47, 47)]
    [InlineData("Basic", 230, 74, 25)]
    [InlineData("Limited", 255, 179, 0)]
    [InlineData("Partial", 224, 224, 224)]
    [InlineData("Reliable", 174, 213, 129)]
    [InlineData("Detailed", 102, 187, 106)]
    [InlineData("Comprehensive", 56, 142, 60)]
    public void IntelligenceColor_UsesConfiguredPalette(
        string level,
        int red,
        int green,
        int blue)
    {
        Assert.Equal(
            Color.Color8((byte)red, (byte)green, (byte)blue),
            OnlyWarStyle.GetIntelligenceColor(level));
    }

    [Fact]
    public void GetPlayerVisibleIntel_EmptyPlayerPresence_DoesNotMaskPdfIntel()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        fixture.DefaultPlanetFaction.SetRegionAwareness(region, 2.6576f);

        Faction player = fixture.Sector.PlayerForce.Faction;
        fixture.Planet.PlanetFactionMap[player.Id] = new PlanetFaction(player)
        {
            IsPublic = true
        };

        Assert.Equal(2.6576f, region.GetPlayerVisibleIntel(), precision: 4);
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

    [Theory]
    [InlineData(0, "None")]
    [InlineData(2, "Minimal")]
    [InlineData(4, "Mediocre")]
    [InlineData(6, "Moderate")]
    [InlineData(8, "Heavy")]
    [InlineData(20, "Massive")]
    public void GetDefenseLevelDescription_NeverExposesRawValue(int level, string expected)
    {
        Assert.Equal(expected, RegionFactionDescriptionExtensions.GetDefenseLevelDescription(level));
    }
}
