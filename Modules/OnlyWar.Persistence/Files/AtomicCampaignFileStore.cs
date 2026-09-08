using System;
using System.IO;
using OnlyWar.Persistence.Abstractions;

namespace OnlyWar.Persistence.Files;

/// <summary>
/// The persistence-owned atomic file primitive. Higher layers choose which campaign to save and
/// supply the bytes; this class never opens or publishes a session.
/// </summary>
public sealed class AtomicCampaignFileStore : IAtomicCampaignFileStore
{
    public byte[] Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return File.ReadAllBytes(Path.GetFullPath(filePath));
    }

    public void Write(string filePath, ReadOnlyMemory<byte> contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The file path must include a directory.", nameof(filePath));
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, contents.ToArray());
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
