using OnlyWar.Domain;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Campaign.Turns;
using OnlyWar.Tests.Fixtures;
using System;
using Xunit;

namespace OnlyWar.Tests.Turns;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class GameSessionTurnControllerTests
{
    [Fact]
    public void ProcessTurn_UsesInjectedSessionDateAndRandomSource()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Date fixtureDate = fixture.CurrentDate;
        Date sessionDate = new(9, 321, 17);
        CountingRng sessionRandom = new();
        GameSession session = new(
            fixture.Rules,
            fixture.Sector,
            sessionDate,
            sessionRandom);

        TestCampaignComposition composition =
            TestPersonnelComposition.CreateCampaign(sessionRandom);
        TurnResolutionResult result = composition.CreateTurnController(session)
            .ProcessTurn(fixture.Sector);

        Assert.NotNull(result);
        Assert.Equal(18, sessionDate.Week);
        Assert.Equal(1, fixtureDate.Week);
        Assert.True(sessionRandom.LinearDoubleCalls > 0);
    }

    [Fact]
    public void ProcessTurn_RejectsSectorOutsideInjectedSession()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        GameSession session = new(
            fixture.Rules,
            fixture.Sector,
            new Date(9, 321, 17),
            new CountingRng());
        TestCampaignComposition composition =
            TestPersonnelComposition.CreateCampaign(session.Random);
        TurnController controller = composition.CreateTurnController(session);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => controller.ProcessTurn(new Sector()));

        Assert.Equal("sector", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsMinimalNpcOnlySessionWithoutPlayerForce()
    {
        Sector sector = new();
        GameSession session = new(
            SectorSimulationFixture.Create().Rules,
            sector,
            new Date(9, 321, 17),
            new CountingRng());

        TestCampaignComposition composition =
            TestPersonnelComposition.CreateCampaign(session.Random);
        TurnController controller = composition.CreateTurnController(session);

        Assert.NotNull(controller);
    }

    private sealed class CountingRng : IRNG
    {
        internal int LinearDoubleCalls { get; private set; }

        public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;

        public double GetLinearDouble()
        {
            LinearDoubleCalls++;
            return 0.0;
        }

        public int GetIntBelowMax(int min, int max) => min;

        public double NextRandomZValue() => 0.0;
    }
}
