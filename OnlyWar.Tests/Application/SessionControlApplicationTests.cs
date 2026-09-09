using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OnlyWar.Application;
using OnlyWar.Domain;
using OnlyWar.Campaign.Settings;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class SessionControlApplicationTests
{
    [Fact]
    public void SaveIsRejectedForAReplacedSessionAndWithoutStorage()
    {
        CampaignApplication application = CreateApplication();
        Guid staleToken = application.SessionToken;
        application.Install(CreateSession(application.ActiveSession.Rules));

        // The replaced campaign token is refused outright, while the explicitly composed
        // application can save the currently installed campaign.
        Assert.False(application.SaveCampaign(
            new(staleToken, SaveCampaignKind.Manual, "Stale")).Succeeded);
        SaveCampaignResult current = application.SaveCampaign(
            new(application.SessionToken, SaveCampaignKind.Manual, "Current"));
        Assert.True(current.Succeeded, current.Message);
    }

    [Fact]
    public void StatusReportsTheInstalledCampaignAndItsRecoverability()
    {
        CampaignApplication application = CreateApplication();

        CampaignStatusView installed = application.QueryStatus();
        Assert.True(installed.HasCampaign);
        Assert.Equal("Chapter", installed.CampaignName);

        application.MarkChanged();
        Assert.True(application.QueryStatus().IsDirty);

        application.Close();
        Assert.False(application.QueryStatus().HasCampaign);
    }

    [Fact]
    public void PreflightAndRecruitmentGateComeFromTheActiveSession()
    {
        CampaignApplication application = CreateApplication();

        // No recruitment program at all is not an incomplete setup, so the turn is not gated.
        Assert.False(application.RequiresRecruitmentSetup());
        Assert.False(application.QueryEndTurnPreflight(
            new EndTurnWarningPreferences()).RequiresConfirmation);

        application.Close();
        Assert.Empty(application.QueryEndTurnPreflight(new EndTurnWarningPreferences()).Items);
    }

    [Theory]
    [InlineData(typeof(CampaignStatusView))]
    [InlineData(typeof(SaveCampaignResult))]
    [InlineData(typeof(SaveCampaignCommand))]
    [InlineData(typeof(ChronicleView))]
    public void SessionControlBoundaryContainsNoLiveDomainGraph(Type root)
    {
        HashSet<Type> visited = [];
        List<Type> forbidden = [];
        void Inspect(Type type)
        {
            if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string)
                || type == typeof(Guid) || type == typeof(decimal) || type == typeof(DateTime))
                return;
            if (type.IsArray) { Inspect(type.GetElementType()); return; }
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments()) Inspect(argument);
                return;
            }
            if (type.Assembly == typeof(Sector).Assembly) { forbidden.Add(type); return; }
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Inspect(property.PropertyType);
            }
        }
        Inspect(root);
        Assert.Empty(forbidden);
    }

    [Fact]
    public void SessionAndReportScreensUseOnlyTheApplicationBoundary()
    {
        foreach ((string folder, string file) in new[]
        {
            ("MainGameScreen", "MainGameScene.CampaignControls.cs"),
            ("CommandScreen", "CommandScreenController.cs"),
            ("StartMenu", "StartMenu.ReleaseControls.cs"),
            ("BattleReviewScreen", "BattleReviewController.cs")
        })
        {
            string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
                RulesDatabaseFixture.RepositoryRoot, "Scenes", folder, file));
            Assert.DoesNotContain("GameDataSingleton", source);
            Assert.DoesNotContain("CurrentCampaignSaveWriter", source);
        }
    }

    private static CampaignApplication CreateApplication()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(41)).CreateApplication();
        application.Install(CreateSession(fixture.Rules));
        return application;
    }

    private static GameSession CreateSession(GameRulesData rules)
    {
        Unit chapter = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        Squad squad = new("Squad", chapter, TestModelFactory.SquadTemplate);
        chapter.AddSquad(squad);
        PlayerForce force = new(null,
            new Army("Army", null, "Commander", chapter, new List<PlayerSoldier>()),
            new Fleet("Fleet", null, null));
        return new GameSession(rules, new Sector(force, [], [], []),
            new Date(20_000), new SeededRNG(42));
    }
}
