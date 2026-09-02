using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class PlanetaryOperationsIconAtlasTests
{
    [Theory]
    [InlineData("Imperium", false, true, "map_imperial")]
    [InlineData("Test Chapter", true, false, "map_player")]
    [InlineData("Tyranids", false, false, "map_tyranids")]
    [InlineData("Genestealer Cult", false, false, "map_genestealer_cult")]
    [InlineData("Orks", false, false, "map_orks")]
    public void PlanetaryOperationsFactionIcons_UseMapAtlasSlots(
        string name,
        bool isPlayer,
        bool isDefault,
        string expectedKey)
    {
        Faction faction = SectorSimulationFixture.BuildTestFaction(
            1, name, isPlayer, isDefault);

        Assert.Equal(expectedKey, IconAtlas.GetPlanetaryOperationsFactionIconKey(faction));
    }

    [Fact]
    public void PlanetaryOperationsFactionIcons_DoNotMisrepresentUnknownFactionAsOrks()
    {
        Faction faction = SectorSimulationFixture.BuildTestFaction(
            1, "Red Corsairs", isPlayer: false, isDefault: false);

        Assert.Null(IconAtlas.GetPlanetaryOperationsFactionIconKey(faction));
    }
}
