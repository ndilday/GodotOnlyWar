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
        GameRulesData rules = firstFixture.Rules
            ?? throw new InvalidOperationException("The rules fixture was not initialized.");
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(101)).CreateApplication();
        GameSession first = new(rules, firstFixture.Sector, new Date(1, 1, 1), new SeededRNG(102));
        GameSession second = new(rules, secondFixture.Sector, new Date(2, 1, 1), new SeededRNG(103));

        application.Install(first);
        application.Install(second);

        Assert.Same(second, application.ActiveSession);
        Assert.Same(second.Sector, application.ActiveSession.Sector);
        Assert.Same(second.CurrentDate, application.ActiveSession.CurrentDate);
        Assert.NotSame(first.Sector, application.ActiveSession.Sector);
    }

    [Fact]
    public void FailedLoadLeavesTheInstalledSessionUntouched()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        GameRulesData rules = fixture.Rules
            ?? throw new InvalidOperationException("The rules fixture was not initialized.");
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(104)).CreateApplication();
        GameSession active = new(rules, fixture.Sector, new Date(3, 1, 1), new SeededRNG(105));
        application.Install(active);

        Assert.ThrowsAny<Exception>(() => application.LoadAndInstall(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "missing-onlywar-save.s3db")));

        Assert.Same(active, application.ActiveSession);
        Assert.Same(active.Sector, application.ActiveSession.Sector);
        Assert.Same(active.CurrentDate, application.ActiveSession.CurrentDate);
    }
}
