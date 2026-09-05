using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles.Actions;

/// <summary>
/// A squad-level intent whose concrete destinations and defenders are selected only after ordinary
/// movement has resolved. This lets a charge pursue the target squad's real endpoint without
/// granting free movement or binding every charger to a stale soldier position.
/// </summary>
public sealed class SquadChargeIntentAction : IAction
{
    private readonly Func<BattleState, IReadOnlyList<IAction>> _resolve;
    private bool _wasExecuted;

    public int ChargingSquadId { get; }
    public int TargetSquadId { get; }
    public int ActorId { get; }
    public IReadOnlyList<IAction> ResolvedMovementActions { get; private set; } = [];

    public SquadChargeIntentAction(
        BattleSquad chargingSquad,
        BattleSquad targetSquad,
        Func<BattleState, IReadOnlyList<IAction>> resolve)
    {
        ArgumentNullException.ThrowIfNull(chargingSquad);
        ArgumentNullException.ThrowIfNull(targetSquad);
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        ChargingSquadId = chargingSquad.Id;
        TargetSquadId = targetSquad.Id;
        ActorId = chargingSquad.AbleSoldiers
            .Select(soldier => soldier.Soldier.Id)
            .DefaultIfEmpty(chargingSquad.Id)
            .Min();
    }

    public void Execute(BattleState state)
    {
        if (_wasExecuted) return;
        _wasExecuted = true;
        ResolvedMovementActions = _resolve(state) ?? [];
    }

    public string Description() =>
        $"Squad {ChargingSquadId} charges squad {TargetSquadId} after movement\n";
}
