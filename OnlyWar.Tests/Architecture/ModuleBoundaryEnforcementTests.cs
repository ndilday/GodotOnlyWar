using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Battles.Models;
using OnlyWar.Application;
using OnlyWar.Application.Abstractions;
using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;
using OnlyWar.Domain.Events;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Operations.Abstractions;
using Xunit;

namespace OnlyWar.Tests.Architecture;

/// <summary>
/// Executable architecture boundaries that the compiler and public type system do not express on
/// their own. These checks deliberately use project metadata and focused public-surface
/// inspection; they do not infer ownership from file names, scan production source text, or keep a
/// repository-wide source-ownership registry.
/// </summary>
public sealed class ModuleBoundaryEnforcementTests
{
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
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Shared query ports expose campaign aggregates: {string.Join(", ", offenders)}");
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
        Assert.Contains(
            nameof(ICampaignSimulationSession.Sector),
            typeof(ICampaignSimulationSession).GetProperties()
                .Select(property => property.Name));
        Assert.Contains(
            nameof(ICampaignSimulationSession.Rules),
            typeof(ICampaignSimulationSession).GetProperties()
                .Select(property => property.Name));
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
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Screen ports expose live campaign or replay types: {string.Join(", ", offenders)}");
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
            string[] expectedReferences = references.Order(StringComparer.Ordinal).ToArray();
            Assert.True(
                expectedReferences.SequenceEqual(actual[project]),
                $"{project} references [{string.Join(", ", actual[project])}], "
                + $"expected [{string.Join(", ", expectedReferences)}].");
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

        Assert.True(
            visited == actual.Count,
            $"Project reference graph contains a cycle involving: "
            + string.Join(", ", incoming
                .Where(pair => pair.Value > 0)
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void SqliteProviderIsOwnedByPersistenceProject()
    {
        string modulesRoot = Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Modules");
        string[] owners = Directory
            .EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .SelectMany(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Where(reference => string.Equals(
                    (string)reference.Attribute("Include"),
                    "Microsoft.Data.Sqlite",
                    StringComparison.Ordinal))
                .Select(_ => Path.GetFileNameWithoutExtension(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["OnlyWar.Persistence"], owners);
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

            string[] actual = ReadFriendAssemblies(projectFile);
            Assert.True(
                expected.SequenceEqual(actual),
                $"{project} grants friend access to [{string.Join(", ", actual)}], "
                + $"expected [{string.Join(", ", expected)}].");
        }

        Assert.Equal(
            ["OnlyWar.Tests"],
            FriendAssemblies(typeof(BattleReplaySummaryBuilder).Assembly));
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

    private static string[] FriendAssemblies(Assembly assembly) => assembly
        .GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
        .Select(friend => friend.AssemblyName)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static bool IsBuildArtifact(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

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
