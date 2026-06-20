namespace OnlyWar.Helpers
{
    /// <summary>
    /// Injectable random-number source. Lets pure logic (e.g. the rating evaluator)
    /// take its randomness as a dependency so tests can run deterministically with a
    /// fixed or seeded implementation rather than the global static <see cref="RNG"/>.
    /// See TDD §9.1.
    /// </summary>
    public interface IRNG
    {
        /// <summary>Uniform double in [lowerBound, upperBound).</summary>
        double GetDoubleInRange(double lowerBound, double upperBound);

        /// <summary>Uniform double in [0, 1).</summary>
        double GetLinearDouble();

        /// <summary>Uniform integer in [min, max).</summary>
        int GetIntBelowMax(int min, int max);

        /// <summary>Standard normal (mean 0, stddev 1) sample.</summary>
        double NextRandomZValue();
    }
}
