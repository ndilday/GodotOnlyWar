using OnlyWar.Helpers;
using OnlyWar.Helpers.Turns;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class TurnControllerResultTests
{
    [Fact]
    public void ProcessTurn_ReturnsResolutionCollections()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        TurnController controller = new();

        TurnResolutionResult result = controller.ProcessTurn(fixture.Sector);

        Assert.NotNull(result.MissionContexts);
        Assert.NotNull(result.SpecialMissions);
        Assert.NotNull(result.StrategicCombatResults);
    }
}
