using System;

namespace OnlyWar.Helpers.Fortifications
{
    /// <summary>
    /// Converts between a defense <em>level</em> (the displayed strength of a position) and the
    /// <em>construction points</em> invested to reach it, so works held by several factions can be
    /// pooled without inventing fortifications out of nothing.
    /// </summary>
    /// <remarks>
    /// The curve is fixed by the existing build economy, not chosen freely.
    /// FactionDevelopmentPlanner.DefenseBuildCost charges base*10^level to raise a stat, so the
    /// cumulative investment to reach level L is the repunit sum base*(10^0 + ... + 10^(L-1)):
    ///
    ///     Points(L) = (10^L - 1) / 9      Level(P) = log10(9P + 1)
    ///
    /// normalised so one point is exactly the cost of the first level. Two things follow, and both
    /// are the reason fortifications are combined here rather than added:
    ///
    /// 1. Adding levels would be a straight lie about cost. Two allies at level 1 have each paid one
    ///    point; a level-2 position costs eleven. Combining in point space gives Level(2) = 1.28,
    ///    not 2, so a side can never buy a cheap high level by splitting the work between factions.
    /// 2. Because Points' derivative is proportional to 10^L - exactly the marginal price the build
    ///    economy already charges - every faction buys points at the same rate no matter what level
    ///    it sits at. So a faction can keep pricing construction off its OWN level (which it does)
    ///    and the cost per unit of the shared position still comes out identical regardless of how
    ///    the contributions are split.
    ///
    /// Asymptotically n equal allies at level L combine to L + log10(n): doubling the total
    /// investment buys 0.301 of a level, which is what a 10x-per-band curve should yield.
    /// </remarks>
    public static class FortificationMath
    {
        // Past this the double math stops being meaningful and the build economy has already
        // plateaued (DefenseCostCapLevel), so clamp rather than overflow into infinity.
        private const double MaxLevel = 12.0;

        public static double LevelToPoints(double level)
        {
            if (level <= 0.0) return 0.0;
            if (level > MaxLevel) level = MaxLevel;
            return (Math.Pow(10.0, level) - 1.0) / 9.0;
        }

        public static double PointsToLevel(double points)
        {
            if (points <= 0.0) return 0.0;
            return Math.Min(MaxLevel, Math.Log10(9.0 * points + 1.0));
        }

        /// <summary>
        /// The single level that the given separately-held levels amount to when their works are
        /// manned as one position.
        /// </summary>
        public static double Combine(double first, double second) =>
            PointsToLevel(LevelToPoints(first) + LevelToPoints(second));

        /// <summary>
        /// Adds construction points to a position currently at <paramref name="level"/> and returns
        /// the level that results. This is how effort becomes fortification: a fixed number of
        /// points per week climbs quickly at first and then flattens, because each further level
        /// costs ten times the last.
        /// </summary>
        public static double AddPoints(double level, double points) =>
            PointsToLevel(LevelToPoints(level) + Math.Max(0.0, points));

        /// <summary>
        /// How much of the side's shared position a faction's own construction actually buys, as a
        /// fraction in (0, 1].
        /// </summary>
        /// <remarks>
        /// A faction prices construction off its own level, which is correct - every faction buys
        /// points at the same rate. But points bought cheaply at a low own level barely move a
        /// shared position an ally has already built high, because the next shared level costs ten
        /// times the last. Without this a planner sees a cheap option with undiminished benefit and
        /// pours its whole spare garrison into a region that is already as fortified as it needs to
        /// be. The ratio is Points'(own) / Points'(shared) = 10^(own - shared), and it is exactly 1
        /// when the faction has no allied works beside it.
        /// </remarks>
        public static double SharedContributionEfficiency(double ownLevel, double sharedLevel)
        {
            if (sharedLevel <= ownLevel) return 1.0;
            return Math.Pow(10.0, ownLevel - sharedLevel);
        }
    }
}
