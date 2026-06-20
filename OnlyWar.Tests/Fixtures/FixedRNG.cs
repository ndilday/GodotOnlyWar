using OnlyWar.Helpers;

namespace OnlyWar.Tests.Fixtures;

/// <summary>
/// Deterministic <see cref="IRNG"/> test double. <see cref="GetDoubleInRange"/> returns
/// the lower bound, which makes rating normalization divisors exactly the product of the
/// factor lows — letting tests assert formula structure without statistical fuzz.
/// </summary>
internal sealed class FixedRNG : IRNG
{
    public double GetDoubleInRange(double lowerBound, double upperBound) => lowerBound;
    public double GetLinearDouble() => 0.0;
    public int GetIntBelowMax(int min, int max) => min;
    public double NextRandomZValue() => 0.0;
}
