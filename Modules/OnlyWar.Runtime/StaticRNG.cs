namespace OnlyWar.Helpers
{
    /// <summary>
    /// <see cref="IRNG"/> adapter over the global static <see cref="RNG"/>. Production
    /// default: ratings and other consumers keep drawing from the single seeded
    /// sequence that <see cref="RNG.Reset"/> controls, so generation stays deterministic
    /// for a given seed. Tests inject a fixed or seeded implementation instead.
    /// </summary>
    public sealed class StaticRNG : IRNG
    {
        public static readonly StaticRNG Instance = new StaticRNG();

        public double GetDoubleInRange(double lowerBound, double upperBound)
            => RNG.GetDoubleInRange(lowerBound, upperBound);

        public double GetLinearDouble() => RNG.GetLinearDouble();

        public int GetIntBelowMax(int min, int max) => RNG.GetIntBelowMax(min, max);

        public double NextRandomZValue() => RNG.NextRandomZValue();
    }
}
