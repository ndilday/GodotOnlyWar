using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Commits a previously calculated movement intent -- a direction line and a speed budget --
    /// into a reserved destination and a <see cref="MoveAction"/>. Calculation belongs to
    /// <see cref="SoldierMovementProjector"/>; this class is the serial mutation boundary.
    ///
    /// <para>Placing a move emits an action and reserves the destination on the grid so later movers
    /// in the same pass see it taken. That makes commitment order-dependent, and safe only on the
    /// resolver's serial action-building phase.</para>
    /// </summary>
    internal sealed class SoldierMovementPlanner
    {
        // Compatibility aliases. SoldierMovementProjector is the canonical home so decision code
        // can receive it without an action sink or a mutation-capable planner.
        internal const float WalkSpeedMultiplier = SoldierMovementProjector.WalkSpeedMultiplier;
        internal const float JogSpeedMultiplier = SoldierMovementProjector.JogSpeedMultiplier;
        internal const float WalkBulkMultiplier = SoldierMovementProjector.WalkBulkMultiplier;
        internal const float FullBulkMultiplier = SoldierMovementProjector.FullBulkMultiplier;

        private readonly SoldierMovementProjector _projector;
        private readonly ActionSink _actions;
        private readonly BattleGridManager _grid;
        private readonly Action<string> _log;

        internal SoldierMovementPlanner(
            SoldierMovementProjector projector,
            ActionSink actions,
            Action<string> log = null)
        {
            _projector = projector ?? throw new ArgumentNullException(nameof(projector));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _grid = projector.Grid;
            _log = log;
        }

        // Compatibility constructor for callers that still construct the commit boundary from the
        // broad planning bundle. The planner retains only the grid and log it needs.
        internal SoldierMovementPlanner(SquadPlanningServices services, ActionSink actions)
            : this(
                new SoldierMovementProjector(services?.Grid
                    ?? throw new ArgumentNullException(nameof(services))),
                actions,
                services.Log)
        {
        }

        internal static float GetTierSpeed(BattleSoldier soldier, SquadMovementTier tier)
        {
            // A caller may still hold a precomputed Run intent from before armor was resolved.
            // Treat it as the fastest legal movement tier for this soldier rather than allowing
            // restrictive armor to receive full Run speed through a stale decision.
            return SoldierMovementProjector.GetTierSpeed(soldier, tier);
        }

        internal static float GetMovementBudget(BattleSoldier soldier, SquadMovementTier tier)
        {
            return SoldierMovementProjector.GetMovementBudget(soldier, tier);
        }

        internal ValueTuple<int, int> AddMoveAction(
            BattleSoldier soldier,
            float moveSpeed,
            ValueTuple<int, int> line,
            SquadMovementTier? tier = null)
        {
            SoldierMovementProjection projection = _projector.ProjectMove(
                soldier,
                moveSpeed,
                line,
                tier);
            CommitProjectedMove(soldier, projection);
            return projection.ReportedDirection;
        }

        /// <summary>
        /// Serially commits a speculative result: reserve the full rotated footprint, construct
        /// the action, and update the declared soldier state in the same order as the old planner.
        /// </summary>
        internal MoveAction CommitProjectedMove(
            BattleSoldier soldier,
            SoldierMovementProjection projection,
            bool addToActionSink = true)
        {
            _grid.ReserveMoveDestination(
                soldier,
                projection.Destination,
                projection.Orientation);
            MoveAction action = new(
                soldier,
                _grid,
                projection.StartingPoint,
                projection.Destination,
                projection.Orientation,
                projection.MovementBudget);
            if (addToActionSink)
            {
                _actions.Move.Add(action);
            }

            ValueTuple<int, int> actualDirection = projection.ActualDirection;
            soldier.CurrentSpeed = Math.Min(
                GetTierSpeed(soldier, projection.DeclaredTier),
                (float)Math.Sqrt(
                    actualDirection.Item1 * actualDirection.Item1
                    + actualDirection.Item2 * actualDirection.Item2));
            if (soldier.CurrentSpeed <= 0)
            {
                soldier.IsRunning = false;
            }
            if (projection.EffectiveTier != SquadMovementTier.Run)
            {
                soldier.IsRunning = false;
            }
            LogMove(
                soldier,
                projection.EffectiveTier,
                projection.MovementBudget,
                projection.DesiredMove,
                actualDirection);
            return action;
        }

        /// <summary>
        /// Commits the fixed adjacency destination used by the pre-movement charge declaration.
        /// This retains that path's historical one-cell reservation and declared InMelee speed;
        /// the projection itself still comes from SoldierMovementProjector.
        /// </summary>
        internal MoveAction CommitChargeDestination(
            BattleSoldier soldier,
            ValueTuple<int, int> currentPosition,
            ValueTuple<int, int> destination,
            ushort orientation,
            float movementBudget)
        {
            _grid.ReserveSpace(destination);
            MoveAction action = new(
                soldier,
                _grid,
                currentPosition,
                destination,
                orientation,
                movementBudget);
            _actions.Move.Add(action);
            soldier.CurrentSpeed = GetTierSpeed(soldier, SquadMovementTier.InMelee);
            return action;
        }

        /// <summary>
        /// Creates the immediate charge move used after ordinary movement reservations are cleared.
        /// The MoveAction itself performs the live-grid placement immediately; reserving a stale
        /// endpoint here would change which later charger sees the target squad's live position.
        /// </summary>
        internal MoveAction CreateImmediateChargeMove(
            BattleSoldier soldier,
            SoldierMovementProjection projection)
        {
            soldier.CurrentSpeed = GetTierSpeed(soldier, SquadMovementTier.InMelee);
            return new MoveAction(
                soldier,
                _grid,
                projection.StartingPoint,
                projection.Destination,
                projection.Orientation,
                projection.MovementBudget);
        }

        internal SoldierMovementProjection ProjectMove(
            BattleSoldier soldier,
            float moveSpeed,
            ValueTuple<int, int> line,
            SquadMovementTier? tier = null,
            BattleGridManager grid = null,
            ValueTuple<int, int>? targetPointOverride = null) =>
            _projector.ProjectMove(
                soldier,
                moveSpeed,
                line,
                tier,
                grid,
                targetPointOverride);

        /// <summary>
        /// Per-soldier movement trace: what the tier allowed, what the soldier asked for, and what
        /// the grid actually gave it.
        ///
        /// <para>WHY THE THREE ARE SEPARATE. A soldier that covers less ground than its tier permits
        /// has been squeezed by <see cref="FindBestLocation"/>, which walks the major axis down one
        /// cell at a time until the whole footprint fits in free, unreserved cells. That is invisible
        /// in the squad-level ENGAGE_EVAL record -- which reports the posture CHOSEN, not the
        /// distance ACHIEVED -- and it bites large models hardest, since an N-cell footprint needs
        /// every one of those cells. Without <c>blocked</c> there is no way to tell a monster that
        /// decided to jog from one that tried to run and could not fit.</para>
        /// </summary>
        private void LogMove(
            BattleSoldier soldier,
            SquadMovementTier tier,
            float budget,
            ValueTuple<int, int> desired,
            ValueTuple<int, int> achieved)
        {
            if (_log == null) return;
            float desiredDistance = (float)Math.Sqrt(
                (desired.Item1 * desired.Item1) + (desired.Item2 * desired.Item2));
            float achievedDistance = (float)Math.Sqrt(
                (achieved.Item1 * achieved.Item1) + (achieved.Item2 * achieved.Item2));
            string line = new BattleDecisionTrace("MOVE", new List<KeyValuePair<string, string>>
            {
                BattleDecisionTrace.Field("soldier", soldier.Soldier.Id),
                BattleDecisionTrace.Field("name", soldier.Soldier.Name),
                BattleDecisionTrace.Field("squad", soldier.BattleSquad?.Id),
                BattleDecisionTrace.Field("tier", tier),
                BattleDecisionTrace.Field("base_speed", soldier.GetMoveSpeed()),
                BattleDecisionTrace.Field("tier_speed", GetTierSpeed(soldier, tier)),
                BattleDecisionTrace.Field("budget", budget),
                BattleDecisionTrace.Field("leftover_in", soldier.LeftoverMovement),
                BattleDecisionTrace.Field("desired", desiredDistance),
                BattleDecisionTrace.Field("achieved", achievedDistance),
                // The whole point of the record: achieved < desired means the footprint did not fit.
                BattleDecisionTrace.Field("blocked", achievedDistance + 0.0001f < desiredDistance),
                BattleDecisionTrace.Field("current_speed", soldier.CurrentSpeed)
            }).Render();
            lock (_log)
            {
                _log(line);
            }
        }

        // Charge construction shares the projector's geometry without duplicating its rules.
        internal ValueTuple<int, int> CalculateMovementAlongLine(
            ValueTuple<int, int> line,
            float moveSpeed) =>
            _projector.CalculateMovementAlongLine(line, moveSpeed);

        internal ushort CalculateOrientationFromVector(
            ValueTuple<int, int> vector,
            BattleSoldier soldier = null,
            SquadMovementTier tier = SquadMovementTier.Stationary) =>
            _projector.CalculateOrientationFromVector(vector, soldier, tier);
    }
}
