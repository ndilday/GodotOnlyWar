using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OnlyWar.Helpers.Diagnostics
{
    public sealed record DiagnosticAttachment(string FileName, byte[] Content);

    public sealed class DiagnosticExportRequest
    {
        public string DestinationPath { get; init; }
        public string BuildVersion { get; init; }
        public IEnumerable<string> SettingsFiles { get; init; } = Array.Empty<string>();
        public IEnumerable<string> LogFiles { get; init; } = Array.Empty<string>();
        public bool IncludeCurrentCampaign { get; init; }

        /// <summary>
        /// Integration supplies a fresh snapshot. The exporter only invokes this delegate when
        /// IncludeCurrentCampaign is true, keeping consent enforcement at the bundle boundary.
        /// </summary>
        public Func<DiagnosticAttachment> CurrentCampaignSnapshotFactory { get; init; }
    }

    public sealed record DiagnosticExportResult(
        bool Successful,
        string DestinationPath,
        string ErrorMessage,
        IReadOnlyList<string> IncludedFiles);

    public sealed class DiagnosticBundleExporter
    {
        public DiagnosticExportResult Export(DiagnosticExportRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string destination = NormalizeDestination(request.DestinationPath);
            if (string.IsNullOrWhiteSpace(destination))
            {
                return Failed(destination, "Choose a destination for the diagnostic bundle.");
            }

            string directory = Path.GetDirectoryName(destination);
            string temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            List<string> included = new();

            try
            {
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (FileStream output = new(temporaryPath, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None))
                using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
                {
                    AddText(archive, "manifest.txt", BuildManifest(request), included);
                    AddExistingFiles(archive, request.SettingsFiles, "settings", included);
                    AddExistingFiles(archive, request.LogFiles, "logs", included);

                    if (request.IncludeCurrentCampaign)
                    {
                        if (request.CurrentCampaignSnapshotFactory == null)
                        {
                            throw new InvalidOperationException("Current campaign inclusion was requested, but no snapshot provider is available.");
                        }

                        DiagnosticAttachment snapshot = request.CurrentCampaignSnapshotFactory();
                        if (snapshot?.Content == null)
                        {
                            throw new InvalidOperationException("The current campaign snapshot could not be created.");
                        }

                        string snapshotName = MakeSafeFileName(snapshot.FileName, "current-campaign.s3db");
                        AddBytes(archive, "campaign/" + snapshotName, snapshot.Content, included);
                    }
                }

                File.Move(temporaryPath, destination, overwrite: true);
                return new DiagnosticExportResult(true, destination, null, included.AsReadOnly());
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException
                or NotSupportedException or ArgumentException)
            {
                TryDelete(temporaryPath);
                return Failed(destination, exception.Message);
            }
        }

        public static IReadOnlyList<string> DiscoverRecentLogs(string logDirectory, int maximumFiles = 3)
        {
            if (maximumFiles <= 0 || string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(maximumFiles)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static void AddExistingFiles(
            ZipArchive archive,
            IEnumerable<string> paths,
            string folder,
            ICollection<string> included)
        {
            if (paths == null)
            {
                return;
            }

            HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
            {
                string fileName = MakeSafeFileName(Path.GetFileName(path), "diagnostic-file.txt");
                fileName = MakeUnique(fileName, usedNames);
                string entryName = folder + "/" + fileName;
                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                using Stream target = entry.Open();
                using FileStream source = new(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                source.CopyTo(target);
                included.Add(entryName);
            }
        }

        private static string BuildManifest(DiagnosticExportRequest request)
        {
            StringBuilder manifest = new();
            manifest.AppendLine("OnlyWar diagnostic bundle");
            manifest.Append("Build: ").AppendLine(string.IsNullOrWhiteSpace(request.BuildVersion) ? "unknown" : request.BuildVersion);
            manifest.Append("Exported UTC: ").AppendLine(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            manifest.Append("Current campaign included: ").AppendLine(request.IncludeCurrentCampaign ? "yes" : "no");
            return manifest.ToString();
        }

        private static void AddText(ZipArchive archive, string entryName, string content, ICollection<string> included)
        {
            AddBytes(archive, entryName, Encoding.UTF8.GetBytes(content), included);
        }

        private static void AddBytes(ZipArchive archive, string entryName, byte[] content, ICollection<string> included)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            using Stream target = entry.Open();
            target.Write(content, 0, content.Length);
            included.Add(entryName);
        }

        private static string NormalizeDestination(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string trimmed = path.Trim();
            return string.Equals(Path.GetExtension(trimmed), ".zip", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + ".zip";
        }

        private static string MakeSafeFileName(string fileName, string fallback)
        {
            string candidate = string.IsNullOrWhiteSpace(fileName) ? fallback : Path.GetFileName(fileName);
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(invalid, '_');
            }
            return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
        }

        private static string MakeUnique(string fileName, ISet<string> usedNames)
        {
            if (usedNames.Add(fileName))
            {
                return fileName;
            }

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int suffix = 2;
            string candidate;
            do
            {
                candidate = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture) + extension;
                suffix++;
            }
            while (!usedNames.Add(candidate));
            return candidate;
        }

        private static DiagnosticExportResult Failed(string destination, string message)
        {
            return new DiagnosticExportResult(false, destination, message, Array.Empty<string>());
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Preserve the original export error. A uniquely named partial file is harmless.
            }
        }
    }
}
