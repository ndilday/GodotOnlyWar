using OnlyWar.Battles.Abstractions;
using System;
using System.Collections.Generic;

namespace OnlyWar.Application;

/// <summary>
/// Owns the current-session mapping between the opaque replay handles produced by Battles and the
/// ids exposed to the host. Replay history never becomes part of a screen contract.
/// </summary>
internal sealed class BattleReplayRegistry
{
    private readonly Dictionary<Guid, IBattleReplay> _byId = [];
    private readonly Dictionary<IBattleReplay, Guid> _byReplay = [];

    internal Guid Register(IBattleReplay replay)
    {
        if (replay == null) return Guid.Empty;
        if (_byReplay.TryGetValue(replay, out Guid existing)) return existing;

        Guid id = Guid.NewGuid();
        _byReplay[replay] = id;
        _byId[id] = replay;
        return id;
    }

    internal bool TryGet(Guid id, out IBattleReplay replay) => _byId.TryGetValue(id, out replay);
}
