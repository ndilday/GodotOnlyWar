using OnlyWar.Domain.Orders;

namespace OnlyWar.Domain.FactionBehaviors
{
    /// <summary>
    /// How much a faction cares about each kind of work when the planner auctions its battle value.
    /// </summary>
    /// <remarks>
    /// These put the task families on one scale. Each family normalises its own score to roughly 0..1
    /// before its weight applies, so a weight says "how much is a fully-served task of this kind worth
    /// to THIS faction" and nothing more.
    ///
    /// They were global constants until 2026-09-17, which meant an Ork WAAAGH and a planetary defence
    /// force shared one table - and because Defend carried the highest weight, an expanding faction
    /// progressively locked its own army into garrison. On Grist Nine the Orks reached 73% of their
    /// strength in reserve by week three and had nearly stopped attacking.
    ///
    /// A weight cannot freeze a faction the way the old fixed priority ladder did: every task saturates,
    /// so a high weight makes the first points of a family expensive to outbid but cannot make its
    /// fortieth point worth anything.
    ///
    /// Source of truth is the FactionDoctrine table in the rules database, one row per faction. This
    /// type's defaults are the fallback for a faction with no row - detached test fixtures, and any
    /// faction added to the rules DB before its doctrine is authored.
    /// </remarks>
    public sealed class ForceDoctrineWeights
    {
        public static readonly ForceDoctrineWeights Balanced = new();

        public double Defend { get; init; } = 1.00;
        public double Withdraw { get; init; } = 0.90;

        /// <summary>
        /// How much this faction cares about marching idle force toward the fighting.
        /// </summary>
        /// <remarks>
        /// Weighs a move against the alternatives, which for a rear province are usually nothing at
        /// all. Before this existed the only thing that relocated strength was a neighbour winning a
        /// share of a region's Defend task, and that fires only on a defensive SHORTFALL - so a
        /// faction strong enough to cover every border never moved a man. Grist Nine, 2026-09-18: the
        /// Orks made zero moves in four weeks while the thinly spread Imperials made twelve.
        /// </remarks>
        public double Move { get; init; } = 0.50;
        public double Assault { get; init; } = 0.85;
        public double Raid { get; init; } = 0.45;
        public double Recon { get; init; } = 0.55;
        public double Patrol { get; init; } = 0.35;
        public double Construct { get; init; } = 0.40;
        public double Spread { get; init; } = 0.60;
        public double Feed { get; init; } = 0.70;

        /// <summary>
        /// How boldly this faction scouts, trading being seen for what it learns.
        /// </summary>
        /// <remarks>
        /// Aggression is a difficulty delta on both axes at once, and MissionAggressionModifiers makes
        /// EffectDifficulty the exact inverse of ExposureDifficulty: a force that will not expose
        /// itself cannot press close enough to learn much. Each step is worth 0.5 difficulty, or 0.1
        /// sigma per daily roll, so Cautious to Aggressive is roughly three times the weekly
        /// observation margin - the single largest lever on whether a sweep becomes actionable.
        ///
        /// It replaces FactionReconPatrolPlanner's awareness ladder, which returned Cautious on
        /// unknown ground and grew bolder as a region became familiar. That spent information exactly
        /// where it was scarcest, and it made the choice for every faction alike: a WAAAGH that does
        /// not care who sees it coming crept about like a cult that does.
        /// </remarks>
        public Aggression ReconAggression { get; init; } = Aggression.Normal;
    }
}
