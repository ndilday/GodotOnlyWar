using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
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
        Date singletonDate = GameDataSingleton.Instance.Date;
        Date sessionDate = new(9, 321, 17);
        CountingRng sessionRandom = new();
        GameSession session = new(
            GameDataSingleton.Instance.GameRulesData,
            fixture.Sector,
            sessionDate,
            sessionRandom);

        TurnResolutionResult result = new TurnController(session).ProcessTurn(fixture.Sector);

        Assert.NotNull(result);
        Assert.Equal(18, sessionDate.Week);
        Assert.Equal(1, singletonDate.Week);
        Assert.True(sessionRandom.LinearDoubleCalls > 0);
    }

    [Fact]
    public void ProcessTurn_RejectsSectorOutsideInjectedSession()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        GameSession session = new(
            GameDataSingleton.Instance.GameRulesData,
            fixture.Sector,
            new Date(9, 321, 17),
            new CountingRng());
        TurnController controller = new(session);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => controller.ProcessTurn(new Sector()));

        Assert.Equal("sector", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsMinimalNpcOnlySessionWithoutPlayerForce()
    {
        Sector sector = new();
        GameSession session = new(
            GameDataSingleton.Instance.GameRulesData,
            sector,
            new Date(9, 321, 17),
            new CountingRng());

        TurnController controller = new(session);

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
