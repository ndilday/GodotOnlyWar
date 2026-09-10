using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles.Models;

namespace OnlyWar.Battles.Actions;

/// <summary>
/// A squad-level closing move whose concrete destinations are selected only after ordinary movement
/// has resolved, so a squad closes on where its quarry actually ended up rather than on a stale
/// position. Movement is simultaneous: a pursuer that aims at the quarry's turn-start cell arrives
/// where nobody is standing and undershoots by the quarry's move every turn, however much faster it
/// is. See <see cref="BattleContactRules.CanReachContactThisTurn"/> for the observed failure.
///
/// <para>This action builds NO attacks. Attacks resolve from turn-start geometry, ahead of movement
/// (TDD §6.6); reaching contact here marks the arriving soldier so that
/// next turn's strike is a charge. Making this pass build attacks is what grew a second, divergent
/// engaged-soldier decision that silently lost point-blank fire and gun-and-blade.</para>
/// </summary>
public sealed class SquadClosingMoveAction : IAction
{
    private readonly Func<BattleState, IReadOnlyList<IAction>> _resolve;
    private bool _wasExecuted;

    public int ClosingSquadId { get; }
    public int TargetSquadId { get; }
    public int ActorId { get; }
    public IReadOnlyList<IAction> ResolvedMovementActions { get; private set; } = [];

    public SquadClosingMoveAction(
        BattleSquad closingSquad,
        BattleSquad targetSquad,
        Func<BattleState, IReadOnlyList<IAction>> resolve)
    {
        ArgumentNullException.ThrowIfNull(closingSquad);
        ArgumentNullException.ThrowIfNull(targetSquad);
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        ClosingSquadId = closingSquad.Id;
        TargetSquadId = targetSquad.Id;
        ActorId = closingSquad.AbleSoldiers
            .Select(soldier => soldier.Soldier.Id)
            .DefaultIfEmpty(closingSquad.Id)
            .Min();
    }

    public void Execute(BattleState state)
    {
        if (_wasExecuted) return;
        _wasExecuted = true;
        ResolvedMovementActions = _resolve(state) ?? [];
    }

    public string Description() =>
        $"Squad {ClosingSquadId} closes on squad {TargetSquadId} after movement\n";
}
