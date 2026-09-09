using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Architecture;

/// <summary>
/// Keeps the retired pre-refactor namespace buckets from becoming a second public module.
/// Compatibility sources are explicit, repository-relative entries in the ownership manifest;
/// every other production source must use the namespace family of its owning module.
/// </summary>
public sealed class NamespaceOwnershipEnforcementTests
{
    private static readonly string[] ProductionRoots = ["Modules", "Host", "Scenes"];

    [Fact]
    public void ProductionSourcesDoNotAddDeprecatedBroadNamespaces()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        OwnershipManifest manifest = LoadManifest(repositoryRoot);
        string[] offenders = EnumerateProductionSources(repositoryRoot)
            .SelectMany(source => FindDeprecatedUsages(
                source.Relative,
                File.ReadAllText(source.Full),
                manifest.DeprecatedNamespaces))
            .Where(usage => !IsAllowedCompatibilityUsage(usage, manifest))
            .Select(usage => $"{usage.Relative}:{usage.Line}:{usage.Namespace}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CompatibilitySourcesAreExplicitAndPhysicallyIsolated()
    {
        string repositoryRoot = RulesDatabaseFixture.RepositoryRoot;
        OwnershipManifest manifest = LoadManifest(repositoryRoot);

        Assert.NotEmpty(manifest.CompatibilitySources);
        foreach (string relative in manifest.CompatibilityNamespaceAllowlist.Keys)
        {
            Assert.True(
                manifest.CompatibilitySources
                    .Select(NormalizeRelativePath)
                    .Contains(NormalizeRelativePath(relative), StringComparer.OrdinalIgnoreCase),
                $"Compatibility namespace allowlist entry is not a listed source: {relative}.");
        }

        foreach (string relative in manifest.CompatibilitySources)
        {
            string normalized = NormalizeRelativePath(relative);
            string full = Path.Combine(
                repositoryRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(full), $"Missing compatibility source {relative}.");
            Assert.Contains(
                "/Compatibility/",
                $"/{normalized.Trim('/')}/",
                StringComparison.OrdinalIgnoreCase);
        }

        string[] unlisted = EnumerateProductionSources(repositoryRoot)
            .SelectMany(source => FindDeprecatedUsages(
                source.Relative,
                File.ReadAllText(source.Full),
                manifest.DeprecatedNamespaces))
            .Select(usage => NormalizeRelativePath(usage.Relative))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(relative => !manifest.CompatibilitySources
                .Select(NormalizeRelativePath)
                .Contains(relative, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(unlisted);
    }

    [Fact]
    public void ScannerDetectsBothDeprecatedNamespaceDeclarationsAndImports()
    {
        const string source = "namespace OnlyWar.Models.Missions;\nusing Legacy = OnlyWar.Helpers.Extensions;";

        string[] findings = FindDeprecatedUsages(
                "Synthetic.cs",
                source,
                ["OnlyWar.Models", "OnlyWar.Helpers"])
            .Select(usage => usage.Namespace)
            .ToArray();

        Assert.Equal(["OnlyWar.Models.Missions", "OnlyWar.Helpers.Extensions"], findings);
    }

    private static OwnershipManifest LoadManifest(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "Modules", "source-ownership.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        return new OwnershipManifest(
            root.GetProperty("DeprecatedNamespaces")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray(),
            root.GetProperty("CompatibilitySources")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray(),
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                root.GetProperty("CompatibilityNamespaceAllowlist").GetRawText())
            ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<(string Full, string Relative)> EnumerateProductionSources(
        string repositoryRoot)
    {
        foreach (string directory in ProductionRoots)
        {
            string path = Path.Combine(repositoryRoot, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(file))
                {
                    continue;
                }

                yield return (file, NormalizeRelativePath(Path.GetRelativePath(repositoryRoot, file)));
            }
        }
    }

    private static IEnumerable<NamespaceUsage> FindDeprecatedUsages(
        string relative,
        string source,
        IReadOnlyCollection<string> deprecatedNamespaces)
    {
        string roots = string.Join(
            "|",
            deprecatedNamespaces
                .OrderByDescending(value => value.Length)
                .Select(Regex.Escape));
        Regex pattern = new(
            $@"^\s*(?:(?:global\s+)?using\s+(?:static\s+)?(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?|namespace\s+)(?<namespace>(?:{roots})(?:\.[A-Za-z_][A-Za-z0-9_]*)*)(?=\s|;|{{)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string trimmed = lines[index].TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            Match match = pattern.Match(lines[index]);
            if (match.Success)
            {
                yield return new NamespaceUsage(
                    NormalizeRelativePath(relative),
                    index + 1,
                    match.Groups["namespace"].Value);
            }
        }
    }

    private static bool IsAllowedCompatibilityUsage(
        NamespaceUsage usage,
        OwnershipManifest manifest)
    {
        string relative = NormalizeRelativePath(usage.Relative);
        return manifest.CompatibilityNamespaceAllowlist.TryGetValue(relative, out string[] allowed)
            && allowed.Contains(usage.Namespace, StringComparer.Ordinal);
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool IsBuildArtifact(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private sealed record OwnershipManifest(
        string[] DeprecatedNamespaces,
        string[] CompatibilitySources,
        Dictionary<string, string[]> CompatibilityNamespaceAllowlist);

    private sealed record NamespaceUsage(string Relative, int Line, string Namespace);
}
