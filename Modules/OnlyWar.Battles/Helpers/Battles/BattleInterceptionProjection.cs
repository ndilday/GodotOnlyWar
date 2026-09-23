using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Battles;

/// <summary>
/// Pure, bounded geometry for a hypothetical pressing pursuer and one quarry squad.
///
/// <para>The projection deliberately answers a counterfactual question. It does not read
/// <see cref="BattleSoldier.CurrentSpeed"/>, selected options, leftover movement, or executed
/// actions. The adapter uses the same fast-approach capability as the withdrawal/pursuit roles,
/// then this class solves the relative straight-line motion analytically instead of rolling the
/// battle forward.</para>
/// </summary>
internal static class BattleInterceptionProjection
{
    internal const float GeometryTolerance = 0.0001f;

    internal readonly record struct Point(float X, float Y);

    internal readonly record struct PairInput(
        int PursuerSquadId,
        int QuarrySquadId,
        Point PursuerPosition,
        Point QuarryPosition,
        float PursuerMoveSpeed,
        float QuarryMoveSpeed,
        Point QuarryHeading,
        float ContactAllowance = BattleContactRules.MeleeContactAllowance);

    internal readonly record struct PairResult(
        int PursuerSquadId,
        int QuarrySquadId,
        float InitialDistance,
        float ContactTurns,
        float AttackableContactTurns,
        string Reason)
    {
        internal bool IsReachable => !float.IsPositiveInfinity(ContactTurns);

        /// <summary>
        /// Contact can be created by this turn's movement. It is not a claim that a melee attack
        /// can resolve this turn: attacks resolve before movement, so only contact already present
        /// at turn start is attackable immediately.
        /// </summary>
        internal bool CanReachContactThisTurn =>
            IsReachable && ContactTurns <= 1f + GeometryTolerance;
    }

    /// <summary>
    /// The same relative-motion projection as <see cref="Project(PairInput)"/>, but with an
    /// arbitrary destination allowance.  A useful ranged-fire boundary is a different
    /// destination from melee contact: reaching it during a movement pass makes the shot
    /// available in the next attack phase, whereas an already-entered boundary is available at
    /// elapsed time zero.
    /// </summary>
    internal readonly record struct DestinationResult(
        int PursuerSquadId,
        int QuarrySquadId,
        float InitialDistance,
        float DestinationAllowance,
        float DestinationTurns,
        float AttackableDestinationTurns,
        string Reason)
    {
        internal bool IsReachable => !float.IsPositiveInfinity(DestinationTurns);

        internal bool CanReachThisTurn =>
            IsReachable && DestinationTurns <= 1f + GeometryTolerance;
    }

    internal readonly record struct AggregateResult(
        float EarliestContactTurns,
        float EarliestAttackableContactTurns,
        int? PursuerSquadId,
        int? QuarrySquadId,
        bool CanReachContactThisTurn,
        int PairCount,
        PairInput? SelectedInput = null,
        PairResult? SelectedPair = null)
    {
        internal bool IsReachable => !float.IsPositiveInfinity(EarliestContactTurns);

        /// <summary>
        /// The pair that supplies the finite aggregate, or the first stable candidate when every
        /// pair is unreachable. The latter is diagnostic context only; it is not a supplier.
        /// </summary>
        internal bool HasSelectedCandidate => SelectedInput.HasValue && SelectedPair.HasValue;

        internal string SelectionReason => SelectedPair?.Reason ?? "no_valid_pair";
    }

    /// <summary>
    /// Projects one pair. The quarry continues on its supplied withdrawal heading and the
    /// pursuer presses toward the future quarry position, choosing the shortest feasible path to
    /// the moving contact radius. The first non-negative time at which the pursuer can be within
    /// <paramref name="input"/>'s contact allowance is returned.
    /// </summary>
    internal static PairResult Project(PairInput input)
    {
        DestinationResult result = ProjectToDestination(input, input.ContactAllowance);
        return new PairResult(
            result.PursuerSquadId,
            result.QuarrySquadId,
            result.InitialDistance,
            result.DestinationTurns,
            result.AttackableDestinationTurns,
            result.Reason switch
            {
                "already_in_destination" => "already_in_contact",
                "reaches_destination" => "press_reaches_contact",
                _ => result.Reason
            });
    }

    /// <summary>
    /// Projects the first time that a concrete pair can enter a destination radius.  The
    /// allowance is supplied by the caller so ranged useful-fire timing cannot accidentally be
    /// read as melee contact timing.
    /// </summary>
    internal static DestinationResult ProjectToDestination(
        PairInput input,
        float destinationAllowance)
    {
        float initialDistance = Distance(input.PursuerPosition, input.QuarryPosition);
        if (!IsFinite(input.PursuerPosition.X)
            || !IsFinite(input.PursuerPosition.Y)
            || !IsFinite(input.QuarryPosition.X)
            || !IsFinite(input.QuarryPosition.Y)
            || !IsFinite(input.QuarryHeading.X)
            || !IsFinite(input.QuarryHeading.Y)
            || !IsFinite(initialDistance)
            || !IsFinite(input.PursuerMoveSpeed)
            || !IsFinite(input.QuarryMoveSpeed)
            || !IsFinite(destinationAllowance))
        {
            return UnreachableDestination(input, initialDistance, destinationAllowance,
                "invalid_geometry");
        }

        float allowance = MathF.Max(0, destinationAllowance);
        if (initialDistance <= allowance + GeometryTolerance)
        {
            return new DestinationResult(
                input.PursuerSquadId,
                input.QuarrySquadId,
                initialDistance,
                allowance,
                0,
                0,
                "already_in_destination");
        }

        Point quarryDirection = Normalize(input.QuarryHeading.X, input.QuarryHeading.Y);
        float pursuerSpeed = MathF.Max(0, input.PursuerMoveSpeed);
        float quarrySpeed = MathF.Max(0, input.QuarryMoveSpeed);

        // The pursuer may steer toward the quarry's predicted point, which is the bounded
        // analytical equivalent of pressing rather than holding its last selected action's line.
        // Contact is feasible at time t when the target's distance from the pursuer's start is no
        // more than the pursuer's travel plus the contact allowance:
        //     |relativePosition + quarryVelocity * t| <= pursuerSpeed * t + allowance.
        Point relativePosition = new(
            input.QuarryPosition.X - input.PursuerPosition.X,
            input.QuarryPosition.Y - input.PursuerPosition.Y);
        Point quarryVelocity = new(
            quarryDirection.X * quarrySpeed,
            quarryDirection.Y * quarrySpeed);

        float a = Dot(quarryVelocity, quarryVelocity) - (pursuerSpeed * pursuerSpeed);
        float b = 2f * (Dot(relativePosition, quarryVelocity)
            - (pursuerSpeed * allowance));
        float c = Dot(relativePosition, relativePosition) - (allowance * allowance);
        if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
        {
            return UnreachableDestination(input, initialDistance, allowance, "invalid_geometry");
        }

        if (MathF.Abs(a) <= GeometryTolerance)
        {
            if (b >= -GeometryTolerance)
            {
                return UnreachableDestination(
                    input, initialDistance, allowance, "no_feasible_closing_path");
            }

            float linearContactTurns = -c / b;
            return linearContactTurns < -GeometryTolerance
                ? UnreachableDestination(input, initialDistance, allowance, "moving_apart")
                : BuildDestinationReachable(
                    input, initialDistance, allowance, MathF.Max(0, linearContactTurns));
        }

        float discriminant = (b * b) - (4f * a * c);
        if (discriminant < -GeometryTolerance)
        {
            return UnreachableDestination(input, initialDistance, allowance,
                "misses_destination_radius");
        }

        float root = MathF.Sqrt(MathF.Max(0, discriminant));
        float first = (-b - root) / (2f * a);
        float second = (-b + root) / (2f * a);
        float contactTurns = FirstNonNegative(first, second);
        if (float.IsPositiveInfinity(contactTurns))
        {
            return UnreachableDestination(input, initialDistance, allowance, "moving_apart");
        }

        return BuildDestinationReachable(input, initialDistance, allowance, contactTurns);
    }

    private static DestinationResult BuildDestinationReachable(
        PairInput input,
        float initialDistance,
        float destinationAllowance,
        float contactTurns)
    {
        // A movement contact made during this turn is usable by melee at the next turn's attack
        // phase. Exact boundary contacts stay on the current movement interval.
        float attackableTurns = contactTurns <= GeometryTolerance
            ? 0
            : MathF.Ceiling(contactTurns - GeometryTolerance);
        return new DestinationResult(
            input.PursuerSquadId,
            input.QuarrySquadId,
            initialDistance,
            destinationAllowance,
            contactTurns,
            attackableTurns,
            contactTurns <= GeometryTolerance ? "already_in_destination" : "reaches_destination");
    }

    /// <summary>
    /// Returns the earliest feasible pair in stable squad-id order. An all-unreachable aggregate
    /// uses positive infinity and no supplying pair, which keeps the impossible state explicit.
    /// </summary>
    internal static AggregateResult EarliestPair(IEnumerable<PairInput> inputs)
    {
        List<(PairInput Input, PairResult Result)> projections = (inputs ?? [])
            .OrderBy(result => result.PursuerSquadId)
            .ThenBy(result => result.QuarrySquadId)
            .Select(input => (input, Project(input)))
            .ToList();
        List<(PairInput Input, PairResult Result)> feasible = projections
            .Where(result => result.Result.IsReachable)
            .OrderBy(result => result.Result.ContactTurns)
            .ThenBy(result => result.Result.PursuerSquadId)
            .ThenBy(result => result.Result.QuarrySquadId)
            .ToList();
        if (feasible.Count == 0)
        {
            (PairInput Input, PairResult Result)? diagnostic = projections.Count == 0
                ? null
                : projections[0];
            return new AggregateResult(
                float.PositiveInfinity,
                float.PositiveInfinity,
                null,
                null,
                false,
                projections.Count,
                diagnostic?.Input,
                diagnostic?.Result);
        }
        (PairInput Input, PairResult Result) earliest = feasible[0];

        return new AggregateResult(
            earliest.Result.ContactTurns,
            earliest.Result.AttackableContactTurns,
            earliest.Result.PursuerSquadId,
            earliest.Result.QuarrySquadId,
            earliest.Result.CanReachContactThisTurn,
            projections.Count,
            earliest.Input,
            earliest.Result);
    }

    internal static Point Normalize(float x, float y)
    {
        float length = MathF.Sqrt((x * x) + (y * y));
        return length <= GeometryTolerance
            ? new Point(0, 0)
            : new Point(x / length, y / length);
    }

    internal static float Distance(Point first, Point second)
    {
        float dx = first.X - second.X;
        float dy = first.Y - second.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Returns the shortest separation still possible after <paramref name="elapsedTurns"/> of
    /// the pair's straight-line relative motion.  It is useful when a preparation action and a
    /// movement action occupy the same turn: the attack phase sees the turn-start separation, so
    /// callers can evaluate the next integer attack phase without inventing a mid-turn shot.
    /// </summary>
    internal static float SeparationAtTime(PairInput input, float elapsedTurns)
    {
        if (!IsFinite(elapsedTurns) || elapsedTurns < 0) return float.PositiveInfinity;
        Point direction = Normalize(input.QuarryHeading.X, input.QuarryHeading.Y);
        float pursuerSpeed = MathF.Max(0, input.PursuerMoveSpeed);
        float quarrySpeed = MathF.Max(0, input.QuarryMoveSpeed);
        Point relative = new(
            input.QuarryPosition.X - input.PursuerPosition.X,
            input.QuarryPosition.Y - input.PursuerPosition.Y);
        Point quarryVelocity = new(
            direction.X * quarrySpeed,
            direction.Y * quarrySpeed);
        Point quarryAtTime = new(
            relative.X + (quarryVelocity.X * elapsedTurns),
            relative.Y + (quarryVelocity.Y * elapsedTurns));
        return MathF.Max(0, Distance(new Point(0, 0), quarryAtTime)
            - (pursuerSpeed * elapsedTurns));
    }

    private static DestinationResult UnreachableDestination(
        PairInput input,
        float initialDistance,
        float destinationAllowance,
        string reason) =>
        new(
            input.PursuerSquadId,
            input.QuarrySquadId,
            initialDistance,
            MathF.Max(0, destinationAllowance),
            float.PositiveInfinity,
            float.PositiveInfinity,
            reason);

    private static PairResult Unreachable(PairInput input, float initialDistance, string reason) =>
        new(
            input.PursuerSquadId,
            input.QuarrySquadId,
            initialDistance,
            float.PositiveInfinity,
            float.PositiveInfinity,
            reason);

    private static float FirstNonNegative(float first, float second)
    {
        float result = float.PositiveInfinity;
        if (first >= -GeometryTolerance) result = MathF.Max(0, first);
        if (second >= -GeometryTolerance) result = MathF.Min(result, MathF.Max(0, second));
        return result;
    }

    private static float Dot(Point first, Point second) =>
        (first.X * second.X) + (first.Y * second.Y);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
