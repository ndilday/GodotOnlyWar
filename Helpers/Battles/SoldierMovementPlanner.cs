using System;
using System.Collections.Generic;
using OnlyWar.Helpers.Battles.Actions;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Turns a movement intent -- a direction line and a speed budget -- into an actual destination
    /// and a <see cref="MoveAction"/>: how far a tier lets a soldier travel, where along that line
    /// it lands, which way it ends up facing, and how the grid squeezes the footprint when the
    /// intended square will not fit.
    ///
    /// <para>The only planning collaborator that holds an <see cref="ActionSink"/> as well as a
    /// <see cref="SquadPlanningServices"/>: placing a move IS emitting an action, and it also
    /// reserves the destination on the grid so later movers in the same pass see it taken. That
    /// makes it order-dependent, and safe only on the resolver's serial action-building phase.</para>
    /// </summary>
    internal sealed class SoldierMovementPlanner
    {
        // THE canonical tier speeds. Internal because the planner's posture scoring and the
        // resolver's pursuit projections must both predict the speed this class will actually move
        // at -- they disagreed (0.66 vs 0.5) until 2026-07-30, and the posture decision was
        // predicting a jog a third faster than the one it got. Do not re-declare these elsewhere.
        internal const float WalkSpeedMultiplier = 0.2f;
        internal const float JogSpeedMultiplier = 0.5f;

        private readonly SquadPlanningServices _services;
        private readonly ActionSink _actions;
        private readonly BattleGridManager _grid;
        private readonly Action<string> _log;

        internal SoldierMovementPlanner(SquadPlanningServices services, ActionSink actions)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _grid = _services.Grid;
            _log = _services.Log;
        }

        internal static float GetTierSpeed(BattleSoldier soldier, SquadMovementTier tier)
        {
            return tier switch
            {
                SquadMovementTier.Walk => soldier.GetMoveSpeed() * WalkSpeedMultiplier,
                SquadMovementTier.Jog => soldier.GetMoveSpeed() * JogSpeedMultiplier,
                SquadMovementTier.Run or SquadMovementTier.InMelee => soldier.GetMoveSpeed(),
                _ => 0
            };
        }

        internal static float GetMovementBudget(BattleSoldier soldier, SquadMovementTier tier)
        {
            return GetTierSpeed(soldier, tier) + soldier.LeftoverMovement;
        }

        internal ValueTuple<int, int> AddMoveAction(
            BattleSoldier soldier,
            float moveSpeed,
            ValueTuple<int, int> line,
            SquadMovementTier? tier = null)
        {
            ValueTuple<int, int> desiredMove = CalculateMovementAlongLine(line, moveSpeed);
            ValueTuple<int, int> newLocation = new ValueTuple<int, int>(soldier.TopLeft.Value.Item1 + desiredMove.Item1, soldier.TopLeft.Value.Item2 + desiredMove.Item2);
            SquadMovementTier movementTier = tier ?? soldier.BattleSquad.MovementTier;
            ushort orientation = CalculateOrientationFromVector(line, soldier, movementTier);
            newLocation = FindBestLocation(
                soldier,
                soldier.TopLeft.Value,
                newLocation,
                moveSpeed,
                orientation);
            _grid.ReserveMoveDestination(soldier, newLocation, orientation);
            _actions.Move.Add(new MoveAction(
                soldier,
                _grid,
                soldier.TopLeft.Value,
                newLocation,
                orientation,
                moveSpeed));
            ValueTuple<int, int> actualDirection = new(
                newLocation.Item1 - soldier.TopLeft.Value.Item1,
                newLocation.Item2 - soldier.TopLeft.Value.Item2);
            soldier.CurrentSpeed = Math.Min(
                GetTierSpeed(soldier, movementTier),
                (float)Math.Sqrt(
                    actualDirection.Item1 * actualDirection.Item1
                    + actualDirection.Item2 * actualDirection.Item2));
            if (soldier.CurrentSpeed <= 0)
            {
                soldier.IsRunning = false;
            }
            LogMove(soldier, movementTier, moveSpeed, desiredMove, actualDirection);
            return actualDirection.Item1 == 0 && actualDirection.Item2 == 0
                ? line
                : actualDirection;
        }

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

        internal ValueTuple<int, int> CalculateMovementAlongLine(ValueTuple<int, int> line, float moveSpeed)
        {
            ValueTuple<int, int> targetLocation;
            if (moveSpeed <= 0) return new ValueTuple<int, int>(0, 0);   // this shouldn't happen
            else if(line.Item1 == 0)
            {
                targetLocation = new ValueTuple<int, int>(0, line.Item2 < 0 ? -(int)moveSpeed : (int)moveSpeed);
                if (_grid.IsSpaceAvailable(targetLocation)) return targetLocation;
            }
            else if(line.Item2 == 0)
            {
                targetLocation = new ValueTuple<int, int>(line.Item1 < 0 ? -(int)moveSpeed : (int)moveSpeed, 0);
                if (_grid.IsSpaceAvailable(targetLocation)) return targetLocation;
            }

            // multiply line by the square root of moveSpeed^2/line^2
            int lineLengthSq = (line.Item1 * line.Item1) + (line.Item2 * line.Item2);
            float speedSq = moveSpeed * moveSpeed;
            float multiplier = (float)Math.Sqrt(speedSq / lineLengthSq);

            // if we're fast enough to get to the destination, just go there
            if (multiplier >= 1.0f) return line;

            float xDistance = line.Item1 * multiplier;
            float yDistance = line.Item2 * multiplier;

            // should always move a minimum of one space
            if (xDistance == 0 && yDistance == 0)
            {
                if (line.Item1 > line.Item2)
                {
                    return new ValueTuple<int, int>(1, 0);
                }
                else
                {
                    return new ValueTuple<int, int>(0, 1);
                }
            }
            else
            {
                // if there's movement in both dimensions and "Wasted" movement in the longer direction
                // determine if the excess is enough to finish the movement along the smaller leg
                float xLeftover = xDistance % 1;
                float yLeftover = yDistance % 1;

                if (line.Item2 != 0 && xLeftover != 0 && Math.Abs(xDistance) > Math.Abs(yDistance))
                {
                    int x = (int)xDistance;
                    int y = yDistance < 0 ? (int)yDistance -1 : (int)yDistance + 1;
                    if((x * x) + (y * y) < speedSq)
                    {
                        return new ValueTuple<int, int>(x, y);
                    }
                }
                else if (line.Item2 != 0 && yLeftover != 0)
                {
                    int x = xDistance < 0 ? (int)xDistance - 1: (int)xDistance + 1;
                    int y = (int)yDistance;
                    if ((x * x) + (y * y) < speedSq)
                    {
                        return new ValueTuple<int, int>(x, y);
                    }
                }
            }
            return new ValueTuple<int, int> ((int)xDistance, (int)yDistance);
        }

        internal ushort CalculateOrientationFromVector(
            ValueTuple<int, int> vector,
            BattleSoldier soldier = null,
            SquadMovementTier tier = SquadMovementTier.Stationary)
        {
            if (vector.Item1 == 0 && vector.Item2 == 0)
            {
                return soldier?.Orientation ?? 0;
            }

            double angle = Math.Atan2(vector.Item1, vector.Item2);
            int desired = (int)Math.Round(angle / (Math.PI / 4.0));
            desired = (desired % BattleOrientation.HeadingCount
                + BattleOrientation.HeadingCount)
                % BattleOrientation.HeadingCount;

            if (soldier == null
                || (tier != SquadMovementTier.Run && tier != SquadMovementTier.InMelee))
            {
                return (ushort)desired;
            }

            int current = soldier.Orientation % BattleOrientation.HeadingCount;
            int difference = desired - current;
            if (difference > BattleOrientation.HeadingCount / 2)
            {
                difference -= BattleOrientation.HeadingCount;
            }
            else if (difference < -(BattleOrientation.HeadingCount / 2))
            {
                difference += BattleOrientation.HeadingCount;
            }

            int limited = Math.Clamp(difference, -1, 1);
            return (ushort)((current + limited + BattleOrientation.HeadingCount)
                % BattleOrientation.HeadingCount);
        }

        internal ValueTuple<int, int> FindBestLocation(
            BattleSoldier soldier,
            ValueTuple<int, int> startingPoint,
            ValueTuple<int, int> targetPoint,
            float speed,
            ushort orientation,
            BattleGridManager grid = null)
        {
            grid ??= _grid;
            float speedSq = speed * speed;
            int xMove = targetPoint.Item1 - startingPoint.Item1;
            int yMove = targetPoint.Item2 - startingPoint.Item2;
            // Shift around the shorter axis first: the major axis carries the intent of the move,
            // so give ground on the minor one.
            bool majorIsX = xMove * xMove > yMove * yMove;
            int major = majorIsX ? xMove : yMove;
            int minor = majorIsX ? yMove : xMove;
            // Which side of the intended lateral offset gets probed first. Outward (away from the
            // line of travel) matches the pre-existing bias.
            int leadSide = minor < 0 ? -1 : 1;

            while (major * major > 0)
            {
                float lateralBudgetSq = speedSq - (major * major);
                // Probe the intended offset first and then alternate outward from it — 0, +1, -1,
                // +2, -2, … A side that has left the movement budget is skipped rather than ending
                // the search, because when the intended offset is nonzero the two sides run out at
                // different magnitudes and the nearer one still has usable squares.
                for (int magnitude = 0; ; magnitude++)
                {
                    bool anyWithinBudget = false;
                    int sides = magnitude == 0 ? 1 : 2;
                    for (int side = 0; side < sides; side++)
                    {
                        int lateral = minor
                            + (magnitude * (side == 0 ? leadSide : -leadSide));
                        if (lateral * lateral > lateralBudgetSq) continue;
                        anyWithinBudget = true;
                        ValueTuple<int, int> newTarget = majorIsX
                            ? new ValueTuple<int, int>(
                                startingPoint.Item1 + major,
                                startingPoint.Item2 + lateral)
                            : new ValueTuple<int, int>(
                                startingPoint.Item1 + lateral,
                                startingPoint.Item2 + major);
                        if (grid.IsMoveDestinationAvailable(soldier, newTarget, orientation))
                        {
                            return newTarget;
                        }
                    }
                    if (!anyWithinBudget) break;
                }
                // if we can't find a lateral move that works, start over with the main axis reduced by 1
                major -= major > 0 ? 1 : -1;
            }
            return startingPoint;
        }
    }
}
