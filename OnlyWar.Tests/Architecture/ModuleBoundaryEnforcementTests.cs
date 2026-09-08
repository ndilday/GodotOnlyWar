using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Application;
using OnlyWar.Application.Abstractions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
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
            Path.Combine("Modules", "OnlyWar.Runtime", "Naming", "NameGeneratorFacade.cs"),
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
            "using OnlyWar.Helpers.UI;",
            "using OnlyWar.Helpers.PlanetaryOperations;",
            "using OnlyWar.Helpers.Recruitment;",
            "using OnlyWar.Models.Command;",
            "using OnlyWar.Helpers.Turns;"
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
    public void ApplicationQueryContractsDoNotDeclareLegacyPresentationNamespaces()
    {
        string queriesRoot = Path.Combine(
            RulesDatabaseFixture.RepositoryRoot, "Modules", "OnlyWar.Application", "Queries");
        string[] legacyNamespaces =
        [
            "namespace OnlyWar.Helpers.UI",
            "namespace OnlyWar.Helpers.PlanetaryOperations",
            "namespace OnlyWar.Helpers"
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
            typeof(Unit)
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
                Path.Combine(repositoryRoot, "Modules"), "*.csproj", SearchOption.TopDirectoryOnly))
            .Append(Path.Combine(repositoryRoot, "OnlyWarGodot.csproj"));

        Assert.DoesNotContain(graphFiles,
            path => File.Exists(path)
                && File.ReadAllText(path).Contains("OnlyWar.Contracts", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossModuleContractsDoNotRemainInImplementationProjects()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        string[] implementationProjects =
            ["OnlyWar.Generation", "OnlyWar.Operations", "OnlyWar.Persistence", "OnlyWar.Runtime"];

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
