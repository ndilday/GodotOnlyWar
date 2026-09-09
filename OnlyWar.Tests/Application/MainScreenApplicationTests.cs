using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OnlyWar.Application;
using OnlyWar.Domain;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Reports;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

/// <summary>
/// SB-11b: the main game screen's own campaign reads and commands. These assert the decisions the
/// scene used to make for itself - which world to open on, whether the founding directive is
/// outstanding, what the turn report says, and which squads can take a neophyte - now come from
/// the application, and that a replaced session cannot be written through a stale token.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class MainScreenApplicationTests
{
    [Fact]
    public void HeaderReportsTheActiveSessionAndEmptiesWhenTheCampaignCloses()
    {
        CampaignApplication application = CreateApplication();
        application.ActiveSession.Sector.PlayerForce.Army.Requisition = 1234;

        CampaignHeaderView header = application.QueryHeader();
        Assert.Equal(application.ActiveSession.CurrentDate.ToString(), header.DateText);
        Assert.Equal(1234, header.Requisition);

        application.Close();
        Assert.Equal(new CampaignHeaderView("", 0), application.QueryHeader());
    }

    [Fact]
    public void StartupOpensOnTheOrbitedWorldAndFallsBackToACharteredOne()
    {
        Planet promised = CreatePlanet(1);
        Planet other = CreatePlanet(2);

        // No task force in orbit: any charted world is better than opening on nothing.
        CampaignApplication application = CreateApplication(planets: [promised, other]);
        Assert.Equal(promised.Id, application.QueryStartup().InitialPlanetId);

        // With the chapter fleet in orbit, the campaign opens on the world it is orbiting.
        GameSession orbiting = CreateSession(
            application.ActiveSession.Rules, [promised, other], orbitedPlanet: other);
        application.Install(orbiting);
        Assert.Equal(other.Id, application.QueryStartup().InitialPlanetId);
    }

    [Fact]
    public void OpeningBriefIsPendingOnlyWhileUnacknowledgedAndRefusesAReplacedSession()
    {
        CampaignApplication application = CreateApplication();
        CampaignScenario scenario = new(ScenarioType.PromisedWorld, 1, "Take the world.", 0);
        application.ActiveSession.Sector.Scenario = scenario;

        Assert.True(application.QueryStartup().OpeningBriefPending);

        // A token from a campaign that has since been replaced must not mark the new one read.
        Guid staleToken = application.SessionToken;
        application.Install(CreateSession(application.ActiveSession.Rules));
        application.ActiveSession.Sector.Scenario = scenario;
        application.AcknowledgeOpeningBrief(staleToken);
        Assert.False(scenario.BriefingAcknowledged);

        application.AcknowledgeOpeningBrief(application.SessionToken);
        Assert.True(scenario.BriefingAcknowledged);
        Assert.False(application.QueryStartup().OpeningBriefPending);
    }

    [Fact]
    public void LastTurnReportRendersTheSavedSnapshotAndSaysSoWhenThereIsNone()
    {
        CampaignApplication application = CreateApplication();

        TurnReportView missing = application.QueryLastTurnReport();
        Assert.Empty(missing.Entries);
        Assert.Contains("No previous turn report", missing.EmptyMessage);

        application.ActiveSession.Sector.PlayerForce.LastTurnReportSnapshot = new(
            new Date(42, 123, 7).GetTotalWeeks(),
            [new LastTurnReportEntrySnapshot(
                "Recon", "Third Squad", "Recon completed.", "CONTACT", false, null)]);

        TurnReportView restored = application.QueryLastTurnReport();
        EndOfTurnReportEntry entry = Assert.Single(restored.Entries);
        Assert.Equal("Recon", entry.Title);
        // A restored snapshot never carries a replay, so the card cannot offer one.
        Assert.False(entry.CanOpenDebrief);
        Assert.Equal(new Date(42, 123, 7).ToString(), restored.ResolvedDateLabel);
        Assert.Null(restored.EmptyMessage);
    }

    [Fact]
    public void ResolveTurnRejectsAReplacedSessionAndLeavesThePreviousReportIntact()
    {
        CampaignApplication application = CreateApplication();
        LastTurnReportSnapshot previous = new(7, []);
        PlayerForce replaced = application.ActiveSession.Sector.PlayerForce;
        replaced.LastTurnReportSnapshot = previous;
        Guid staleToken = application.SessionToken;
        application.Install(CreateSession(application.ActiveSession.Rules));

        ResolveTurnView refused = application.ResolveTurn(staleToken);
        Assert.False(refused.Succeeded);
        Assert.Null(refused.Report);
        // Neither campaign advanced: the replaced one keeps its report and the new one gains none.
        Assert.Same(previous, replaced.LastTurnReportSnapshot);
        Assert.Null(application.ActiveSession.Sector.PlayerForce.LastTurnReportSnapshot);
    }

    [Fact]
    public void NeophytePlacementExplainsWhyThereIsNoTargetAndRefusesAReplacedSession()
    {
        CampaignApplication application = CreateApplication();

        // No recruitment program: the reason is a campaign fact, not a message the screen writes.
        NeophytePlacementOptions options = application.QueryNeophytePlacementTargets();
        Assert.False(options.IsAvailable);
        Assert.Contains("recruitment program", options.UnavailableReason);
        Assert.Empty(options.Targets);

        Guid staleToken = application.SessionToken;
        application.Install(CreateSession(application.ActiveSession.Rules));
        NeophytePlacementResult refused = application.PlaceNeophyte(staleToken, 1, 2);
        Assert.False(refused.Succeeded);
        Assert.Contains("campaign changed", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(CampaignHeaderView))]
    [InlineData(typeof(MainScreenStartupView))]
    [InlineData(typeof(ResolveTurnView))]
    [InlineData(typeof(NeophytePlacementOptions))]
    [InlineData(typeof(NeophytePlacementResult))]
    [InlineData(typeof(TurnReportView))]
    [InlineData(typeof(EndOfTurnReportEntry))]
    [InlineData(typeof(BattleReplayDisplay))]
    public void MainScreenBoundaryContainsNoLiveDomainGraph(Type root)
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
    public void TheTurnReportDialogNoLongerDecidesWhatThePlayerIsTold()
    {
        string controller = ReadScene("EndOfTurnDialogController.cs");
        string view = ReadScene("EndOfTurnDialogView.cs");
        foreach (string source in new[] { controller, view })
        {
            Assert.DoesNotContain("GameDataSingleton", source);
            Assert.DoesNotContain("MissionContext", source);
            Assert.DoesNotContain("StrategicCombatResult", source);
            Assert.DoesNotContain("GetPlayerVisibleIntel", source);
        }
        Assert.DoesNotContain("NpcMissionReportBuilder", controller);
        Assert.DoesNotContain("LastTurnReportSnapshot", controller);
    }

    [Fact]
    public void MainGameSceneNoLongerResolvesItsOwnCampaignFactsOrMutatesTheCampaign()
    {
        string source = ReadScene(Path.Combine("MainGameScreen", "MainGameScene.cs"));
        // Turn resolution, the persisted report, the founding directive and neophyte placement
        // are all application commands now. This source audit also guards against reintroducing a
        // global campaign read.
        Assert.DoesNotContain("TurnResolutionResult", source);
        Assert.DoesNotContain("LastTurnReportSnapshot", source);
        Assert.DoesNotContain("BriefingAcknowledged", source);
        Assert.DoesNotContain("RecruitmentPromotionService", source);
        Assert.DoesNotContain("GameDataSingleton.Instance.Date", source);
        Assert.DoesNotContain("Army.Requisition", source);
    }

    private static string ReadScene(string relativePath) =>
        File.ReadAllText(Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Scenes", relativePath));

    private static CampaignApplication CreateApplication(IReadOnlyList<Planet> planets = null)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(41)).CreateApplication();
        application.Install(CreateSession(fixture.Rules, planets));
        return application;
    }

    private static GameSession CreateSession(
        GameRulesData rules,
        IReadOnlyList<Planet> planets = null,
        Planet orbitedPlanet = null)
    {
        Unit chapter = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        Squad squad = new("Squad", chapter, TestModelFactory.SquadTemplate);
        chapter.AddSquad(squad);
        Fleet fleet = new("Fleet", null, null);
        PlayerForce force = new(null,
            new Army("Army", null, "Commander", chapter, new List<PlayerSoldier>()),
            fleet);
        List<TaskForce> taskForces = [];
        if (orbitedPlanet != null)
        {
            TaskForce taskForce = new(900, null, null, orbitedPlanet, null, []);
            fleet.TaskForces.Add(taskForce);
            taskForces.Add(taskForce);
        }
        return new GameSession(
            rules,
            new Sector(force, [], [.. planets ?? []], taskForces),
            new Date(20_000),
            new SeededRNG(42));
    }

    private static Planet CreatePlanet(int id) =>
        new(id, $"Planet {id}", new Coordinate((ushort)id, (ushort)id), 1, null, 1, 0);
}
