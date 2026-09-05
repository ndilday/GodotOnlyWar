using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Shared planning-pass read capabilities: live grid/model references, rules, lazy tracing,
    /// and the shared memo. No RNG or action sink is available to decision collaborators.
    /// Inputs are warmed and stable during worker evaluation, not deeply immutable; declaration
    /// and construction mutate them only after every decision completes.
    /// </summary>
    internal sealed class SquadPlanningServices
    {
        internal BattleGridManager Grid { get; }
        internal IReadOnlyDictionary<int, BattleSoldier> SoldierMap { get; }
        internal IReadOnlyDictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplates { get; }
        /// <summary>Null when nothing is listening; every caller must null-check before formatting
        /// a trace, so the no-logging hot path stays free.</summary>
        internal Action<string> Log { get; }
        internal BattlePlanningContext Context { get; }

        internal SquadPlanningServices(
            BattleGridManager grid,
            IReadOnlyDictionary<int, BattleSoldier> soldierMap,
            IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates,
            Action<string> log,
            BattlePlanningContext context)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            SoldierMap = soldierMap ?? throw new ArgumentNullException(nameof(soldierMap));
            MeleeWeaponTemplates = meleeWeaponTemplates
                ?? throw new ArgumentNullException(nameof(meleeWeaponTemplates));
            Log = log;
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Whether the soldier is on the board. Every scorer guards on this before reading a
        /// position, so it lives here rather than being duplicated per collaborator.
        /// </summary>
        internal bool IsPlaced(BattleSoldier soldier) =>
            soldier != null && Grid.IsSoldierPlaced(soldier.Soldier.Id);

        /// <summary>
        /// The battle value a soldier is worth to remove -- the currency every planning score is
        /// denominated in. It uses the tactical equipment-aware value while leaving the intrinsic
        /// SoldierTemplate.BattleValue untouched for strategic force generation.
        /// </summary>
        internal static float BattleValueOf(BattleSoldier soldier) =>
            Math.Max(0, soldier?.EffectiveBattleValue ?? soldier?.Soldier?.Template?.BattleValue ?? 0);
    }

    /// <summary>
    /// The narrow read-side capability used by ranged targeting and shot evaluation.
    ///
    /// <para>This deliberately omits the battle RNG, melee-template map, and action sink from the
    /// targeting dependency graph. The model references remain live -- this is a capability
    /// boundary, not a deep immutable snapshot -- but the supplied operations are limited to the
    /// frozen layout, soldier state, trace sink, and per-pass memo that ranged scoring already
    /// requires.</para>
    /// </summary>
    internal sealed class RangedTargetingServices
    {
        internal BattleGridManager Grid { get; }
        internal IReadOnlyDictionary<int, BattleSoldier> SoldierMap { get; }
        internal Action<string> Log { get; }
        internal BattlePlanningContext Context { get; }

        internal RangedTargetingServices(SquadPlanningServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            Grid = services.Grid;
            SoldierMap = services.SoldierMap;
            Log = services.Log;
            Context = services.Context;
        }

        internal bool IsPlaced(BattleSoldier soldier) =>
            soldier != null && Grid.IsSoldierPlaced(soldier.Soldier.Id);

        internal static float BattleValueOf(BattleSoldier soldier) =>
            SquadPlanningServices.BattleValueOf(soldier);
    }

    /// <summary>
    /// The three action bags a planning pass writes into, separated by resolution phase so the
    /// resolver can order shooting, movement and melee independently of the order they were planned.
    ///
    /// <para>Only the SERIAL half of planning touches this. The resolver chooses every squad's
    /// posture in parallel and only then declares and builds actions one squad at a time, so these
    /// collections need no synchronization -- and must not acquire a parallel writer without
    /// revisiting that.</para>
    /// </summary>
    internal sealed class ActionSink
    {
        internal ICollection<IAction> Shoot { get; }
        internal ICollection<IAction> Move { get; }
        internal ICollection<IAction> Melee { get; }

        internal ActionSink(
            ICollection<IAction> shoot,
            ICollection<IAction> move,
            ICollection<IAction> melee)
        {
            Shoot = shoot ?? throw new ArgumentNullException(nameof(shoot));
            Move = move ?? throw new ArgumentNullException(nameof(move));
            Melee = melee ?? throw new ArgumentNullException(nameof(melee));
        }
    }
}
