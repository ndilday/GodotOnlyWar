using System;

namespace OnlyWar.Persistence.Abstractions;

/// <summary>
/// Explicit file primitive used by persistence adapters. No connection, reader, command, or
/// active-session type crosses this boundary.
/// </summary>
public interface IAtomicCampaignFileStore
{
    byte[] Read(string filePath);
    void Write(string filePath, ReadOnlyMemory<byte> contents);
}
