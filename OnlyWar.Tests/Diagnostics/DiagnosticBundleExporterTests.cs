using OnlyWar.Helpers.Diagnostics;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace OnlyWar.Tests.Diagnostics
{
    public sealed class DiagnosticBundleExporterTests
    {
        [Fact]
        public void Export_IncludesManifestSettingsAndLogsWithoutInvokingUnapprovedSnapshot()
        {
            using TemporaryDirectory temporary = new();
            string settings = temporary.Write("end_turn_warnings.json", "{\"enabled\":true}");
            string log = temporary.Write("game-trace.log", "recent log line");
            bool snapshotInvoked = false;

            DiagnosticExportResult result = new DiagnosticBundleExporter().Export(new DiagnosticExportRequest
            {
                DestinationPath = Path.Combine(temporary.Path, "bundle"),
                BuildVersion = "Alpha 0.7.1-test",
                SettingsFiles = new[] { settings },
                LogFiles = new[] { log },
                IncludeCurrentCampaign = false,
                CurrentCampaignSnapshotFactory = () =>
                {
                    snapshotInvoked = true;
                    return new DiagnosticAttachment("current.s3db", new byte[] { 1 });
                }
            });

            Assert.True(result.Successful, result.ErrorMessage);
            Assert.False(snapshotInvoked);
            Assert.EndsWith(".zip", result.DestinationPath, StringComparison.OrdinalIgnoreCase);
            using ZipArchive archive = ZipFile.OpenRead(result.DestinationPath);
            Assert.NotNull(archive.GetEntry("manifest.txt"));
            Assert.NotNull(archive.GetEntry("settings/end_turn_warnings.json"));
            Assert.NotNull(archive.GetEntry("logs/game-trace.log"));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("campaign/", StringComparison.Ordinal));
        }

        [Fact]
        public void Export_IncludesFreshCampaignOnlyWithExplicitConsent()
        {
            using TemporaryDirectory temporary = new();
            byte[] snapshot = { 83, 81, 76, 105, 116, 101 };

            DiagnosticExportResult result = new DiagnosticBundleExporter().Export(new DiagnosticExportRequest
            {
                DestinationPath = Path.Combine(temporary.Path, "bundle.zip"),
                BuildVersion = "test",
                IncludeCurrentCampaign = true,
                CurrentCampaignSnapshotFactory = () => new DiagnosticAttachment("fresh.s3db", snapshot)
            });

            Assert.True(result.Successful, result.ErrorMessage);
            using ZipArchive archive = ZipFile.OpenRead(result.DestinationPath);
            ZipArchiveEntry entry = archive.GetEntry("campaign/fresh.s3db");
            Assert.NotNull(entry);
            using MemoryStream copied = new();
            using (Stream source = entry.Open())
            {
                source.CopyTo(copied);
            }
            Assert.Equal(snapshot, copied.ToArray());
        }

        [Fact]
        public void Export_FailureDoesNotReplaceAnExistingBundle()
        {
            using TemporaryDirectory temporary = new();
            string destination = temporary.Write("bundle.zip", "known-good-bundle");

            DiagnosticExportResult result = new DiagnosticBundleExporter().Export(new DiagnosticExportRequest
            {
                DestinationPath = destination,
                IncludeCurrentCampaign = true,
                CurrentCampaignSnapshotFactory = null
            });

            Assert.False(result.Successful);
            Assert.Equal("known-good-bundle", File.ReadAllText(destination));
            Assert.Empty(Directory.EnumerateFiles(temporary.Path, "*.tmp-*"));
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "onlywar-diagnostics-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string Write(string name, string content)
            {
                string path = System.IO.Path.Combine(Path, name);
                File.WriteAllText(path, content, Encoding.UTF8);
                return path;
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
