using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// The read-side context every squad-planning collaborator needs: the frozen battle state to
    /// reason over, the rules it resolves weapons against, the random stream, the log sink, and the
    /// shared per-turn memo.
    ///
    /// <para>Bundled so an extracted scorer takes ONE constructor parameter instead of six, and so
    /// the planner and its collaborators demonstrably read the same state. Nothing here is mutated
    /// during planning -- <see cref="BattlePlanningContext"/> is a memo whose own thread-safety
    /// invariant is documented on that type, which matters because the resolver runs
    /// <c>ChooseEngagementOption</c> across squads in parallel on a shared planner. Actions are
    /// written through <see cref="ActionSink"/>, which is deliberately NOT part of this bundle:
    /// the decision half of planning must not be able to emit an action.</para>
    /// </summary>
    internal sealed class SquadPlanningServices
    {
        internal BattleGridManager Grid { get; }
        internal IReadOnlyDictionary<int, BattleSoldier> SoldierMap { get; }
        internal IReadOnlyDictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplates { get; }
        internal IRNG Random { get; }
        /// <summary>Null when nothing is listening; every caller must null-check before formatting
        /// a trace, so the no-logging hot path stays free.</summary>
        internal Action<string> Log { get; }
        internal BattlePlanningContext Context { get; }

        internal SquadPlanningServices(
            BattleGridManager grid,
            IReadOnlyDictionary<int, BattleSoldier> soldierMap,
            IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplates,
            IRNG random,
            Action<string> log,
            BattlePlanningContext context)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            SoldierMap = soldierMap ?? throw new ArgumentNullException(nameof(soldierMap));
            MeleeWeaponTemplates = meleeWeaponTemplates
                ?? throw new ArgumentNullException(nameof(meleeWeaponTemplates));
            Random = random ?? throw new ArgumentNullException(nameof(random));
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
        /// denominated in. Static because it reads nothing but the soldier's own template, and
        /// null-tolerant because callers routinely price a target they have not yet resolved.
        /// </summary>
        internal static float BattleValueOf(BattleSoldier soldier) =>
            Math.Max(0, soldier?.Soldier?.Template?.BattleValue ?? 0);
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
