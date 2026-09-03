using System;
using OnlyWar.Models.Battles;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Calculates a soldier's movement result without reserving cells, emitting an action, or
    /// changing soldier state. The grid is read at the point of projection, so callers must treat a
    /// result as speculative and re-project after any serial reservation that intervenes.
    /// </summary>
    internal sealed class SoldierMovementProjector
    {
        // Canonical tier speeds. Decision scoring, withdrawal forecasts, and serial commitment
        // all use these same values.
        internal const float WalkSpeedMultiplier = 0.2f;
        internal const float JogSpeedMultiplier = 0.5f;
        internal const float WalkBulkMultiplier = 0.5f;
        internal const float FullBulkMultiplier = 1f;

        private readonly BattleGridManager _grid;

        internal BattleGridManager Grid => _grid;

        internal SoldierMovementProjector(BattleGridManager grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        internal SoldierMovementProjection ProjectMove(
            BattleSoldier soldier,
            float moveSpeed,
            ValueTuple<int, int> line,
            SquadMovementTier? tier = null,
            BattleGridManager grid = null,
            ValueTuple<int, int>? targetPointOverride = null)
        {
            ValueTuple<int, int> startingPoint = soldier.TopLeft.Value;
            ValueTuple<int, int> desiredMove = targetPointOverride.HasValue
                ? new ValueTuple<int, int>(
                    targetPointOverride.Value.Item1 - startingPoint.Item1,
                    targetPointOverride.Value.Item2 - startingPoint.Item2)
                : CalculateMovementAlongLine(line, moveSpeed);
            ValueTuple<int, int> targetPoint = targetPointOverride is ValueTuple<int, int> overridePoint
                ? overridePoint
                : new ValueTuple<int, int>(
                    startingPoint.Item1 + desiredMove.Item1,
                    startingPoint.Item2 + desiredMove.Item2);
            SquadMovementTier movementTier = tier ?? soldier.BattleSquad.MovementTier;
            SquadMovementTier effectiveTier = movementTier == SquadMovementTier.Run
                && !soldier.CanRun
                    ? SquadMovementTier.Jog
                    : movementTier;
            ushort orientation = CalculateOrientationFromVector(
                line,
                soldier,
                effectiveTier);
            ValueTuple<int, int> destination = FindBestLocation(
                soldier,
                startingPoint,
                targetPoint,
                moveSpeed,
                orientation,
                grid);
            ValueTuple<int, int> actualDirection = new(
                destination.Item1 - startingPoint.Item1,
                destination.Item2 - startingPoint.Item2);
            ValueTuple<int, int> reportedDirection = actualDirection.Item1 == 0
                && actualDirection.Item2 == 0
                    ? line
                    : actualDirection;
            return new SoldierMovementProjection(
                startingPoint,
                desiredMove,
                destination,
                actualDirection,
                reportedDirection,
                orientation,
                moveSpeed,
                movementTier,
                effectiveTier);
        }

        internal static float GetTierSpeed(BattleSoldier soldier, SquadMovementTier tier)
        {
            // A stale Run intent is treated as Jog when armor made running illegal.
            if (tier == SquadMovementTier.Run && !soldier.CanRun)
            {
                tier = SquadMovementTier.Jog;
            }
            return tier switch
            {
                SquadMovementTier.Walk => soldier.GetMoveSpeed() * WalkSpeedMultiplier,
                SquadMovementTier.Jog => soldier.GetMoveSpeed() * JogSpeedMultiplier,
                SquadMovementTier.Run or SquadMovementTier.InMelee => soldier.GetMoveSpeed(),
                _ => 0
            };
        }

        internal static float GetMovementBudget(BattleSoldier soldier, SquadMovementTier tier) =>
            GetTierSpeed(soldier, tier) + soldier.LeftoverMovement;

        internal ValueTuple<int, int> CalculateMovementAlongLine(
            ValueTuple<int, int> line,
            float moveSpeed)
        {
            ValueTuple<int, int> targetLocation;
            if (moveSpeed <= 0) return new ValueTuple<int, int>(0, 0);
            if (line.Item1 == 0)
            {
                targetLocation = new ValueTuple<int, int>(
                    0,
                    line.Item2 < 0 ? -(int)moveSpeed : (int)moveSpeed);
                if (_grid.IsSpaceAvailable(targetLocation)) return targetLocation;
            }
            else if (line.Item2 == 0)
            {
                targetLocation = new ValueTuple<int, int>(
                    line.Item1 < 0 ? -(int)moveSpeed : (int)moveSpeed,
                    0);
                if (_grid.IsSpaceAvailable(targetLocation)) return targetLocation;
            }

            // Multiply line by the square root of moveSpeed^2/line^2.
            int lineLengthSq = (line.Item1 * line.Item1) + (line.Item2 * line.Item2);
            float speedSq = moveSpeed * moveSpeed;
            float multiplier = (float)Math.Sqrt(speedSq / lineLengthSq);

            if (multiplier >= 1.0f) return line;

            float xDistance = line.Item1 * multiplier;
            float yDistance = line.Item2 * multiplier;
            if (xDistance == 0 && yDistance == 0)
            {
                return line.Item1 > line.Item2
                    ? new ValueTuple<int, int>(1, 0)
                    : new ValueTuple<int, int>(0, 1);
            }

            // Preserve the original diagonal rounding and strict budget check. In particular,
            // this is not interchangeable with a normalized-vector or floor-based projection.
            float xLeftover = xDistance % 1;
            float yLeftover = yDistance % 1;
            if (line.Item2 != 0 && xLeftover != 0 && Math.Abs(xDistance) > Math.Abs(yDistance))
            {
                int x = (int)xDistance;
                int y = yDistance < 0 ? (int)yDistance - 1 : (int)yDistance + 1;
                if ((x * x) + (y * y) < speedSq)
                {
                    return new ValueTuple<int, int>(x, y);
                }
            }
            else if (line.Item2 != 0 && yLeftover != 0)
            {
                int x = xDistance < 0 ? (int)xDistance - 1 : (int)xDistance + 1;
                int y = (int)yDistance;
                if ((x * x) + (y * y) < speedSq)
                {
                    return new ValueTuple<int, int>(x, y);
                }
            }
            return new ValueTuple<int, int>((int)xDistance, (int)yDistance);
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
            bool majorIsX = xMove * xMove > yMove * yMove;
            int major = majorIsX ? xMove : yMove;
            int minor = majorIsX ? yMove : xMove;
            int leadSide = minor < 0 ? -1 : 1;

            while (major * major > 0)
            {
                float lateralBudgetSq = speedSq - (major * major);
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
                major -= major > 0 ? 1 : -1;
            }
            return startingPoint;
        }
    }

    /// <summary>
    /// A speculative movement result. It is safe to pass through candidate scoring because it
    /// contains values only; reservations and realized soldier state belong to serial commitment.
    /// </summary>
    internal readonly record struct SoldierMovementProjection(
        ValueTuple<int, int> StartingPoint,
        ValueTuple<int, int> DesiredMove,
        ValueTuple<int, int> Destination,
        ValueTuple<int, int> ActualDirection,
        ValueTuple<int, int> ReportedDirection,
        ushort Orientation,
        float MovementBudget,
        SquadMovementTier DeclaredTier,
        SquadMovementTier EffectiveTier);
}
