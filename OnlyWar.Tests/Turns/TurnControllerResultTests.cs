using OnlyWar.Domain;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Campaign.Turns;
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
        TestCampaignComposition composition =
            TestPersonnelComposition.CreateCampaign(new StaticRNG());
        TurnController controller = composition.CreateTurnController(new GameSession(
            fixture.Rules, fixture.Sector, fixture.CurrentDate, new StaticRNG()));

        TurnResolutionResult result = controller.ProcessTurn(fixture.Sector);

        Assert.NotNull(result.MissionContexts);
        Assert.NotNull(result.SpecialMissions);
        Assert.NotNull(result.StrategicCombatResults);
    }
}
