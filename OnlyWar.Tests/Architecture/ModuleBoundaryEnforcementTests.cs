using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Operations.Contracts;
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
