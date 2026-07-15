using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OnlyWar.Helpers.Storage
{
    /// <summary>
    /// Assigns safe save paths and applies retention policy around the existing atomic game-state
    /// writer. Callers supply the write callback so this service stays independent of live scene
    /// state and GameStateDataAccess remains the single serialization implementation.
    /// </summary>
    internal sealed class SaveGameManager
    {
        internal const int PostTurnAutosaveRetention = 3;

        private readonly string _saveDirectory;
        private readonly SaveGameCatalog _catalog;

        internal SaveGameManager(string saveDirectory)
        {
            _saveDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                saveDirectory ?? throw new ArgumentNullException(nameof(saveDirectory))));
            _catalog = new SaveGameCatalog(_saveDirectory);
        }

        internal SaveGameEntry CreateManualSave(
            string displayName,
            string campaignName,
            Action<string> writeSave)
        {
            string cleanedDisplayName = RequireDisplayName(displayName);
            string fileName = $"manual-{MakeFileSlug(cleanedDisplayName)}-{Guid.NewGuid():N}.s3db";
            string savePath = Path.Combine(_saveDirectory, fileName);
            return WriteManagedSave(savePath, cleanedDisplayName, campaignName,
                SaveGameKind.Manual, writeSave);
        }

        internal SaveGameEntry OverwriteManualSave(
            string existingSavePath,
            string displayName,
            string campaignName,
            Action<string> writeSave)
        {
            string fullPath = RequireManagedSavePath(existingSavePath);
            SaveGameEntry existing = _catalog.Find(fullPath)
                ?? throw new FileNotFoundException("The selected manual save no longer exists.", fullPath);
            if (existing.Kind != SaveGameKind.Manual)
            {
                throw new InvalidOperationException("Autosaves cannot be overwritten as manual saves.");
            }

            return WriteManagedSave(fullPath, RequireDisplayName(displayName), campaignName,
                SaveGameKind.Manual, writeSave);
        }

        internal SaveGameEntry SavePostTurnAutosave(
            string campaignName,
            Action<string> writeSave)
        {
            string suffix = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
            string savePath = Path.Combine(
                _saveDirectory,
                $"{GameStorage.PostTurnAutosaveFilePrefix}{suffix}.s3db");
            SaveGameEntry result = WriteManagedSave(savePath, "End of Turn", campaignName,
                SaveGameKind.Autosave, writeSave);
            PrunePostTurnAutosaves(result.FilePath);
            return result;
        }

        internal SaveGameEntry SaveProtectedPreTurn(
            string campaignName,
            Action<string> writeSave)
        {
            string savePath = Path.Combine(
                _saveDirectory,
                GameStorage.ProtectedPreTurnSaveFileName);
            return WriteManagedSave(savePath, "Before Last Turn", campaignName,
                SaveGameKind.ProtectedPreTurn, writeSave);
        }

        internal SaveGameEntry SaveInitialAutosave(
            string campaignName,
            Action<string> writeSave)
        {
            string savePath = Path.Combine(_saveDirectory, GameStorage.InitialAutosaveFileName);
            return WriteManagedSave(savePath, "Campaign Start", campaignName,
                SaveGameKind.Autosave, writeSave);
        }

        internal void DeleteManualSave(string savePath)
        {
            string fullPath = RequireManagedSavePath(savePath);
            SaveGameEntry existing = _catalog.Find(fullPath)
                ?? throw new FileNotFoundException("The selected manual save no longer exists.", fullPath);
            if (existing.Kind != SaveGameKind.Manual)
            {
                throw new InvalidOperationException("Autosaves are managed by the game and cannot be deleted here.");
            }

            File.Delete(fullPath);
            string metadataPath = SaveGameMetadataStore.GetPath(fullPath);
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }

        private SaveGameEntry WriteManagedSave(
            string savePath,
            string displayName,
            string campaignName,
            SaveGameKind kind,
            Action<string> writeSave)
        {
            ArgumentNullException.ThrowIfNull(writeSave);
            Directory.CreateDirectory(_saveDirectory);
            string fullPath = RequireManagedSavePath(savePath);
            string metadataPath = SaveGameMetadataStore.GetPath(fullPath);
            string saveBackup = fullPath + $".{Guid.NewGuid():N}.manager-backup";
            string metadataBackup = metadataPath + $".{Guid.NewGuid():N}.manager-backup";
            bool saveExisted = File.Exists(fullPath);
            bool metadataExisted = File.Exists(metadataPath);
            bool writeStarted = false;
            bool preserveRecoveryCopies = false;
            SaveGameEntry committedEntry = null;

            try
            {
                if (saveExisted)
                {
                    File.Copy(fullPath, saveBackup, overwrite: false);
                }
                if (metadataExisted)
                {
                    File.Copy(metadataPath, metadataBackup, overwrite: false);
                }

                writeStarted = true;
                writeSave(fullPath);
                if (!File.Exists(fullPath))
                {
                    throw new IOException("The save writer completed without producing a save file.");
                }

                SaveGameMetadataStore.Write(fullPath, new SaveGameMetadata
                {
                    DisplayName = displayName,
                    CampaignName = string.IsNullOrWhiteSpace(campaignName)
                        ? null
                        : campaignName.Trim(),
                    Kind = kind
                });

                committedEntry = _catalog.Find(fullPath);
                if (committedEntry == null || !committedEntry.IsCompatible)
                {
                    throw new InvalidDataException(
                        committedEntry?.FailureReason
                        ?? "The completed save could not be validated.");
                }
            }
            catch (Exception writeException)
            {
                if (writeStarted)
                {
                    try
                    {
                        RestoreFile(fullPath, saveBackup, saveExisted);
                        RestoreFile(metadataPath, metadataBackup, metadataExisted);
                    }
                    catch (Exception restoreException)
                    {
                        preserveRecoveryCopies = true;
                        throw new IOException(
                            "The save failed and its prior files could not be fully restored. "
                            + $"Recovery copies remain beside the save. Write error: {writeException.Message}",
                            new AggregateException(writeException, restoreException));
                    }
                }

                throw;
            }
            finally
            {
                if (!preserveRecoveryCopies)
                {
                    DeleteIfExists(saveBackup);
                    DeleteIfExists(metadataBackup);
                }
            }

            return committedEntry;
        }

        private void PrunePostTurnAutosaves(string newestSavePath)
        {
            if (!Directory.Exists(_saveDirectory))
            {
                return;
            }

            IEnumerable<string> expired = Directory
                .EnumerateFiles(
                    _saveDirectory,
                    $"{GameStorage.PostTurnAutosaveFilePrefix}*.s3db",
                    SearchOption.TopDirectoryOnly)
                // The just-committed save is protected even if a custom writer happened to
                // preserve an older filesystem timestamp.
                .OrderByDescending(path => string.Equals(
                    path, newestSavePath, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .Skip(PostTurnAutosaveRetention);

            foreach (string path in expired)
            {
                // Retention cleanup happens only after the new autosave is safely committed.
                // A locked old save may survive one pass, but no valid recovery point is lost.
                try
                {
                    File.Delete(path);
                    DeleteIfExists(SaveGameMetadataStore.GetPath(path));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // The next successful post-turn save will retry pruning.
                }
            }
        }

        private string RequireManagedSavePath(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentException("A save path is required.", nameof(savePath));
            }

            string fullPath = Path.GetFullPath(savePath);
            if (!string.Equals(Path.GetDirectoryName(fullPath), _saveDirectory,
                               StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(fullPath), ".s3db",
                                  StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The save path must be an .s3db file in the save directory.");
            }

            return fullPath;
        }

        private static string RequireDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A manual save name is required.", nameof(displayName));
            }

            string trimmed = displayName.Trim();
            return trimmed.Length <= 100 ? trimmed : trimmed[..100];
        }

        private static string MakeFileSlug(string displayName)
        {
            StringBuilder result = new();
            bool previousWasSeparator = false;
            foreach (char character in displayName.Normalize(NormalizationForm.FormKC))
            {
                if (char.IsLetterOrDigit(character))
                {
                    result.Append(char.ToLowerInvariant(character));
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator && result.Length > 0)
                {
                    result.Append('-');
                    previousWasSeparator = true;
                }

                if (result.Length >= 48)
                {
                    break;
                }
            }

            string slug = result.ToString().Trim('-');
            return string.IsNullOrEmpty(slug) ? "save" : slug;
        }

        private static void RestoreFile(string targetPath, string backupPath, bool existed)
        {
            if (existed)
            {
                if (!File.Exists(backupPath))
                {
                    throw new IOException($"The recovery copy for {Path.GetFileName(targetPath)} is missing.");
                }
                File.Move(backupPath, targetPath, overwrite: true);
            }
            else if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Backup/temp cleanup is best-effort. Never turn a successful save into a failure.
            }
        }
    }
}
