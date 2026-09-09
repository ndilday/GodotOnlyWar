using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Application;
using OnlyWar.Application.Abstractions;
using OnlyWar.Domain.Missions;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Events;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Architecture;

/// <summary>
/// SB-12's enforcement fixture. The assembly-reference matrix is checked in
/// <c>OnlyWar.HeadlessTests.HeadlessBoundaryTests</c>, which can only see what a project
/// references; these are the checks that need to see the sources: which files may reach for the
/// retired global campaign state, which may name the process-wide RNG, and where SQL is allowed to live.
///
/// Each allowlist below names exact files and says why. That is the point of the rule: an
/// allowlist that named a whole subsystem would not constrain anything. When a bridge is removed,
/// delete its line here too — a stale entry is not an error, so nothing else will notice.
/// </summary>
public class ModuleBoundaryEnforcementTests
{
    // Directories that hold shipping code. The test project itself is deliberately not scanned:
    // tests may name the static RNG for deterministic setup, but production sources are scanned
    // independently from the fixtures.
    private static readonly string[] ProductionRoots =
        ["Modules", "Scenes", "Host"];

    [Fact]
    public void LegacyRootProductionFoldersDoNotContainShippingSources()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        string[] legacyRoots = ["Helpers", "Models", "Builders", "Composition"];

        string[] offenders = legacyRoots
            .SelectMany(directory =>
            {
                string path = Path.Combine(repositoryRoot, directory);
                return Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                    : [];
            })
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheRetiredCampaignSingletonDoesNotAppearInProductionSources()
    {
        Assert.Empty(FindOffenders("GameDataSingleton", []));
    }

    [Fact]
    public void TheProcessWideRngIsConfinedToGenerationAndCompositionRoots()
    {
        // Sector generation is seeded globally on purpose (SectorBuilder calls RNG.Reset(seed)), so
        // the generation path and the name generator that runs inside it may name StaticRNG. Live
        // simulation may not: a policy that needs randomness takes the session's IRNG.
        string[] allowed =
        [
            Path.Combine("Modules", "OnlyWar.Runtime", "StaticRNG.cs"),
            Path.Combine("Modules", "OnlyWar.Runtime", "Compatibility", "NameGeneratorFacade.cs"),
            Path.Combine("Modules", "OnlyWar.Generation", "Chapter", "NewChapterBuilder.cs"),
            Path.Combine(
                "Modules", "OnlyWar.Application", "Helpers", "Application", "Adapters", "Generation",
                "GenerationSupportAdapters.cs"),
            // The host's composition roots, where the application is constructed.
            Path.Combine("Scenes", "StartMenu", "StartMenu.cs"),
            Path.Combine("Scenes", "StartMenu", "StartMenu.ReleaseControls.cs"),
            Path.Combine("Scenes", "Debug", "MainGamePreviewBootstrap.cs")
        ];

        Assert.Empty(FindOffenders("StaticRNG", allowed));
    }

    [Fact]
    public void ProductionSourcesDoNotDeclareMutableProcessWideIdentityCounters()
    {
        Regex mutableIdentityField = new(
            @"\bstatic\s+(?:volatile\s+)?(?:int|long|uint|ulong)\s+_[A-Za-z0-9]*(?:id|ID)[A-Za-z0-9]*\s*(?:=|;)",
            RegexOptions.Compiled);

        string[] offenders = EnumerateProductionSources()
            .Where(source => mutableIdentityField.IsMatch(CodeOf(source.Full)))
            .Select(source => source.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
        Assert.DoesNotContain(
            EnumerateProductionSources(),
            source => Path.GetFileName(source.Full).Contains("IdGenerator", StringComparison.Ordinal));
        Assert.Empty(FindOffenders("PlanetBuilder.Instance", []));
        Assert.Empty(FindOffenders("SoldierFactory.Instance", []));
        Assert.Empty(FindOffenders("RequestFactory.Instance", []));
    }

    [Fact]
    public void SqlLivesOnlyInPersistence()
    {
        // SB-00 puts SQL in Persistence alone. Campaign owns policy and projections; its load/save
        // callers receive domain data and raw rows from the Persistence adapter.
        List<string> offenders = EnumerateProductionSources()
            .Where(path => ContainsAny(CodeOf(path.Full),
                "using System.Data", "Microsoft.Data.Sqlite", "System.Data.SQLite"))
            .Select(path => path.Relative)
            .Where(relative =>
                !relative.StartsWith(Path.Combine("Modules", "OnlyWar.Persistence") + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GodotIsNameOnlyInTheHostProject()
    {
        List<string> offenders = EnumerateProductionSources()
            .Where(path => path.Relative.StartsWith("Modules" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Where(path => CodeOf(path.Full).Contains("using Godot", StringComparison.Ordinal))
            .Select(path => path.Relative)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ScenesUseApplicationNamespacesForApplicationOwnedProjectionContracts()
    {
        string scenesRoot = Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Scenes");
        string[] legacyProjectionNamespaces =
        [
            "using OnlyWar.Domain.UI;",
            "using OnlyWar.Operations.Planetary;",
            "using OnlyWar.Campaign.Recruitment;",
            "using OnlyWar.Domain.Command;",
            "using OnlyWar.Campaign.Turns;"
        ];

        string[] offenders = Directory
            .EnumerateFiles(scenesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => ContainsAny(CodeOf(path), legacyProjectionNamespaces))
            .Select(path => Path.GetRelativePath(
                RulesDatabaseFixture.RepositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GodotFacingSourcesDoNotReachThroughLiveCampaignOrBattleReplay()
    {
        string root = RulesDatabaseFixture.RepositoryRoot;
        string replayProjection = Path.Combine(
            "Host", "Presentation", "Battles", "BattleReplaySummaryBuilder.cs");
        string[] forbiddenHandles =
        [
            "ActiveSession",
            "BattleHistory",
            "BattleStateSnapshot",
            "BattleSquadSnapshot",
            "BattleSoldierSnapshot",
            "MissionDebriefLine",
            "MissionContext",
            "using OnlyWar.Battles.Models;",
            "using OnlyWar.Domain.Missions;",
            "using OnlyWar.Domain.Orders;",
            "using OnlyWar.Domain.Events;",
            "using OnlyWar.Battles.Actions;",
            "using OnlyWar.Battles.Resolutions;"
        ];

        IEnumerable<(string Full, string Relative)> godotFacingSources =
            Directory.EnumerateFiles(Path.Combine(root, "Scenes"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(
                    Path.Combine(root, "Host", "Presentation"), "*.cs", SearchOption.AllDirectories))
                .Select(path => (Full: path, Relative: Path.GetRelativePath(root, path)))
                .Where(source => !string.Equals(
                    source.Relative, replayProjection, StringComparison.Ordinal));

        string[] offenders = godotFacingSources
            .SelectMany(source => forbiddenHandles
                .Where(handle => handle == "MissionDebriefLine"
                    ? Regex.IsMatch(CodeOf(source.Full), @"\bMissionDebriefLine\b")
                    : CodeOf(source.Full).Contains(handle, StringComparison.Ordinal))
                .Select(handle => $"{source.Relative}: {handle}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GodotFacingModelAndHelperImportsAreConfinedToDocumentedExceptions()
    {
        string root = RulesDatabaseFixture.RepositoryRoot;
        string[] allowed =
        [
            Path.Combine("Host", "Presentation", "Battles", "BattleReplaySummaryBuilder.cs"),
            Path.Combine("Host", "Presentation", "UI", "SystemMenu", "SaveSlotViewModelMapper.cs"),
            Path.Combine("Scenes", "Debug", "MainGamePreviewBootstrap.cs"),
            Path.Combine("Scenes", "Debug", "ReleaseSceneWiringSmoke.cs"),
            Path.Combine("Scenes", "GodotLogBridge.cs"),
            Path.Combine("Scenes", "MainGameScreen", "MainGameScene.CampaignControls.cs"),
            Path.Combine("Scenes", "MainGameScreen", "MainGameScene.cs"),
            Path.Combine("Scenes", "SquadScreen", "ElementLoadoutEditorView.cs"),
            Path.Combine("Scenes", "SquadScreen", "EquipmentLoadoutEditorView.cs"),
            Path.Combine("Scenes", "SquadScreen", "LoadoutDoctrineDialog.cs"),
            Path.Combine("Scenes", "SquadScreen", "SquadScreenController.cs"),
            Path.Combine("Scenes", "SquadScreen", "SquadScreenView.cs"),
            Path.Combine("Scenes", "StartMenu", "StartMenu.ReleaseControls.cs"),
            Path.Combine("Scenes", "StartMenu", "StartMenu.cs")
        ];
        string[] moduleNamespaces =
        [
            "using OnlyWar.Domain",
            "using OnlyWar.Domain",
            "using OnlyWar.Battles",
            "using OnlyWar.Medical",
            "using OnlyWar.Operations"
        ];

        string[] offenders = EnumerateProductionSources()
            .Where(source => source.Relative.StartsWith(
                "Scenes" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || source.Relative.StartsWith(
                    "Host" + Path.DirectorySeparatorChar + "Presentation"
                        + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            .Where(source => ContainsAny(CodeOf(source.Full), moduleNamespaces))
            .Select(source => source.Relative)
            .Where(relative => !allowed.Contains(relative, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ViewProjectionBuildersAreInstalledOnlyAtCompositionRoots()
    {
        string root = RulesDatabaseFixture.RepositoryRoot;
        string mainGameScene = Path.Combine(
            "Scenes", "MainGameScreen", "MainGameScene.cs");
        string[] forbiddenBuilders =
        [
            "new SquadRowViewModelBuilder(",
            "new BattleReplaySummaryBuilder("
        ];

        string[] offenders = Directory
            .EnumerateFiles(Path.Combine(root, "Scenes"), "*.cs", SearchOption.AllDirectories)
            .Select(path => (Full: path, Relative: Path.GetRelativePath(root, path)))
            .Where(source => !string.Equals(
                source.Relative, mainGameScene, StringComparison.Ordinal))
            .SelectMany(source => forbiddenBuilders
                .Where(builder => CodeOf(source.Full).Contains(builder, StringComparison.Ordinal))
                .Select(builder => $"{source.Relative}: {builder}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CampaignConstructionAndReplayProjectionExceptionsAreExplicit()
    {
        string[] allowedCampaignComposition =
        [
            Path.Combine("Scenes", "StartMenu", "StartMenu.cs"),
            Path.Combine("Scenes", "StartMenu", "StartMenu.ReleaseControls.cs"),
            Path.Combine("Scenes", "Debug", "MainGamePreviewBootstrap.cs")
        ];
        string[] allowedReplayComposition =
        [Path.Combine("Scenes", "MainGameScreen", "MainGameScene.cs")];

        Assert.Empty(FindOffenders("new CampaignApplication(", allowedCampaignComposition));
        Assert.Empty(FindOffenders("new BattleReplaySummaryBuilder(", allowedReplayComposition));
    }

    [Fact]
    public void ApplicationQueryContractsDoNotDeclareLegacyPresentationNamespaces()
    {
        string queriesRoot = Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Modules", "OnlyWar.Application", "Queries");
        string[] legacyNamespaces =
        [
            "namespace OnlyWar.Domain.UI",
            "namespace OnlyWar.Operations.Planetary",
            "namespace OnlyWar.Domain"
        ];

        string[] offenders = Directory
            .EnumerateFiles(queriesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => ContainsAny(CodeOf(path), legacyNamespaces))
            .Select(path => Path.GetRelativePath(
                RulesDatabaseFixture.RepositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SharedQueryPortsDoNotExposeCampaignAggregates()
    {
        Type[] queryPorts =
        [
            typeof(IPersonnelAvailabilityQueries),
            typeof(PersonnelMovementRequest),
            typeof(PersonnelOrderAssignmentRequest),
            typeof(PersonnelFormationSnapshot),
            typeof(PersonnelCharacterSnapshot),
            typeof(EngagementInput),
            typeof(EngagementParticipant),
            typeof(EngagementLocation),
            typeof(EngagementFactionFacts),
            typeof(IReadinessDecisions)
        ];

        Type[] forbidden =
        [
            typeof(PlayerSoldier),
            typeof(Squad),
            typeof(Order),
            typeof(Region),
            typeof(CampaignLocation)
        ];

        string[] offenders = PublicSurface(queryPorts)
            .Where(type => forbidden.Contains(type))
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CommonCampaignSessionSeamDoesNotExposeSimulationAggregate()
    {
        string[] commonProperties = typeof(ICampaignSession)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Identity"], commonProperties);
        Assert.Contains(nameof(ICampaignSimulationSession.Sector),
            typeof(ICampaignSimulationSession).GetProperties()
                .Select(property => property.Name));
        Assert.Contains(nameof(ICampaignSimulationSession.Rules),
            typeof(ICampaignSimulationSession).GetProperties()
                .Select(property => property.Name));
    }

    [Fact]
    public void OperationsReadServiceDoesNotTraverseTheActiveSessionGraph()
    {
        string path = Path.Combine(RulesDatabaseFixture.RepositoryRoot,
            "Modules", "OnlyWar.Application", "Queries", "OperationsScreenQueries.cs");
        string source = CodeOf(path);

        Assert.DoesNotContain("ActiveSession.Sector", source);
        Assert.DoesNotContain("ActiveSession.Rules", source);
        Assert.DoesNotContain("ActiveSession.CurrentDate", source);
        Assert.DoesNotContain("ActiveSession.Random", source);
    }

    [Fact]
    public void FeatureScreenServicesUseFeatureContextsInsteadOfTheCampaignGraph()
    {
        string root = RulesDatabaseFixture.RepositoryRoot;
        string[] screenFiles =
        [
            Path.Combine(root, "Modules", "OnlyWar.Application", "CampaignNavigationApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "ChapterScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "CommandScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "DiplomacyScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "LoadoutScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "MainScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "MusterScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "OperationsScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "SectorMapApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "SessionControlApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "SystemInspectorApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "MedicalScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "FleetScreenApplication.cs"),
            Path.Combine(root, "Modules", "OnlyWar.Application", "TrainingScreenApplication.cs")
        ];
        string[] forbiddenHandles =
        [
            "ActiveSession",
            "CampaignServices",
            "GameSession",
            "GameRulesData",
            "Sector",
            "StaticRNG"
        ];

        foreach (string path in screenFiles)
        {
            string source = CodeOf(path);
            foreach (string handle in forbiddenHandles)
            {
                Assert.DoesNotMatch(
                    new Regex($@"\b{Regex.Escape(handle)}\b", RegexOptions.Compiled),
                    source);
            }
        }
    }

    [Fact]
    public void CampaignTurnProcessorsTraverseTheTurnContextInsteadOfTheSession()
    {
        string root = RulesDatabaseFixture.RepositoryRoot;
        string[] roots =
        [
            Path.Combine(root, "Modules", "OnlyWar.Campaign", "Helpers", "Turns"),
            Path.Combine(root, "Modules", "OnlyWar.Campaign", "Helpers", "TurnController.cs")
        ];
        Regex directSessionTraversal = new(
            @"\b(?:_session|session)\s*\.\s*(?:Sector|Rules|CurrentDate|Random|Identity)\b",
            RegexOptions.Compiled);

        IEnumerable<string> sources = roots
            .SelectMany(path => Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                : File.Exists(path) ? [path] : []);
        string[] offenders = sources
            .Where(path => !string.Equals(
                Path.GetFileName(path), "CampaignTurnContext.cs", StringComparison.Ordinal))
            .Where(path => directSessionTraversal.IsMatch(CodeOf(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ScreenPortsReturnDetachedContractGraphs()
    {
        Type[] screenPorts =
        [
            typeof(ICommandScreenApplication),
            typeof(IDiplomacyScreenApplication),
            typeof(ICampaignNavigationApplication),
            typeof(IMainScreenApplication),
            typeof(ISectorMapApplication),
            typeof(ISessionControlApplication),
            typeof(ISystemInspectorApplication),
            typeof(IChapterScreenApplication),
            typeof(IMusterScreenApplication),
            typeof(ILoadoutScreenApplication),
            typeof(IOperationsScreenApplication),
            typeof(IMedicalScreenApplication),
            typeof(IFleetScreenApplication),
            typeof(ITrainingScreenApplication)
        ];
        Type[] forbidden =
        [
            typeof(GameSession),
            typeof(Sector),
            typeof(Planet),
            typeof(Region),
            typeof(Squad),
            typeof(Order),
            typeof(PlayerSoldier),
            typeof(TaskForce),
            typeof(Ship),
            typeof(Faction),
            typeof(PlayerForce),
            typeof(Unit),
            typeof(BattleHistory),
            typeof(BattleStateSnapshot),
            typeof(BattleSquadSnapshot),
            typeof(BattleSoldierSnapshot),
            typeof(MissionDebriefLine),
            typeof(BattleDebriefReport),
            typeof(CampaignEventImportance),
            typeof(RequestSeverity),
            typeof(Aggression),
            typeof(MedicalProcedureType),
            typeof(WoundLevel)
        ];

        string[] offenders = ContractSurface(screenPorts)
            .Where(forbidden.Contains)
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AggregateBackedProjectionImplementationsAreNotPublicPorts()
    {
        Type[] implementationTypes =
        [
            typeof(OperationsScreenQueries),
            typeof(FleetScreenProjector),
            typeof(DiplomacyScreenProjector),
            typeof(PlanetaryForceTreeBuilder),
            typeof(ForceTreeSquad),
            typeof(ForceTreeInputs),
            typeof(RegionControlPresentation),
            typeof(FactionActivityPresentation),
            typeof(RegionTerrainPresentation)
        ];

        Assert.All(implementationTypes, type => Assert.False(
            type.IsPublic,
            $"{type.FullName} is an aggregate-backed implementation detail, not a screen port."));
    }

    [Fact]
    public void FeatureContextsKeepAggregateAndRulesHandlesPrivate()
    {
        Type[] contexts =
        [
            typeof(OperationsReadContext),
            typeof(OperationsCommandContext),
            typeof(MedicalReadContext),
            typeof(MedicalCommandContext),
            typeof(FleetCommandContext),
            typeof(FleetScreenContext),
            typeof(TrainingContext),
            typeof(TrainingScreenContext),
            typeof(CommandScreenContext),
            typeof(DiplomacyScreenContext),
            typeof(CampaignNavigationContext),
            typeof(MainScreenContext),
            typeof(SectorMapContext),
            typeof(SessionControlContext),
            typeof(SystemInspectorContext),
            typeof(ChapterScreenContext),
            typeof(MusterScreenContext),
            typeof(LoadoutScreenContext)
        ];
        Type[] forbidden = [typeof(Sector), typeof(GameRulesData)];

        foreach (Type context in contexts)
        {
            IEnumerable<Type> exposed = context
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(property => property.PropertyType)
                .SelectMany(UnwrapTypes);
            Assert.DoesNotContain(exposed, forbidden.Contains);
        }
    }

    [Fact]
    public void PublicCampaignFacadeDoesNotPublishLiveSessionOrServiceGraph()
    {
        Assert.Null(typeof(CampaignApplication).GetProperty(
            nameof(CampaignApplication.ActiveSession),
            BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(CampaignApplication).GetProperty(
            nameof(CampaignApplication.Services),
            BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(CampaignApplication).GetProperty(
            nameof(CampaignApplication.Storage),
            BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(CampaignApplication).GetProperty(
            nameof(CampaignApplication.SaveManager),
            BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void RetiredContractsHubIsAbsentFromTheCurrentProjectGraph()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        Assert.False(File.Exists(Path.Combine(
            repositoryRoot, "Modules", "OnlyWar.Contracts", "OnlyWar.Contracts.csproj")));

        IEnumerable<string> graphFiles =
            new[] { Path.Combine(repositoryRoot, "OnlyWarGodot.sln") }
            .Concat(Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "Modules"), "*.csproj", SearchOption.AllDirectories))
            .Append(Path.Combine(repositoryRoot, "OnlyWarGodot.csproj"));

        Assert.DoesNotContain(graphFiles,
            path => File.Exists(path)
                && File.ReadAllText(path).Contains("OnlyWar.Contracts", StringComparison.Ordinal));

        Assert.DoesNotContain(
            EnumerateProductionSources(),
            source => CodeOf(source.Full).Contains("namespace OnlyWar.Contracts", StringComparison.Ordinal)
                || CodeOf(source.Full).Contains("using OnlyWar.Contracts", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossModuleContractsDoNotRemainInImplementationProjects()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        string modulesRoot = Path.Combine(repositoryRoot, "Modules");
        string[] implementationProjects = Directory
            .EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(project => !project.EndsWith(".Abstractions", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] contractFiles = implementationProjects
            .SelectMany(project =>
            {
                string contractsPath = Path.Combine(repositoryRoot, "Modules", project, "Contracts");
                return Directory.Exists(contractsPath)
                    ? Directory.EnumerateFiles(contractsPath, "*.cs", SearchOption.AllDirectories)
                    : [];
            })
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(contractFiles);
    }

    [Fact]
    public void ModuleProjectReferencesMatchTheApprovedAcyclicMatrix()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        string modulesRoot = Path.Combine(repositoryRoot, "Modules");
        Dictionary<string, string[]> expected = ApprovedModuleReferences();
        string[] projectFiles = Directory
            .EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .ToArray();

        Dictionary<string, string[]> actual = projectFiles.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path),
            ReadProjectReferenceNames,
            StringComparer.Ordinal);

        Assert.Equal(
            expected.Keys.Order(StringComparer.Ordinal),
            actual.Keys.Order(StringComparer.Ordinal));

        foreach ((string project, string[] references) in expected)
        {
            Assert.True(actual.ContainsKey(project), $"Missing project {project}.");
            Assert.Equal(
                references.Order(StringComparer.Ordinal),
                actual[project]);
        }

        Dictionary<string, int> incoming = actual.Keys
            .ToDictionary(project => project, _ => 0, StringComparer.Ordinal);
        foreach (string reference in actual.Values.SelectMany(references => references))
        {
            Assert.True(incoming.ContainsKey(reference), $"Unknown project reference {reference}.");
            incoming[reference]++;
        }

        Queue<string> ready = new(incoming
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal));
        int visited = 0;
        while (ready.Count > 0)
        {
            string project = ready.Dequeue();
            visited++;
            foreach (string reference in actual[project])
            {
                incoming[reference]--;
                if (incoming[reference] == 0) ready.Enqueue(reference);
            }
        }

        Assert.Equal(actual.Count, visited);
    }

    [Fact]
    public void GodotCompositionRootReferencesTheApprovedModuleSet()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        string[] expected =
        [
            "OnlyWar.Abstractions",
            "OnlyWar.Application.Abstractions",
            "OnlyWar.Battles.Abstractions",
            "OnlyWar.Campaign",
            "OnlyWar.Domain",
            "OnlyWar.Generation.Abstractions",
            "OnlyWar.Medical",
            "OnlyWar.Medical.Abstractions",
            "OnlyWar.Operations",
            "OnlyWar.Operations.Abstractions",
            "OnlyWar.Persistence",
            "OnlyWar.Persistence.Abstractions",
            "OnlyWar.Runtime",
            "OnlyWar.Runtime.Abstractions",
            "OnlyWar.Application",
            "OnlyWar.Battles"
        ];

        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            ReadProjectReferenceNames(Path.Combine(repositoryRoot, "OnlyWarGodot.csproj")));
    }

    [Fact]
    public void ContractPlacementHasOneExplicitCompositionRootAllowlist()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        string modulesRoot = Path.Combine(repositoryRoot, "Modules");
        string[] compositionRootContracts =
        [
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "ChapterScreenContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "FleetScreenContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "LoadoutScreenContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "MainScreenContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "MedicalScreenContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "MusterScreenContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "NavigationContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "OperationsScreenContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "SectorMapContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "SessionControlContracts.cs"),
            Path.Combine("Modules", "OnlyWar.Application", "Queries", "SystemInspectorContracts.cs")
        ];

        string[] namedContractSources = Directory
            .EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .Where(path => Path.GetFileName(path).EndsWith(
                "Contracts.cs", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] offenders = namedContractSources
            .Where(relative => !IsAbstractionSource(relative)
                && !compositionRootContracts.Contains(relative, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
        Assert.All(compositionRootContracts, relative => Assert.True(
            File.Exists(Path.Combine(repositoryRoot, relative)),
            $"Composition-root contract is missing: {relative}"));
    }

    [Fact]
    public void OperationsOutcomeContractsLiveInTheOperationsAbstractionAssembly()
    {
        Type[] contracts =
        [
            typeof(MissionDebriefLine),
            typeof(AmbushSpoilStage),
            typeof(MissionOutcomeClassification),
            typeof(MissionForceDisposition)
        ];

        Assert.All(contracts, contract =>
        {
            Assert.Equal("OnlyWar.Operations.Abstractions", contract.Assembly.GetName().Name);
            Assert.Same(contract, typeof(MissionContext).Assembly.GetType(contract.FullName));
        });
    }

    [Fact]
    public void OperationsReadinessDiagnosticsRemainImplementationPrivate()
    {
        Assert.False(typeof(MissionAvailabilityStatus).IsPublic);
        Assert.False(typeof(MissionSquadReadinessIssue).IsPublic);
        Assert.Null(typeof(MissionContext).GetProperty(
            nameof(MissionContext.AvailabilityStatus),
            BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(MissionContext).GetProperty(
            nameof(MissionContext.ReadinessIssues),
            BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void ProductionFriendAssembliesMatchTheIntentionalAllowlist()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        string[] projectFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .ToArray();

        foreach (string projectFile in projectFiles)
        {
            string project = Path.GetFileNameWithoutExtension(projectFile);
            string[] expected = project switch
            {
                "OnlyWar.Application" or
                "OnlyWar.Battles" or
                "OnlyWar.Campaign" or
                "OnlyWar.Domain" or
                "OnlyWar.Generation" or
                "OnlyWar.Operations" or
                "OnlyWar.Persistence" => ["OnlyWar.Tests"],
                "OnlyWar.Medical" or
                "OnlyWar.Runtime" => [],
                _ when project.EndsWith(".Abstractions", StringComparison.Ordinal) => [],
                _ => throw new InvalidOperationException($"No friend-assembly policy exists for {project}.")
            };

            Assert.Equal(expected, ReadFriendAssemblies(projectFile));
        }

        string assemblyInfo = Path.Combine(repositoryRoot, "Properties", "AssemblyInfo.cs");
        string[] rootFriends = Regex.Matches(
                File.ReadAllText(assemblyInfo),
                @"InternalsVisibleTo\(\s*""([^""]+)""\s*\)")
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["OnlyWar.Tests"], rootFriends);

        string[] sourceDeclarations = EnumerateProductionSources()
            .Where(source => CodeOf(source.Full).Contains("InternalsVisibleTo", StringComparison.Ordinal))
            .Select(source => source.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(sourceDeclarations);
    }

    // The negative half of the fixture: a check that cannot fail proves nothing, so run the same
    // scanner over a synthetic tree that does violate the rule and require it to say so.
    [Fact]
    public void TheScannerActuallyReportsAViolationAndHonorsItsAllowlist()
    {
        string root = Path.Combine(Path.GetTempPath(), $"onlywar-sb12-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Scenes", "FakeScreen"));
            string offender = Path.Combine("Scenes", "FakeScreen", "Bypass.cs");
            string permitted = Path.Combine("Scenes", "FakeScreen", "Composition.cs");
            File.WriteAllText(Path.Combine(root, offender),
                "var sector = GameDataSingleton.Instance.Sector;");
            File.WriteAllText(Path.Combine(root, permitted),
                "var sector = GameDataSingleton.Instance.Sector;");

            Assert.Equal(
                [offender, permitted],
                Scan(root, "GameDataSingleton", []).Order(StringComparer.Ordinal));
            Assert.Equal([offender], Scan(root, "GameDataSingleton", [permitted]));
            Assert.Empty(Scan(root, "GameDataSingleton", [offender, permitted]));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Dictionary<string, string[]> ApprovedModuleReferences() => new(StringComparer.Ordinal)
    {
        ["OnlyWar.Abstractions"] = [],
        ["OnlyWar.Application.Abstractions"] = ["OnlyWar.Abstractions", "OnlyWar.Domain"],
        ["OnlyWar.Battles.Abstractions"] = ["OnlyWar.Domain"],
        ["OnlyWar.Generation.Abstractions"] = ["OnlyWar.Domain", "OnlyWar.Abstractions"],
        ["OnlyWar.Medical.Abstractions"] = ["OnlyWar.Domain"],
        ["OnlyWar.Operations.Abstractions"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Battles.Abstractions",
             "OnlyWar.Medical.Abstractions"],
        ["OnlyWar.Persistence.Abstractions"] = [],
        ["OnlyWar.Runtime.Abstractions"] = ["OnlyWar.Domain", "OnlyWar.Abstractions"],
        ["OnlyWar.Domain"] = ["OnlyWar.Abstractions"],
        ["OnlyWar.Application"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Application.Abstractions",
             "OnlyWar.Generation.Abstractions", "OnlyWar.Operations.Abstractions",
             "OnlyWar.Persistence.Abstractions", "OnlyWar.Runtime.Abstractions",
             "OnlyWar.Battles.Abstractions", "OnlyWar.Medical.Abstractions", "OnlyWar.Runtime",
             "OnlyWar.Campaign", "OnlyWar.Battles", "OnlyWar.Medical", "OnlyWar.Operations",
             "OnlyWar.Persistence", "OnlyWar.Generation"],
        ["OnlyWar.Battles"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Battles.Abstractions", "OnlyWar.Runtime"],
        ["OnlyWar.Campaign"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Application.Abstractions",
             "OnlyWar.Generation.Abstractions", "OnlyWar.Operations.Abstractions",
             "OnlyWar.Battles.Abstractions", "OnlyWar.Medical.Abstractions", "OnlyWar.Runtime",
             "OnlyWar.Battles", "OnlyWar.Medical", "OnlyWar.Operations", "OnlyWar.Generation"],
        ["OnlyWar.Generation"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Generation.Abstractions", "OnlyWar.Runtime"],
        ["OnlyWar.Medical"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Medical.Abstractions"],
        ["OnlyWar.Operations"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Operations.Abstractions",
             "OnlyWar.Battles.Abstractions", "OnlyWar.Medical.Abstractions", "OnlyWar.Runtime"],
        ["OnlyWar.Persistence"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Persistence.Abstractions"],
        ["OnlyWar.Runtime"] =
            ["OnlyWar.Domain", "OnlyWar.Abstractions", "OnlyWar.Runtime.Abstractions"]
    };

    private static string[] ReadProjectReferenceNames(string projectFile)
    {
        string projectDirectory = Path.GetDirectoryName(projectFile);
        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference =>
            {
                string include = (string)reference.Attribute("Include");
                Assert.False(string.IsNullOrWhiteSpace(include), $"Missing Include in {projectFile}.");
                string referencedProject = Path.GetFullPath(Path.Combine(projectDirectory, include));
                Assert.True(File.Exists(referencedProject), $"Missing project reference {referencedProject}.");
                return Path.GetFileNameWithoutExtension(referencedProject);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ReadFriendAssemblies(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants("InternalsVisibleTo")
            .Select(friend => (string)friend.Attribute("Include"))
            .Where(friend => !string.IsNullOrWhiteSpace(friend))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsBuildArtifact(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private static bool IsAbstractionSource(string repositoryRelativePath)
    {
        string[] segments = repositoryRelativePath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return segments.Length > 1
            && string.Equals(segments[0], "Modules", StringComparison.Ordinal)
            && segments[1].EndsWith(".Abstractions", StringComparison.Ordinal);
    }

    // ----- Scanner -------------------------------------------------------------------------

    private static List<string> FindOffenders(string needle, string[] allowed) =>
        Scan(RulesDatabaseFixture.RepositoryRoot, needle, allowed);

    private static List<string> Scan(string root, string needle, IReadOnlyList<string> allowed) =>
        EnumerateSources(root)
            .Where(source => CodeOf(source.Full).Contains(needle, StringComparison.Ordinal))
            .Select(source => source.Relative)
            .Where(relative => !allowed.Contains(relative, StringComparer.Ordinal))
            .ToList();

    /// <summary>
    /// The file with its comment lines dropped. Naming a bridge in prose — to say a type does not
    /// use it, or to point a reader at where it is installed — is documentation, not access, and a
    /// rule that forbade the words would only teach people to stop writing the explanation down.
    /// </summary>
    private static string CodeOf(string path) =>
        string.Join('\n', File.ReadLines(path).Where(line =>
        {
            string trimmed = line.TrimStart();
            return !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal)
                && !trimmed.StartsWith("/*", StringComparison.Ordinal);
        }));

    private static IEnumerable<(string Full, string Relative)> EnumerateProductionSources() =>
        EnumerateSources(RulesDatabaseFixture.RepositoryRoot);

    private static IEnumerable<(string Full, string Relative)> EnumerateSources(string root)
    {
        foreach (string directory in ProductionRoots)
        {
            string path = Path.Combine(root, directory);
            if (!Directory.Exists(path)) continue;
            foreach (string file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file);
                // Build output under Modules/<assembly>/obj holds generated copies of the sources.
                if (relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal)
                    || relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                yield return (file, relative);
            }
        }
    }

    private static bool ContainsAny(string source, params string[] needles) =>
        needles.Any(needle => source.Contains(needle, StringComparison.Ordinal));

    private static IEnumerable<Type> PublicSurface(IEnumerable<Type> roots) =>
        roots.SelectMany(type => new[] { type }
            .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(constructor => constructor.GetParameters()
                    .Select(parameter => parameter.ParameterType)))
            .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(method => new[] { method.ReturnType }
                    .Concat(method.GetParameters().Select(parameter => parameter.ParameterType))))
            .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(property => property.PropertyType)))
        .SelectMany(UnwrapTypes)
        .Distinct();

    private static IEnumerable<Type> ContractSurface(IEnumerable<Type> roots)
    {
        Queue<Type> pending = new(roots);
        HashSet<Type> visited = [];
        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();
            foreach (Type nested in UnwrapTypes(type))
            {
                if (!visited.Add(nested)) continue;
                yield return nested;
                if (nested.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                foreach (ConstructorInfo constructor in nested.GetConstructors(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    foreach (ParameterInfo parameter in constructor.GetParameters())
                    {
                        pending.Enqueue(parameter.ParameterType);
                    }
                }
                foreach (MethodInfo method in nested.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    pending.Enqueue(method.ReturnType);
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        pending.Enqueue(parameter.ParameterType);
                    }
                }
                foreach (PropertyInfo property in nested.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    pending.Enqueue(property.PropertyType);
                }
                foreach (FieldInfo field in nested.GetFields(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    pending.Enqueue(field.FieldType);
                }
            }
        }
    }

    private static IEnumerable<Type> UnwrapTypes(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            foreach (Type nested in UnwrapTypes(type.GetElementType())) yield return nested;
            yield break;
        }
        if (type.IsGenericType)
        {
            yield return type.GetGenericTypeDefinition();
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type nested in UnwrapTypes(argument)) yield return nested;
            }
            yield break;
        }
        yield return type;
    }
}
