using OnlyWar.Helpers;
using OnlyWar.Helpers.Turns;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Turns;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public class TurnResolutionResultTests
{
    [Fact]
    public void ProcessTurn_ReturnsResultExposedByCompatibilityProperties()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        TurnController controller = new();

        TurnResolutionResult result = controller.ProcessTurn(fixture.Sector);

        Assert.Same(result.MissionContexts, controller.MissionContexts);
        Assert.Same(result.SpecialMissions, controller.SpecialMissions);
        Assert.Same(result.StrategicCombatResults, controller.StrategicCombatResults);
        Assert.Equal(result.ScenarioNotification, controller.ScenarioNotification);
    }
}
