using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Battles;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// Small, engine-independent geometry fixtures for comparing interception estimates
/// with the separation that was actually observed. These records intentionally keep
/// soldier speed and position separate from the production battle model so that the
/// examples can be reused by estimate and trace tests without changing battle state.
/// </summary>
internal static class InterceptionEstimateFixture
{
    internal sealed record GeometrySoldier(int Id, float X, float Y, float CurrentSpeed);

    internal sealed record GeometrySquad(int Id, IReadOnlyList<GeometrySoldier> Soldiers);

    internal sealed record ObservedProgress(
        float StartSeparation,
        float EndSeparation,
        float SeparationGain,
        float ScalarClosingSpeed,
        float? ScalarInterceptTurns);

    internal static GeometrySquad SingleSoldierSquad(
        int squadId,
        int soldierId,
        float x,
        float y,
        float currentSpeed)
    {
        return new GeometrySquad(
            squadId,
            new[] { new GeometrySoldier(soldierId, x, y, currentSpeed) });
    }

    internal static float NearestSoldierSeparation(GeometrySquad first, GeometrySquad second)
    {
        return first.Soldiers
            .SelectMany(firstSoldier => second.Soldiers.Select(secondSoldier =>
                Distance(firstSoldier, secondSoldier)))
            .DefaultIfEmpty(float.PositiveInfinity)
            .Min();
    }

    internal static (float X, float Y) Centroid(GeometrySquad squad)
    {
        if (squad.Soldiers.Count == 0)
        {
            return (0f, 0f);
        }

        return (
            squad.Soldiers.Average(soldier => soldier.X),
            squad.Soldiers.Average(soldier => soldier.Y));
    }

    internal static float CentroidSeparation(GeometrySquad first, GeometrySquad second)
    {
        var firstCentroid = Centroid(first);
        var secondCentroid = Centroid(second);
        return MathF.Sqrt(
            MathF.Pow(firstCentroid.X - secondCentroid.X, 2f)
            + MathF.Pow(firstCentroid.Y - secondCentroid.Y, 2f));
    }

    internal static float MinimumCurrentSpeed(GeometrySquad squad)
    {
        return squad.Soldiers
            .Select(soldier => soldier.CurrentSpeed)
            .DefaultIfEmpty(0f)
            .Min();
    }

    internal static GeometrySquad NearestPursuer(
        IReadOnlyCollection<GeometrySquad> pursuers,
        GeometrySquad quarry)
    {
        return pursuers
            .OrderBy(pursuer => NearestSoldierSeparation(pursuer, quarry))
            .ThenBy(pursuer => pursuer.Id)
            .First();
    }

    internal static GeometrySquad NearestQuarry(
        IReadOnlyCollection<GeometrySquad> quarries,
        GeometrySquad pursuer)
    {
        return quarries
            .OrderBy(quarry => NearestSoldierSeparation(pursuer, quarry))
            .ThenBy(quarry => quarry.Id)
            .First();
    }

    /// <summary>
    /// Counterfactual time to melee contact for one assigned pair. This is not
    /// evidence that the pair actually pressed or that the battle will last that long.
    /// </summary>
    internal static float HypotheticalMeleeContactTurns(
        GeometrySquad pursuer,
        GeometrySquad quarry,
        float contactAllowance = BattleContactRules.MeleeContactAllowance)
    {
        var remainingSeparation = MathF.Max(
            0f,
            NearestSoldierSeparation(pursuer, quarry) - contactAllowance);
        var closingSpeed = MinimumCurrentSpeed(pursuer) - MinimumCurrentSpeed(quarry);

        if (remainingSeparation <= 0f)
        {
            return 0f;
        }

        return closingSpeed <= BattleContactRules.PursuitSpeedAdvantageTolerance
            ? float.PositiveInfinity
            : remainingSeparation / closingSpeed;
    }

    /// <summary>
    /// Counterfactual time to a useful attack envelope for one assigned pair. The
    /// useful range can represent ranged fire or a melee allowance supplied by the
    /// caller; it is deliberately distinct from melee contact time.
    /// </summary>
    internal static float HypotheticalUsefulAttackTurns(
        GeometrySquad pursuer,
        GeometrySquad quarry,
        float usefulAttackRange)
    {
        var remainingSeparation = MathF.Max(
            0f,
            NearestSoldierSeparation(pursuer, quarry) - usefulAttackRange);
        var closingSpeed = MinimumCurrentSpeed(pursuer) - MinimumCurrentSpeed(quarry);

        if (remainingSeparation <= 0f)
        {
            return 0f;
        }

        return closingSpeed <= BattleContactRules.PursuitSpeedAdvantageTolerance
            ? float.PositiveInfinity
            : remainingSeparation / closingSpeed;
    }

    /// <summary>
    /// Captures what a trace can say after actions execute. The scalar estimate is
    /// retained as a diagnostic-shaped value only; separation gain is the observed
    /// result and may disagree with the counterfactual estimate.
    /// </summary>
    internal static ObservedProgress Observe(
        GeometrySquad startingPursuer,
        GeometrySquad startingQuarry,
        GeometrySquad endingPursuer,
        GeometrySquad endingQuarry,
        float pursuerDeclaredSpeed,
        float quarryDeclaredSpeed)
    {
        var startSeparation = NearestSoldierSeparation(startingPursuer, startingQuarry);
        var endSeparation = NearestSoldierSeparation(endingPursuer, endingQuarry);
        var scalarClosingSpeed = pursuerDeclaredSpeed - quarryDeclaredSpeed;

        return new ObservedProgress(
            startSeparation,
            endSeparation,
            startSeparation - endSeparation,
            scalarClosingSpeed,
            scalarClosingSpeed > 0f ? endSeparation / scalarClosingSpeed : null);
    }

    private static float Distance(GeometrySoldier first, GeometrySoldier second)
    {
        return MathF.Sqrt(
            MathF.Pow(first.X - second.X, 2f)
            + MathF.Pow(first.Y - second.Y, 2f));
    }
}
