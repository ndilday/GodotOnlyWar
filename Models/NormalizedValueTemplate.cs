namespace OnlyWar.Models
{
    // A value drawn from a normal distribution: value = BaseValue + z * StandardDeviation,
    // where z is a standard-normal sample. Symmetric around BaseValue.
    public class NormalizedValueTemplate
    {
        public float BaseValue;
        public float StandardDeviation;
    }

    // A value drawn from a log-normal distribution sitting on a hard floor:
    // value = Floor + 10^z * Scale, where z is a standard-normal sample. The 10^z term is
    // always positive, so Floor is a true minimum; Scale is the median magnitude of the
    // variable part (added at z = 0). The distribution is right-skewed, so the mean
    // (Floor + ~3.77 * Scale) sits above the median (Floor + Scale).
    public class LogNormalValueTemplate
    {
        public float Floor;
        public float Scale;
    }

    public class LinearValueTemplate
    {
        public int MinValue;
        public int MaxValue;
    }
}
