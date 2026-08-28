using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class PlanetaryOperationsIconAtlasTests
{
    [Theory]
    [InlineData("control_contested")]
    [InlineData("mission_recon")]
    [InlineData("mission_defend")]
    [InlineData("mission_patrol")]
    [InlineData("mission_attack")]
    [InlineData("mission_diversion")]
    [InlineData("fortification_entrenchment")]
    [InlineData("fortification_listening_post")]
    [InlineData("fortification_anti_air")]
    [InlineData("mission_ambush")]
    [InlineData("mission_sabotage")]
    [InlineData("mission_show_of_force")]
    [InlineData("order_active")]
    [InlineData("order_assigned")]
    [InlineData("order_unassigned")]
    public void PlanetaryOperationsIcons_AreRegistered(string key)
    {
        Assert.True(IconAtlas.HasIcon(key));
    }

    [Theory]
    [InlineData("Imperium", false, true, "map_imperial")]
    [InlineData("Test Chapter", true, false, "map_player")]
    [InlineData("Tyranids", false, false, "map_tyranids")]
    [InlineData("Genestealer Cult", false, false, "map_genestealer_cult")]
    [InlineData("Orks", false, false, "map_orks")]
    [InlineData("Red Corsairs", false, false, "map_orks")]
    public void PlanetaryOperationsFactionIcons_UseMapAtlasSlots(
        string name,
        bool isPlayer,
        bool isDefault,
        string expectedKey)
    {
        Faction faction = SectorSimulationFixture.BuildTestFaction(
            1, name, isPlayer, isDefault);

        Assert.Equal(expectedKey, IconAtlas.GetPlanetaryOperationsFactionIconKey(faction));
        Assert.True(IconAtlas.HasPlanetaryOperationsFactionIcon(expectedKey));
    }
}
