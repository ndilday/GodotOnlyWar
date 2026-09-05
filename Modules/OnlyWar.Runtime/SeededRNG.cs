using System;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Deterministic <see cref="IRNG"/> backed by its own seeded <see cref="Random"/>
    /// instance, independent of the global static <see cref="RNG"/>. Useful for tests
    /// and for any system that wants reproducible randomness in isolation (TDD §9.1).
    /// </summary>
    public sealed class SeededRNG : IRNG
    {
        private readonly Random _random;

        public SeededRNG(int seed)
        {
            _random = new Random(seed);
        }

        public double GetDoubleInRange(double lowerBound, double upperBound)
            => _random.NextDouble() * (upperBound - lowerBound) + lowerBound;

        public double GetLinearDouble() => _random.NextDouble();

        public int GetIntBelowMax(int min, int max) => _random.Next(min, max);

        public double NextRandomZValue()
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = 1.0 - _random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }
    }
}
