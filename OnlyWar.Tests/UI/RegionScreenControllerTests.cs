using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.UI;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class RegionScreenControllerTests
{
    [Fact]
    public void BuildMissionsFlagTexts_ReturnsOneBadgePerPublicEnemyFaction()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        fixture.AddControllingFaction(0, "Tyranids", population: 5_000);
        fixture.AddPublicCult(0, population: 1_000, organization: 100);
        fixture.AddHiddenFaction(0, GrowthType.Conversion, population: 500);

        var badges = RegionScreenController.BuildMissionsFlagTexts(fixture.Planet.Regions[0]);

        Assert.Equal(["Genestealer Cult", "Tyranids"], badges);
    }
}
