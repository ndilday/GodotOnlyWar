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
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Application;

/// <summary>
/// SB-11c: the roster, fleet, training, diplomacy and map screens. These assert that the rules
/// those screens used to apply for themselves - transfer legality, fleet re-tasking, doctrine
/// validation, roster ordering and map geometry - now come from the application, that a replaced
/// campaign cannot be written through a stale token, and that no live campaign object reaches a
/// screen through a returned view.
/// </summary>
[Collection(OnlyWar.Tests.TestCollections.SharedState)]
public sealed class RosterAndMapScreenApplicationTests
{
    // ----- Fleet -------------------------------------------------------------------------

    [Fact]
    public void SquadTransferNeedsBerthsAndASharedLocation()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out Ship destination, out Ship distant, out Squad squad);

        Assert.True(application.CanTransferSquadToShip(squad.Id, destination.Id));
        // A ship parked at another world is not a berth this squad can walk to.
        Assert.False(application.CanTransferSquadToShip(squad.Id, distant.Id));
        // Nor is the berth it already occupies.
        Assert.False(application.CanTransferSquadToShip(squad.Id, source.Id));

        Assert.True(application.TransferSquadToShip(
            application.SessionToken, squad.Id, destination.Id).Succeeded);
        Assert.Same(destination, squad.BoardedLocation);
        Assert.DoesNotContain(squad, source.LoadedSquads);
    }

    [Fact]
    public void ATransferQueuedAgainstAReplacedCampaignIsRefused()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out Ship destination, out _, out Squad squad);
        Guid staleToken = application.SessionToken;
        application.Install(CreateEmptySession(application.ActiveSession.Rules));

        FleetCommandResult refused = application.TransferSquadToShip(
            staleToken, squad.Id, destination.Id);

        Assert.False(refused.Succeeded);
        Assert.Same(source, squad.BoardedLocation);
    }

    [Fact]
    public void DividingRefusesToEmptyTheOriginalTaskForce()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out Ship destination, out _, out _);
        TaskForce taskForce = source.Fleet;

        FleetDivideSelectionView whole = application.EvaluateDivideSelection(
            taskForce.Id, [source.Id, destination.Id]);
        Assert.False(whole.CanDivide);
        Assert.Contains("At least one ship must remain", whole.Detail);
        Assert.False(application.DivideFleet(
            application.SessionToken, taskForce.Id, [source.Id, destination.Id]).Succeeded);
        Assert.Equal(2, taskForce.Ships.Count);

        Assert.True(application.EvaluateDivideSelection(taskForce.Id, [destination.Id]).CanDivide);
        Assert.True(application.DivideFleet(
            application.SessionToken, taskForce.Id, [destination.Id]).Succeeded);
        Assert.Single(taskForce.Ships);
    }

    [Fact]
    public void OnlyAPlayerTaskForceInOrbitOffersReTasking()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out _, out Ship distant, out _);

        Assert.True(application.QueryFleetActions(source.Fleet.Id).IsActionable);
        Assert.True(application.QueryFleetActions(source.Fleet.Id).CanDivide);

        source.Fleet.TravelPhase = FleetTravelPhase.InWarp;
        Assert.False(application.QueryFleetActions(source.Fleet.Id).IsActionable);

        // The other task force is at a different world, so it is never a merge candidate.
        Assert.False(application.QueryFleetActions(distant.Fleet.Id).CanMerge);
    }

    [Fact]
    public void TheFleetScreenIsHandedNoLiveCampaignObject()
    {
        CampaignApplication application = CreateFleetCampaign(out _, out _, out _, out _);
        Assert.NotEmpty(application.QueryFleetScreen().Fleets);
        AssertDetached(typeof(FleetRosterView));
        AssertDetached(typeof(FleetMoveOptionsView));
        AssertDetached(typeof(FleetDivideOptionsView));
        AssertDetached(typeof(FleetMergeOptionsView));
    }

    // ----- System inspector and sector map -----------------------------------------------

    [Fact]
    public void TheInspectorPreselectsTheFirstActionableChapterFleet()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out _, out _, out _);
        Planet planet = source.Fleet.Planet;

        SystemInspectorView view = application.QuerySystemInspector(
            planet.Id, selectedFleetId: null, includeDossier: false);

        Assert.True(view.HasSystem);
        Assert.Equal(source.Fleet.Id, view.SelectedFleetId);
        Assert.True(view.SelectedFleetActions.IsActionable);
        Assert.All(view.OrbitingFleets, fleet => Assert.True(fleet.IsPlayerFleet));
        Assert.Null(view.Dossier);
    }

    [Fact]
    public void TheInspectorDropsASelectionThatHasLeftOrbit()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out _, out Ship distant, out _);
        Planet planet = source.Fleet.Planet;

        SystemInspectorView view = application.QuerySystemInspector(
            planet.Id, selectedFleetId: distant.Fleet.Id, includeDossier: false);

        // The requested fleet orbits another world, so the inspector falls back rather than
        // offering actions against a task force that is not there.
        Assert.Equal(source.Fleet.Id, view.SelectedFleetId);
    }

    [Fact]
    public void AnUnknownWorldRendersTheInspectorsEmptyState()
    {
        CampaignApplication application = CreateFleetCampaign(out _, out _, out _, out _);

        Assert.False(application.QuerySystemInspector(-1, null, true).HasSystem);
        Assert.False(application.QuerySystemInspector(null, null, true).HasSystem);
    }

    [Fact]
    public void TheSectorMapGetsMarkersAndGeometryRatherThanTheSector()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out _, out _, out _);

        Assert.True(application.TryQuerySectorGrid(out int width, out int height));
        Assert.True(width > 0 && height > 0);

        SectorMapGeometryView geometry = application.QuerySectorMapGeometry(true);
        Assert.True(geometry.HasCampaign);
        Assert.NotEmpty(geometry.Planets);
        Assert.NotNull(geometry.OpeningCenter);

        // A task force in the warp is out of contact and is not drawn at all.
        int visibleBefore = application.QuerySectorMapFleets().Count;
        source.Fleet.TravelPhase = FleetTravelPhase.InWarp;
        Assert.Equal(visibleBefore - 1, application.QuerySectorMapFleets().Count);

        AssertDetached(typeof(SectorMapGeometryView));
        AssertDetached(typeof(SectorMapSelectionView));
        AssertDetached(typeof(SectorMapPlanetLabelFacts));
    }

    [Fact]
    public void TheSelectedSystemOverlayNamesItsOrbitingFleets()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out _, out _, out _);

        SectorMapSelectionView selection =
            application.QuerySectorMapSelection(source.Fleet.Planet.Id);

        Assert.True(selection.Exists);
        Assert.Contains(selection.OrbitingFleets, fleet => fleet.FleetId == source.Fleet.Id);
        Assert.All(selection.OrbitingFleets, fleet => Assert.True(fleet.IsPlayerFleet));
    }

    // ----- Diplomacy ---------------------------------------------------------------------

    [Fact]
    public void TheDiplomacyBoardSaysSoWhenNoGovernorIsPetitioning()
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        CampaignApplication application = InstallFixture(fixture);

        IReadOnlyList<TreeNode> entries = application.QueryDiplomacy().Entries;

        Assert.Single(entries);
        Assert.Contains("No outstanding requests", entries[0].Name);
        Assert.False(entries[0].Selectable);
        AssertDetached(typeof(DiplomacyBoardView));
    }

    // ----- Chapter roster ----------------------------------------------------------------

    [Fact]
    public void TheChapterBrowserRendersItsOwnEmptyStateWithoutAChapter()
    {
        CampaignApplication application = CreateFleetCampaign(out _, out _, out _, out _);

        ChapterBrowserView view = application.QueryChapterBrowser(
            new ChapterBrowserQuery(null, null, null, null, null, [], null));

        Assert.False(view.HasChapter);
        Assert.False(application.HasChapter);
        Assert.Equal("No Chapter Data", view.Detail.Title);
        Assert.Empty(view.TransferOptions);
    }

    [Fact]
    public void TheChapterBrowserRendersTheSquadRosterAndItsTransferOptions()
    {
        CampaignApplication application = CreateChapterCampaign(out Squad squad);

        ChapterBrowserView view = application.QueryChapterBrowser(
            new ChapterBrowserQuery(null, squad.Id, null, null, null, [], null));

        Assert.True(view.HasChapter);
        Assert.Equal("Battle Brothers", view.LeftMenuTitle);
        Assert.Equal(squad.Members.Count, view.LeftMenu.Count);
        Assert.NotNull(view.DetailSoldierId);
        Assert.Equal(squad.Id, view.DetailSoldierSquadId);
        Assert.Equal(view.LeftMenu.Count, view.ContextSoldierIds.Count);
        AssertDetached(typeof(ChapterBrowserView));
    }

    [Fact]
    public void ATransferConfirmedAgainstAReplacedCampaignIsRefused()
    {
        CampaignApplication application = CreateChapterCampaign(out Squad squad);
        int soldierId = squad.Members.First().Id;
        Guid staleToken = application.SessionToken;
        application.Install(CreateEmptySession(application.ActiveSession.Rules));

        ChapterTransferResult refused = application.ConfirmTransfer(
            staleToken, soldierId, 0, [soldierId]);

        Assert.False(refused.Succeeded);
        Assert.False(refused.DidTransfer);
    }

    // ----- Muster ------------------------------------------------------------------------

    [Fact]
    public void ReplacingTheCampaignDiscardsTheStagedMusterPlan()
    {
        CampaignApplication application = CreateChapterCampaign(out _);
        Assert.Equal(0, application.StagedActionCount);

        // Nothing legal to stage in this fixture; the point is that the plan and its commit are
        // bound to the session that raised them.
        Guid staleToken = application.SessionToken;
        application.Install(CreateEmptySession(application.ActiveSession.Rules));

        Assert.Equal(0, application.StagedActionCount);
        Assert.False(application.CommitMusterPlan(staleToken).Succeeded);
        Assert.Null(application.StageMusterAction(staleToken, 1, "squad:1:1"));
    }

    [Fact]
    public void TheMusterScopeSelectorListsTheChapterAndItsCompanies()
    {
        CampaignApplication application = CreateChapterCampaign(out _);

        IReadOnlyList<MusterScopeOption> scopes = application.QueryMusterScopes();

        Assert.NotEmpty(scopes);
        Assert.Equal(0, scopes[0].Id);
        Assert.Contains("ENTIRE CHAPTER", scopes[0].Label);
        AssertDetached(typeof(MusterFormationRow));
        AssertDetached(typeof(MusterPlanView));
    }

    // ----- Loadouts and chapter doctrine --------------------------------------------------

    [Fact]
    public void TheSquadLoadoutScreenReadsAndWritesThroughCommands()
    {
        CampaignApplication application = CreateChapterCampaign(out Squad squad);

        SquadLoadoutView view = application.QuerySquadLoadout(squad.Id);
        Assert.True(view.Exists);
        Assert.Equal(squad.Name, view.Title);

        Assert.True(application.SetSquadLoadout(
            application.SessionToken, squad.Id, view.Loadout).Succeeded);
        Assert.False(squad.UsesLoadoutDoctrine);

        Assert.True(application.ReturnSquadToDoctrine(
            application.SessionToken, squad.Id).Succeeded);
        Assert.True(squad.UsesLoadoutDoctrine);

        AssertDetached(typeof(SquadLoadoutView), allowRulesTemplates: true);
    }

    [Fact]
    public void OperationalDoctrineIsStagedAsValuesAndOnlySavedByItsOwnSession()
    {
        CampaignApplication application = CreateChapterCampaign(out _);
        ChapterOperationalDoctrine live =
            application.ActiveSession.Sector.PlayerForce.Army.ChapterOperationalDoctrine;
        OperationalDoctrineView doctrine = application.QueryOperationalDoctrine();
        Assert.NotEmpty(doctrine.InjuryThresholdLabels);

        Assert.Contains("roster consequence", application.DescribeOperationalDoctrineConsequence(
            doctrine.InjuryThresholdIndex, true, 4));

        Guid staleToken = application.SessionToken;
        application.Install(CreateEmptySession(application.ActiveSession.Rules));
        Assert.False(application.SaveOperationalDoctrine(staleToken, 0, true, 9).Succeeded);
        Assert.NotEqual(9, live.MinimumDutyReadySquadStrength);
    }

    // ----- Training ----------------------------------------------------------------------

    [Fact]
    public void TheTenthCompanyScreenLocksWithoutARecruitmentProgram()
    {
        CampaignApplication application = CreateChapterCampaign(out _);

        RecruitmentScreenSnapshot snapshot =
            application.QueryRecruitmentScreen(null, selectedSquadId: null);

        Assert.False(snapshot.IsUnlocked);
        Assert.Contains("no Home World", snapshot.LockedMessage);
        Assert.Null(application.QueryDoctrineDraft());
        Assert.False(application.IsRecruitmentSetupComplete);
    }

    [Fact]
    public void ConfirmingRecruitmentDoctrineRefusesAReplacedSession()
    {
        CampaignApplication application = CreateChapterCampaign(out _);
        RecruitmentDoctrineDraft draft = new(
            OnlyWar.Domain.Recruitment.RecruitmentPolicy.VoluntaryPresentation,
            0, 0, 0, 0, 0, 0.5f);
        Guid staleToken = application.SessionToken;
        application.Install(CreateEmptySession(application.ActiveSession.Rules));

        TrainingCommandResult refused = application.ConfirmDoctrine(staleToken, draft);

        Assert.False(refused.Succeeded);
    }

    // ----- Navigation --------------------------------------------------------------------

    [Fact]
    public void NavigationResolvesToTheSurfaceTheTargetActuallyLivesOn()
    {
        CampaignApplication application = CreateFleetCampaign(
            out Ship source, out _, out _, out Squad squad);

        CampaignNavigationRoute fleet = application.ResolveNavigation(
            CampaignNavigationTargetKind.Fleet, source.Fleet.Id);
        Assert.Equal(CampaignNavigationRouteKind.Fleet, fleet.Kind);

        // A world that is not charted resolves to nothing rather than opening an empty screen.
        Assert.Equal(
            CampaignNavigationRouteKind.None,
            application.ResolveNavigation(
                CampaignNavigationTargetKind.Planet, -1).Kind);

        Assert.Equal(source.Fleet.Planet.Name,
            application.QueryPlanetName(source.Fleet.Planet.Id));
        Assert.Null(application.QueryPlanetName(-1));
        Assert.Null(application.QueryRegionPlanet(-1));
        // The squad in this fixture is aboard ship but not in the order of battle, so nothing
        // resolves for it; the point is that the scene no longer has to work that out.
        Assert.Equal(
            CampaignNavigationRouteKind.None,
            application.ResolveSquadLocation(squad.Id).Kind);
    }

    // ----- Scene-wide audit ---------------------------------------------------------------

    [Fact]
    public void NoCampaignScreenMentionsTheRetiredCampaignSingleton()
    {
        List<string> offenders = Directory
            .EnumerateFiles(
                Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Scenes"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("GameDataSingleton"))
            .Select(path => Path.GetRelativePath(
                Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Scenes"), path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoCampaignScreenNamesAMutationServiceOfItsOwn()
    {
        string[] mutators =
        [
            "SoldierTransferService",
            "MusterPlanService",
            "LoadoutDoctrineService",
            "CharacterLoadoutService",
            "EquipmentLoadoutService",
            "FleetTransferService",
            "RecruitmentPromotionService",
            "OrderForceService",
            "SubsectorBuilder",
            "VoronoiSubsectorMapper"
        ];

        List<string> offenders = [];
        foreach (string path in Directory.EnumerateFiles(
            Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Scenes"),
            "*.cs",
            SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            foreach (string mutator in mutators)
            {
                if (source.Contains(mutator)) offenders.Add($"{Path.GetFileName(path)}:{mutator}");
            }
        }

        Assert.Empty(offenders);
    }

    // ----- Fixtures ----------------------------------------------------------------------

    private static void AssertDetached(Type root, bool allowRulesTemplates = false)
    {
        HashSet<Type> visited = [];
        List<Type> forbidden = [];
        void Inspect(Type type)
        {
            if (!visited.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string)
                || type == typeof(Guid) || type == typeof(decimal)) return;
            if (type.IsArray) { Inspect(type.GetElementType()); return; }
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments()) Inspect(argument);
                return;
            }
            if (type.Assembly == typeof(Sector).Assembly)
            {
                // The loadout surfaces exchange authored rules templates, which are immutable
                // reference data rather than the campaign graph.
                if (allowRulesTemplates && IsRulesTemplate(type)) return;
                forbidden.Add(type);
                return;
            }
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Inspect(property.PropertyType);
            }
        }
        Inspect(root);
        Assert.Empty(forbidden);
    }

    private static bool IsRulesTemplate(Type type) =>
        type.Namespace == "OnlyWar.Domain.Equippables"
        || type.Namespace == "OnlyWar.Domain.Squads"
        || type.Namespace == "OnlyWar.Domain.Soldiers"
        || type.Namespace == "OnlyWar.Domain.Readiness"
        || type.Namespace == "OnlyWar.Domain.Recruitment"
        || type == typeof(ChapterOperationalDoctrine);

    private static CampaignApplication InstallFixture(SectorSimulationFixture fixture)
    {
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(31)).CreateApplication();
        application.Install(new GameSession(
            fixture.Rules,
            fixture.Sector,
            fixture.CurrentDate,
            new SeededRNG(32)));
        return application;
    }

    /// <summary>
    /// Two chapter task forces in orbit at one world plus a third at another, so transfer,
    /// divide, merge and the inspector's fallback all have something real to decide.
    /// </summary>
    private static CampaignApplication CreateFleetCampaign(
        out Ship source, out Ship destination, out Ship distant, out Squad squad)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Faction player = fixture.Sector.PlayerForce.Faction;
        Planet home = fixture.Planet;
        Planet away = new(2, "Distant World", new Coordinate(9, 9), 1, null, 1, 0);

        source = CreateShip(1, "Source", 40);
        destination = CreateShip(2, "Destination", 40);
        distant = CreateShip(3, "Distant", 40);
        TaskForce inOrbit = new(901, player, null, home, null, [source, destination]);
        TaskForce elsewhere = new(902, player, null, away, null, [distant]);

        Fleet fleet = new("Fleet", null, null);
        fleet.TaskForces.Add(inOrbit);
        fleet.TaskForces.Add(elsewhere);
        PlayerForce force = new(
            player,
            new Army("Army", null, "Commander", null, new List<PlayerSoldier>()),
            fleet);

        Unit company = new(50, "Company", new UnitTemplate(50, "Company", false, [], []), []);
        squad = new Squad(11, "Boarding Squad", company, TestModelFactory.SquadTemplate);
        squad.AddSquadMember(TestModelFactory.CreateSoldier(name: "Boarding Marine"));
        company.AddSquad(squad);
        source.LoadSquad(squad);
        squad.BoardedLocation = source;

        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(31)).CreateApplication();
        application.Install(new GameSession(
            fixture.Rules,
            new Sector(force, [], [home, away], [inOrbit, elsewhere]),
            fixture.CurrentDate,
            new SeededRNG(32)));
        return application;
    }

    /// <summary>A minimal chapter with one squad, for the roster, loadout and muster surfaces.</summary>
    private static CampaignApplication CreateChapterCampaign(out Squad squad)
    {
        SectorSimulationFixture fixture = SectorSimulationFixture.Create();
        Unit chapter = new("Chapter", new UnitTemplate(100, "Chapter", true, [], []));
        squad = new Squad("Command Squad", chapter, TestModelFactory.SquadTemplate);
        for (int index = 0; index < 3; index++)
        {
            squad.AddSquadMember(TestModelFactory.CreateSoldier(name: $"Brother {index}"));
        }
        chapter.AddSquad(squad);

        PlayerForce force = new(
            fixture.Sector.PlayerForce.Faction,
            new Army("Army", null, "Commander", chapter, new List<PlayerSoldier>()),
            new Fleet("Fleet", null, null));
        CampaignApplication application = TestPersonnelComposition.CreateCampaign(new SeededRNG(51)).CreateApplication();
        application.Install(new GameSession(
            fixture.Rules,
            new Sector(force, [], [fixture.Planet], []),
            fixture.CurrentDate,
            new SeededRNG(52)));
        return application;
    }

    private static GameSession CreateEmptySession(GameRulesData rules)
    {
        Unit chapter = new("Chapter", new UnitTemplate(101, "Chapter", true, [], []));
        PlayerForce force = new(null,
            new Army("Army", null, "Commander", chapter, new List<PlayerSoldier>()),
            new Fleet("Fleet", null, null));
        return new GameSession(
            rules, new Sector(force, [], [], []), new Date(20_000), new SeededRNG(53));
    }

    private static Ship CreateShip(int id, string name, ushort capacity) =>
        new(id, name, new ShipTemplate(id, name, capacity, 0, 0));
}
