using System;
using OnlyWar.Application;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class CampaignApplicationTests
{
    [Fact]
    public void InstallSwitchesThePublishedSessionWithoutRetainingThePreviousSession()
    {
        SectorSimulationFixture firstFixture = SectorSimulationFixture.Create();
        SectorSimulationFixture secondFixture = SectorSimulationFixture.CreateDetached();
        GameRulesData rules = GameDataSingleton.Instance.GameRulesData
            ?? throw new InvalidOperationException("The shared rules fixture was not initialized.");
        CampaignApplication application = new(new SeededRNG(101));
        GameSession first = new(rules, firstFixture.Sector, new Date(1, 1, 1), new SeededRNG(102));
        GameSession second = new(rules, secondFixture.Sector, new Date(2, 1, 1), new SeededRNG(103));

        application.Install(first);
        application.Install(second);

        Assert.Same(second, application.ActiveSession);
        Assert.Same(second.Sector, GameDataSingleton.Instance.Sector);
        Assert.Same(second.CurrentDate, GameDataSingleton.Instance.Date);
        Assert.NotSame(first.Sector, application.ActiveSession.Sector);
    }

    [Fact]
    public void FailedLoadLeavesTheInstalledSessionUntouched()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        GameRulesData rules = GameDataSingleton.Instance.GameRulesData
            ?? throw new InvalidOperationException("The shared rules fixture was not initialized.");
        CampaignApplication application = new(new SeededRNG(104));
        GameSession active = new(rules, fixture.Sector, new Date(3, 1, 1), new SeededRNG(105));
        application.Install(active);

        Assert.ThrowsAny<Exception>(() => application.LoadAndInstall(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "missing-onlywar-save.s3db")));

        Assert.Same(active, application.ActiveSession);
        Assert.Same(active.Sector, GameDataSingleton.Instance.Sector);
        Assert.Same(active.CurrentDate, GameDataSingleton.Instance.Date);
    }
}
