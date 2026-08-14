using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Planets;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

public class TacticalRegionControllerTests
{
    [Fact]
    public void PlayerOccupiedRegionColor_UsesImperialControllerWhileChapterSharesGround()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.CreateDetached();
        Region region = fixture.Planet.Regions[0];
        var player = fixture.Sector.PlayerForce.Faction;
        var playerPlanetFaction = new PlanetFaction(player) { IsPublic = true };
        fixture.Planet.PlanetFactionMap[player.Id] = playerPlanetFaction;
        region.RegionFactionMap[player.Id] = new RegionFaction(playerPlanetFaction, region)
        {
            IsPublic = true
        };

        Color actual = TacticalRegionController.GetPlayerOccupiedRegionColor(region);
        Color expected = fixture.Default.Color.ToGodotColor().Darkened(0.42f)
            .Lerp(new Color(0.08f, 0.10f, 0.10f), 0.18f);
        expected.A = 1f;

        Assert.Equal(expected, actual);
    }
}
