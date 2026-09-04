namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Thresholds shared by the strategic reconnaissance and offensive-evaluation policies.
/// Keeping this one cross-policy signal in a named type avoids copying the value while leaving
/// policy-specific constants with their owning planner.
/// </summary>
internal static class FactionStrategyPlanningConstants
{
    internal const float ReconIntelThreshold = 1.0f;
}
