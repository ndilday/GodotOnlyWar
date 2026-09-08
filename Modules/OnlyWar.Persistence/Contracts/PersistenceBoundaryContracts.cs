using System;
using System.Collections.Generic;

namespace OnlyWar.Persistence.Contracts;

/// <summary>
/// Explicit file primitive used by persistence adapters.  No connection, reader, command, or
/// active-session type crosses this boundary.
/// </summary>
public interface IAtomicCampaignFileStore
{
    byte[] Read(string filePath);
    void Write(string filePath, ReadOnlyMemory<byte> contents);
}

/// <summary>Result metadata returned by a storage adapter without selecting a live campaign.</summary>
public sealed record PersistenceLoadResult<TState>(
    TState State,
    int FormatVersion,
    bool UpgradeRequired,
    IReadOnlyList<string> Warnings = null);

/// <summary>
/// Application supplies reconstruction because persistence must not reference Generation,
/// Runtime, or the active-session owner.
/// </summary>
public interface IDerivedStateReconstructionPort<TState, TDerivedState>
{
    TDerivedState Rebuild(TState state);
}
