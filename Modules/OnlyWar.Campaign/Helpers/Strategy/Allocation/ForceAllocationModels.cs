using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Strategy.Allocation;

/// <summary>
/// The kinds of work a faction can spend battle value on in one planning pass.
/// </summary>
/// <remarks>
/// Ambush is deliberately absent. Surprise decorates an advance or a raid (Order.OpensWithAmbush)
/// rather than competing for force, and since RegionFaction.HasEmergenceAdvantage now expires at the
/// end of the planning pass that follows a reveal, it is not a bankable asset that would need an
/// option value of its own.
/// </remarks>
internal enum ForceTaskKind
{
    Defend,
    Withdraw,
    Move,
    Recon,
    Patrol,
    Assault,
    Raid,
    Construct,
    ConsumptionSpread,
    Feed
}

internal static class ForceAllocationConstants
{
    /// <summary>
    /// Ceiling on how many candidate bid sizes one region offers. Once battle value is the currency,
    /// this is the quantum's only remaining job, so it is a runtime knob and not a balance one.
    /// </summary>
    internal const int MaxBidStepsPerRegion = 100;

    /// <summary>
    /// How far a region may bid from, in region hops. This is the auction's main cost driver, because
    /// it multiplies every task by the regions that can reach it.
    /// </summary>
    /// <remarks>
    /// One hop, not two. An offensive stages from ground bordering its target - PotentialOffensive's
    /// own AttackingRegions are the adjacent regions - so a force two borders away could not join the
    /// attack this week in any case, and its bid was already discounted to about a third. Allowing it
    /// roughly tripled the number of pairings the auction had to price for no change in the plan.
    /// </remarks>
    internal const int MaxBidHops = 1;

    /// <summary>Value retained per hop a force has to cross before it can act.</summary>
    internal const double TransitDiscountPerHop = 0.6;

    /// <summary>
    /// Price charged per alternative enemy target a staging region could otherwise have answered. Keeps
    /// the flexible region free and spends the cornered one, which is what
    /// FactionStagingPlanner's opportunity-cost ordering used to decide by fiat.
    /// </summary>
    internal const double SourceFlexibilityPenalty = 0.35;

    /// <summary>
    /// Marginal value per battle value of a garrison ALREADY at its saturation. Small and positive, so
    /// Defend absorbs the residue and beats doing nothing, and can never outbid real work. This is what
    /// lets Defend serve as the reserve sink, so there is no separate "hold in reserve" task.
    /// </summary>
    internal const double DefenceEpsilonTailPerBattleValue = 1e-9;

    /// <summary>
    /// Weight of the "a hopeless defence still buys time" term against the "hold the region" term.
    /// P(hold) alone is an S-curve whose low end is flat, so a small garrison facing a large threat
    /// would value defence at nearly nothing and abandon the region - the overrun this whole design
    /// exists to avoid. The delay term is near-linear and keeps a floor under defence.
    /// </summary>
    internal const double DefenceDelayWeight = 0.35;

    /// <summary>Fraction of its saturation at which a defence's hold probability turns over.</summary>
    internal const double DefenceHoldKnee = 0.6;

    /// <summary>Fraction of its CARRY POINT at which an assault becomes likely to take the region.</summary>
    /// <remarks>
    /// Shapes the value curve only. It is NOT the launch threshold: an assault launches at the full
    /// carry point (ForceTaskBuilder.AddOffensiveTasks), and the curve below that is never realised
    /// because ForceTask.TotalValue is zero under MinimumViableAward.
    /// </remarks>
    internal const double AssaultKnee = 0.7;

    /// <summary>
    /// What an assault that annihilates the defence is worth against one that merely carries the
    /// region.
    /// </summary>
    /// <remarks>
    /// An assault used to saturate at its force ratio - about 1.5 times the believed defender - so the
    /// auction was told that more force than that changes nothing. The resolver says otherwise. A
    /// defence takes clamped losses at any ordinary ratio and can go to ground only at exactly zero, so
    /// force ABOVE the carry point buys the one outcome the carry point cannot: annihilation
    /// (StrategicCombatRules.OverrunForceRatio).
    ///
    /// Monody Prime, 2026-09-18: 35,164 Ork battle value shared Alpha with 33 Imperials and the region
    /// stayed contested week after week. The Orks were not refusing to finish it - they were never
    /// asked to. The assault was sized against the remnant, so about fifty points attacked it, and a
    /// 1.5:1 fight leaves a remnant that re-arms its population growth before the next turn
    /// (PlanetDemographicsProcessor.OverrunRemnantGarrisonArmingRate).
    ///
    /// The multiple is what the tail is worth, not what it costs: value at and below the carry point is
    /// UNCHANGED, because the importance is raised by exactly the share the shape gives back there. So
    /// this cannot make an assault outbid or underbid anything at its launch threshold; it only prices
    /// the force beyond it, which nothing else was bidding for anyway.
    /// </remarks>
    internal const double AnnihilationValueMultiple = 4.0 / 3.0;

    /// <summary>
    /// Scout squads past which another observer adds little, read off the resolver as the rule
    /// requires.
    /// </summary>
    /// <remarks>
    /// Recon is the ONE mission type that fans out: MissionForcePolicy.GetMode returns
    /// IndependentSquads for it and nothing else, so MissionTurnProcessor.BuildMissionElements gives
    /// every assigned squad its own MissionContext, its own driver and its own daily
    /// LeaderMissionTest roll. TurnIntelligenceLedger then pools every squad's every daily margin and
    /// ReconIntelligenceRules.AwarenessDelta takes the square root of the total - "diminishing returns
    /// in both days and squads", in its own words.
    ///
    /// So the value of a probe really does grow with squad count, as sqrt, which is why the shape for
    /// this task is Concave while almost everything else is Linear. Reading LeaderMissionTest alone
    /// suggests otherwise - it makes a single roll for the most senior leader - but that roll happens
    /// once PER SQUAD, not once per order.
    ///
    /// Three is a deliberate floor on the diminishing return rather than a hard mechanical limit: the
    /// fourth squad is still worth something, and the auction will buy it when nothing else wants the
    /// battle value.
    /// </remarks>
    internal const int ReconSaturationSquads = 3;

    /// <summary>
    /// Observation margin one scout squad is expected to pool over a week's sweep.
    /// </summary>
    /// <remarks>
    /// Observation margin one scout squad is expected to pool over a week, scouting at Normal.
    /// </summary>
    /// <remarks>
    /// Measured, not guessed, across two Grist Nine runs on 2026-09-17. Cautious sweeps averaged 0.96
    /// per squad; Aggressive ones 2.77. Normal sits one step above Cautious, hence 1.55.
    ///
    /// THE POOLED MARGIN IS A SIGNED SUM, not a best-of-n, so a squad that rolls badly cancels one that
    /// rolled well - a Cautious run saw Theta lose 3.56 to a single sweep and Pi finish net negative.
    /// Adding squads raises the mean and the variance together, so reliability comes from the
    /// aggression the faction scouts at far more than from squad count. That is what
    /// FactionDoctrine.ReconAggression is for: at Aggressive the same four-squad sweep pools around 11
    /// against a threshold needing 4, and negatives fell from a quarter of sweeps to a tenth.
    /// </remarks>
    internal const double ExpectedSweepMarginAtNormal = 1.55;

    /// <summary>
    /// How much a squad's weekly observation margin moves per step of recon aggression.
    /// </summary>
    /// <remarks>
    /// Aggression is a difficulty delta, and a mission check converts it at (skill - difficulty) / 5
    /// per daily roll over seven days, so one 0.5-difficulty step is worth 0.7 of weekly margin in
    /// theory. Measured across two Grist Nine runs it came out at 0.60: Cautious sweeps averaged 0.96
    /// per squad and Aggressive ones 2.77, three steps apart.
    ///
    /// A single flat figure cannot serve here, which the first calibration missed. The expected margin
    /// depends on how boldly the faction scouts, so a Genestealer Cult on Avoid needs far more squads
    /// for the same result than an Ork horde on Aggressive - and sizing both from one constant either
    /// starves the cult or wastes half the WAAAGH.
    /// </remarks>
    internal const double MarginPerAggressionStep = 0.60;

    /// <summary>
    /// Ceiling on one region's sweep, so a hopeless awareness gap cannot demand the whole army.
    /// </summary>
    internal const int MaxReconSquadsPerSweep = 6;

    // A MaxPatrolSquadsPerRegion sat here briefly on 2026-09-20, capping the patrol task's saturation
    // because a screen was the one defensive task that spent its award by instantiating soldiers. The
    // screen is now abstract (RegionFaction.PatrolScreenBattleValue), so the cap was removed rather
    // than kept: it would have constrained balance to solve a problem that no longer exists. The
    // ceiling that matters now is StrategicCombatRules.MaxGeneratedSquads / MaxTacticalActors, which
    // bounds every force that actually reaches a battle.

    /// <summary>
    /// Population at or below which a region is not worth holding for its inhabitants alone, and the
    /// population at which it is worth the maximum. Worth scales logarithmically between them.
    /// </summary>
    /// <remarks>
    /// FIXED ANCHORS, not the largest region on the planet. Defence and construction used to divide a
    /// region's population by `states.Max(...)`, so adding one large region made every other region
    /// less worth defending and less worth fortifying — the same defect removed from reconnaissance,
    /// and the reason the invariant exists: a family may normalise only against something intrinsic to
    /// itself.
    ///
    /// Logarithmic because the spread demands it. Grist Nine runs 507 to 35,824; Monody Prime runs 350
    /// to 7,320,539. On a linear scale every region but the largest is worth nothing, which is exactly
    /// what dividing by the maximum already did.
    ///
    /// The consequence is intended: a region of 35,824 really is worth less than one of 7,320,539, and
    /// a faction fighting on both worlds should value them differently. The old normaliser made every
    /// planet's largest region score 1.0 regardless of whether it held a hamlet or a hive.
    /// </remarks>
    internal const double RegionWorthFloorPopulation = 100.0;
    internal const double RegionWorthReferencePopulation = 100_000.0;

    /// <summary>
    /// Least a region is worth while an enemy borders it, however few people live there.
    /// </summary>
    /// <remarks>
    /// A FLOOR, not a quantity. The intent is narrow and always was: a thinly populated frontier region
    /// matters because losing it opens the way in, and scoring on population alone made the rear
    /// province refuse to spare a man for the border it sits behind. Everything above that floor should
    /// be decided by what the ground is actually worth.
    ///
    /// It was briefly `min(1.0, hostileFronts * 0.35)`, which saturated immediately: on a contested
    /// planet every region borders three or more enemy-held regions, 3 x 0.35 caps at 1.0, and the
    /// population term then never entered the max at all. Grist Nine, 2026-09-18 — every Ork region
    /// reported `imp=0.550`, which is the doctrine weight times exactly 1.0. Counting fronts made the
    /// term do the opposite of its job.
    ///
    /// It also replaced an earlier threat term that could not work: defence importance used to include
    /// `unansweredThreat / largestUnansweredThreat` while the defence SATURATION is that same threat,
    /// so the two cancelled and every threatened region bid at an identical rate. Position is what this
    /// expresses; the enemy's SIZE is already in the saturation. Same rule the offensive side learned —
    /// importance is a property of the target, and force-dependence lives in the curve.
    /// </remarks>
    internal const double FrontierWorthFloor = 0.25;

    /// <summary>
    /// Value the front retains per region hop, which is what makes "closer to the enemy" a number.
    /// </summary>
    /// <remarks>
    /// A potential field over the planet: every region holding a public enemy is a source, and the
    /// value falls by this factor per hop outward. A rear province three borders back sits at 0.216
    /// against its neighbour's 0.36, so the gradient points forward from ANY depth - which is the
    /// whole reason a field is needed rather than a look at the neighbours. MaxBidHops is 1, so
    /// without this an interior region cannot see the front at all and its force never marches.
    ///
    /// Kept separate from TransitDiscountPerHop even though both start at 0.6. That one prices a bid
    /// reaching across a border THIS TURN; this one describes how far away the fighting is. They
    /// answer different questions and should be tunable apart.
    /// </remarks>
    internal const double FrontGradientPerHop = 0.6;

    /// <summary>
    /// The pull of the smallest enemy presence, as a share of the largest one on the planet.
    /// </summary>
    /// <remarks>
    /// Sources are weighted by believed enemy strength so force flows toward the main fight rather
    /// than the nearest picket, but never to zero: a lone enemy holding a region is still a reason to
    /// march that way, and an unweighted field would send the whole army to one border.
    /// </remarks>
    internal const double MinimumFrontPull = 0.25;

    /// <summary>
    /// The most of its marchable force a region will send forward in one turn.
    /// </summary>
    /// <remarks>
    /// Without it a rear province empties into its neighbour in a single week, because the surplus has
    /// nothing else to bid on and the whole of it is therefore the best use of itself. A gradient
    /// should produce a flow, not a teleport: half a turn at a time means the army concentrates over
    /// several turns and the line behind it thins gradually rather than vanishing.
    /// </remarks>
    internal const double MaxMarchFractionPerTurn = 0.5;

    /// <summary>
    /// How much force a region can usefully hold, as a multiple of what holding it actually needs.
    /// </summary>
    /// <remarks>
    /// A staging area, not a garrison: a frontier region massing for an attack needs more than its own
    /// defence requires, but not without limit. Past this the ground is full and the force is better
    /// left where it is, where it at least holds something. This also bounds the double-dip - a
    /// region's Defend task may pull reinforcements from the same neighbour in the same pass, and its
    /// shortfall is inside this headroom rather than added on top of it.
    /// </remarks>
    internal const double FrontStagingMultiple = 3.0;

    /// <summary>
    /// Hold probability below which a region is considered lost, making Withdraw worth scoring at all.
    /// </summary>
    internal const double WithdrawHoldThreshold = 0.25;

    /// <summary>
    /// A region whose troops are pulled out keeps this share as a rearguard rather than emptying.
    /// </summary>
    internal const double WithdrawRearguardFraction = 0.15;

    // Doctrine weights moved to ForceDoctrineWeights on 2026-09-17, sourced per faction from the
    // FactionDoctrine table in the rules database. Until then they were global constants, so an Ork
    // WAAAGH and a planetary defence force shared one table - and because Defend carried the highest
    // weight, an expanding faction progressively locked its own army into garrison.

    /// <summary>
    /// The construction benefit that earns the full construction weight. Set at the level
    /// FactionDevelopmentPlanner assigns to entrenching a region with an enemy standing in it, which is
    /// the most urgent build in the game. Everything else is priced as a fraction of that: entrenching
    /// against a neighbour lands near two thirds, a listening post on quiet ground near a quarter, and
    /// anti-air - which no combat resolver currently reads - near a sixteenth.
    /// </summary>
    internal const double ReferenceConstructionBenefit = 4.5;

    /// <summary>
    /// How much of a region's whole defence one level of works adds, which is what stops a cheap build
    /// being mistaken for a valuable one.
    /// </summary>
    /// <remarks>
    /// Read off the combat rules rather than chosen: a level of entrenchment is worth about 8% through
    /// StrategicCombatRules.DefenderProtection, about 10% through EntrenchmentMultiplier, and about 20%
    /// off tactical casualties in MissionAftermathProcessor - call it a sixth of a defence, and less at
    /// higher levels as those curves flatten. Without this the model priced ONE LEVEL of works as
    /// though it were comparable to garrisoning the region outright, which is what let a 200-point
    /// build outbid a 1,000-point garrison.
    /// </remarks>
    internal const double ConstructionLevelShareOfDefence = 1.0 / 6.0;

    // RESOLVED 2026-09-18 by removing the asymmetry instead of pricing it: the default faction is no
    // longer pinned to defensiveOnly, so there is no faction that mounts no offensives to discount.
    // A PDF still almost never attacks, but the force ratio stops it rather than a rule. Posture is an
    // INTENTION, not a strength or a position, so no amount of reconnaissance could have observed it.
    // See TurnOrderPlanner and PRD §4.24.
}

/// <summary>
/// Shapes describing what the k-th point of battle value is worth to a task, as a cumulative fraction
/// of the task's importance. Every shape is continuous in battle value and every one returns 1.0 at
/// saturation, so families remain comparable once their doctrine weight is applied.
/// </summary>
internal static class ForceValueCurves
{
    /// <summary>
    /// Diminishing returns. Retained for a resolver that genuinely has them, but NOT the default.
    /// </summary>
    /// <remarks>
    /// A square root has unbounded slope at zero, so the first sliver of a concave task is worth an
    /// arbitrarily large amount PER BATTLE VALUE - and since that is exactly how bids are ranked, a
    /// concave task outbids every threshold-shaped one no matter how doctrine weights them. Feeding and
    /// patrolling starved the assaults outright until these became Linear.
    ///
    /// The rule that came out of it: linear is the default, and a curve is non-linear only where the
    /// resolver really has a threshold - the defence hold probability and the assault force ratio.
    /// </remarks>
    internal static double Concave(double fraction) =>
        fraction <= 0.0 ? 0.0 : Math.Sqrt(Math.Clamp(fraction, 0.0, 1.0));

    /// <summary>
    /// A threshold: worth little until the commitment approaches the level that actually decides the
    /// outcome, then rising quickly. Normalised so the shape still spans 0..1 across the saturation
    /// range rather than starting and ending part-way up the logistic.
    /// </summary>
    internal static double Threshold(double fraction, double knee)
    {
        double x = Math.Clamp(fraction, 0.0, 1.0);
        double at0 = Logistic(0.0, knee);
        double at1 = Logistic(1.0, knee);
        if (at1 - at0 <= double.Epsilon) return x;
        return (Logistic(x, knee) - at0) / (at1 - at0);
    }

    /// <summary>
    /// An assault: a threshold at the force ratio that carries the region, then a linear tail to the
    /// ratio that annihilates the defence rather than merely beating it.
    /// </summary>
    /// <remarks>
    /// Two thresholds, not one, because the resolver has two. Taking the ground needs the force ratio;
    /// finishing the defenders needs StrategicCombatRules.OverrunForceRatio, and between the two the
    /// extra force buys a steadily better chance of leaving nothing behind. A single threshold at the
    /// carry point told the auction that the second one did not exist, which is why a horde beside a
    /// remnant kept sending a squad at it.
    ///
    /// The tail is LINEAR. It is not a second threshold: every point of it makes the mop-up more
    /// complete, and the overrun ratio is where that stops rather than where it starts.
    /// </remarks>
    internal static double Assault(long battleValue, long carryPoint, long annihilationPoint)
    {
        if (carryPoint <= 0L) return 0.0;
        double carried = Threshold(
            battleValue / (double)carryPoint, ForceAllocationConstants.AssaultKnee);
        if (annihilationPoint <= carryPoint) return carried;

        double carryShare = 1.0 / ForceAllocationConstants.AnnihilationValueMultiple;
        double surplus = Math.Clamp(
            (battleValue - carryPoint) / (double)(annihilationPoint - carryPoint), 0.0, 1.0);
        return carried * carryShare + surplus * (1.0 - carryShare);
    }

    /// <summary>Value accrues evenly up to saturation. Used where a resolver spends what it is given.</summary>
    internal static double Linear(double fraction) => Math.Clamp(fraction, 0.0, 1.0);

    /// <summary>
    /// Probability that a defence of this relative size holds. Used for its own sake by Withdraw, which
    /// has to know when a region is lost rather than merely expensive.
    /// </summary>
    internal static double HoldProbability(double fraction) =>
        Threshold(Math.Clamp(fraction, 0.0, 1.0), ForceAllocationConstants.DefenceHoldKnee);

    private static double Logistic(double x, double knee) =>
        1.0 / (1.0 + Math.Exp(-(x - knee) / 0.15));
}

/// <summary>
/// One piece of work competing for battle value, with the point past which more force stops changing
/// the outcome and the shape of what each point is worth on the way there.
/// </summary>
/// <remarks>
/// Saturation is READ OFF the resolver that will execute the task, never authored: the rule is "the
/// point at which the resolver stops responding to more troops". Several already existed in the code
/// before this system - an assault's force ratio, the defensive requirement, a construction level's
/// cost, and the point in RaidUtility where expected damage stops binding.
/// </remarks>
internal sealed class ForceTask
{
    internal ForceTaskKind Kind { get; init; }

    /// <summary>The region the task is about: the target for an offensive, the home region otherwise.</summary>
    internal Region Objective { get; init; }

    /// <summary>The acting faction's own presence that this task belongs to, where it has one.</summary>
    internal RegionFaction Home { get; init; }

    internal PotentialOffensive Offensive { get; init; }
    internal DefenseType? Construction { get; init; }
    internal double ConstructionAmount { get; init; }
    internal Region Destination { get; init; }

    /// <summary>Doctrine-weighted worth of this task when fully staffed.</summary>
    internal double Importance { get; init; }

    /// <summary>
    /// Separates tasks that score identically per battle value. Higher wins.
    /// </summary>
    /// <remarks>
    /// This exists so a preference can be expressed WITHOUT putting it in the importance, where it
    /// would distort the ranking against every other family. Reconnaissance is the case it was added
    /// for: the size of a target's population is a reason to scout that region rather than an equally
    /// cheap one, but it is not a reason to scout it rather than to garrison, build or attack - and
    /// scaling the importance by it said the second thing while meaning the first.
    ///
    /// It also cannot couple one task to another. The old recon importance divided by the largest
    /// candidate's population, so adding a city to a planet made every hamlet on it less worth
    /// looking at.
    /// </remarks>
    internal double TieBreakValue { get; init; }

    /// <summary>Battle value past which this task's own resolver stops responding.</summary>
    internal long Saturation { get; init; }

    /// <summary>
    /// The least battle value that produces anything at all. Below it the award is simply burned.
    /// </summary>
    /// <remarks>
    /// Faction.MinimumForceRequest is NOT this figure. It prices the cheapest squad of any kind, while
    /// a tasking that needs a particular kind of formation has to pay that formation's price - a
    /// reconnaissance sweep builds through ForceCompositionProfile.ScoutPatrol, so an Ork probe costs a
    /// 109-point Kommando squad even though the roster's cheapest squad is a 30-point Nobz remnant.
    ///
    /// Observed 2026-09-16 on Grist Nine: seven recon taskings in one week were each awarded somewhere
    /// between 30 and 108, generated no squads, and took their region's battle value with them. The
    /// Orks issued no offensive at all that week as a result.
    /// </remarks>
    internal long MinimumViableAward { get; init; } = 1L;

    /// <summary>The least this task will accept from a bidder, given what it already holds.</summary>
    /// <remarks>
    /// The region's own floor, and nothing more. This USED to demand the whole of
    /// <see cref="MinimumViableAward"/> from the first bid, which meant a task could only ever be
    /// funded by ONE region: with nothing assigned, the floor was the entire award, so a bidder that
    /// could not cover it alone made no bid, and the task therefore never left zero and never lowered
    /// its floor.
    ///
    /// Two neighbours holding 200 apiece could not between them pay for a 327-point sweep, with 400
    /// spare beside the target. Viability is now checked ONCE, when the auction ends
    /// (ForceAllocationAuction's refund pass), which is the point at which the total is actually
    /// known.
    /// </remarks>
    internal long MinimumBidGiven(long sourceFloor) => sourceFloor;

    /// <summary>True while this task holds too little to produce anything at all.</summary>
    internal bool ShortOfViableAward => Assigned < MinimumViableAward;

    /// <summary>Cumulative fraction of Importance earned by a given battle value.</summary>
    internal Func<long, double> Shape { get; init; }

    /// <summary>
    /// Battle values at which this task's own curve turns, always offered as bid amounts whatever the
    /// bidder's quantum.
    /// </summary>
    /// <remarks>
    /// For curves whose turns sit at a fixed fraction of the saturation the auction can derive them
    /// (ForceAllocationAuction.KneeFractions). An assault's cannot be derived that way any more: its
    /// saturation is the overrun ratio while its threshold is at the carry point, and the two are set
    /// from different things. A coarse region stepping over the carry point badly over- or
    /// under-commits, which is the one case where a coarse quantum causes a wrong decision rather than
    /// a rough one.
    /// </remarks>
    internal IReadOnlyList<long> ValueBreakPoints { get; init; } = Array.Empty<long>();

    /// <summary>
    /// Enemy presences this task's saturation is shared with. Two regions garrisoning against the same
    /// neighbour are answering ONE threat, and counting it twice is what
    /// FactionThreatAssessment.ExpectedAttackerCommitFraction exists to hedge against. Naming the
    /// sources lets the ledger below state it exactly instead.
    /// </summary>
    internal IReadOnlyList<RegionFaction> SharedThreats { get; init; } = Array.Empty<RegionFaction>();

    /// <summary>
    /// Tasks that cannot both be funded. An assault and a raid on the same region are alternatives, not
    /// a pair: the auction prices them against each other, and whichever draws force first excludes the
    /// other. Without this a faction both stormed and raided the same target in one week.
    /// </summary>
    internal TaskExclusionGroup Exclusion { get; init; }

    internal long Assigned { get; private set; }

    /// <summary>Of <see cref="Assigned"/>, how much came from outside the objective's own region.</summary>
    internal long AssignedFromOutside { get; private set; }

    /// <summary>
    /// The most this task may draw from OTHER regions - the shortfall, not the whole requirement.
    /// </summary>
    /// <remarks>
    /// Troops already standing in a region count toward holding it whether or not they were able to
    /// bid for the job; a region too thin to field one squad cannot bid at all, and its garrison is
    /// still there. Without this cap a neighbour delivered the entire requirement on top of the men
    /// already in place, and the region ended up holding half again what it needed while the rear
    /// province emptied.
    /// </remarks>
    internal long OutsideCapacity { get; init; } = long.MaxValue;

    internal long RemainingFromOutside =>
        Math.Max(0L, Math.Min(Remaining, OutsideCapacity - AssignedFromOutside));

    internal long Remaining => Math.Max(0L, Saturation - Assigned);

    internal void Award(long battleValue, bool fromOutside)
    {
        long amount = Math.Max(0L, battleValue);
        Assigned += amount;
        if (fromOutside) AssignedFromOutside += amount;
        // An exclusion is claimed by the task that becomes VIABLE, not by the one that took the first
        // crumb. Claiming it on any award at all was safe only while a bid had to cover the whole
        // viable amount in one go: now that several regions may fill a task between them, an assault
        // could take one quantum, lock its raid out, then end the auction short of its launch
        // threshold and be refunded - leaving the target neither stormed nor raided.
        if (Exclusion != null && !ShortOfViableAward) Exclusion.Winner ??= this;
    }

    /// <summary>True when an alternative to this task has already taken the force.</summary>
    internal bool ExcludedByRival => Exclusion?.Winner != null && !ReferenceEquals(Exclusion.Winner, this);

    internal double TotalValue(long battleValue)
    {
        if (battleValue <= 0L || Saturation <= 0L) return 0.0;
        // A task holding less than its viable award is worth NOTHING, because the committer will
        // refuse it. MinimumViableAward is a hard gate and the value curve did not know about it, so
        // the curve happily reported an assault at 1,387 of a 1,400 floor as worth about half its
        // importance - when it was in fact worth nothing at all, and the final 13 points were the
        // ones that unlocked everything.
        //
        // Stating the gate here makes the completing bid price itself: MarginalValue across the floor
        // is now the whole jump from zero, so a task within a few points of viability becomes the
        // best buy on the planet instead of an ordinary one. Grist Nine, 2026-09-18: 3,621 battle
        // value was drawn into tasks over three weeks and handed back by the refund pass, one of them
        // 13 points short, and all of it ended up garrisoning.
        if (battleValue < MinimumViableAward) return 0.0;
        long within = Math.Min(battleValue, Saturation);
        double value = Importance * Shape(within);
        // Defend keeps a small positive slope above saturation so it can absorb battle value nothing
        // else wants. Every other task is flat there, and a flat task simply stops bidding.
        if (Kind == ForceTaskKind.Defend && battleValue > Saturation)
        {
            value += (battleValue - Saturation)
                * ForceAllocationConstants.DefenceEpsilonTailPerBattleValue;
        }
        return value;
    }

    /// <summary>Worth of adding this much battle value on top of what is already assigned.</summary>
    internal double MarginalValue(long battleValue) =>
        TotalValue(Assigned + battleValue) - TotalValue(Assigned);

    /// <summary>What a bid of this size is worth for RANKING, which is not always its margin.</summary>
    /// <remarks>
    /// Below <see cref="MinimumViableAward"/> every contribution is priced at the rate the COMPLETED
    /// task earns, not at its position on the curve. Without this, letting several regions share a
    /// task would create an incentive to under-fund it: recon's curve is concave, so the first squad
    /// of a three-squad sweep earns 58% of the value for 33% of the cost - 1.7 times the sweep's own
    /// rate. A region would buy one squad, the sweep would end the auction short of its floor, the
    /// refund pass would cancel it, and the same thing would happen again the next week.
    ///
    /// Pricing the whole run-up at the completed rate makes one squad and three squads score exactly
    /// alike, so nothing is gained by stopping short and the task fills at the rate it deserves.
    /// Tasks with no real floor are unaffected: their MinimumViableAward is 1, so the first point is
    /// priced at Importance x Shape(1), which is what the margin would have been anyway.
    /// </remarks>
    internal double BidValue(long battleValue)
    {
        if (battleValue <= 0L) return 0.0;
        if (!ShortOfViableAward) return MarginalValue(battleValue);

        // A bid that carries the task over its floor is worth the whole of what that unlocks, and
        // MarginalValue now says so on its own because TotalValue is zero below the floor.
        long toFloor = MinimumViableAward - Assigned;
        if (battleValue >= toFloor) return MarginalValue(battleValue);

        // A bid that does NOT reach the floor is worth nothing yet. Pricing it at nothing would mean
        // a task no single region can afford never starts at all - the defect this whole change set
        // exists to remove - so it is priced at the rate the completed task earns.
        //
        // That rate is flat, which is what stops a region gaining by underfunding: recon's curve is
        // concave, so priced marginally the first squad of a three-squad sweep would earn 58% of the
        // value for 33% of the cost, and a region would buy one squad every week and have it refunded
        // every week.
        return TotalValue(MinimumViableAward) / MinimumViableAward * battleValue;
    }

    /// <summary>
    /// Hands back everything this task holds, so the auction can treat it as never having been funded.
    /// </summary>
    /// <remarks>
    /// Only ever called on a task left short of its viable award, which is why the shared-threat ledger
    /// is not unwound here: Defend is the one task that books against it, and Defend's floor is 1, so
    /// it can never be the task being refunded.
    /// </remarks>
    internal void Refund()
    {
        Assigned = 0L;
        AssignedFromOutside = 0L;
        if (Exclusion != null && ReferenceEquals(Exclusion.Winner, this)) Exclusion.Winner = null;
    }

    /// <summary>
    /// Whether this task will still take battle value. Defend never refuses, because it is also the
    /// reserve sink; everything else stops at saturation.
    /// </summary>
    internal bool AcceptsMore =>
        !ExcludedByRival && (Kind == ForceTaskKind.Defend || Remaining > 0L);

    internal string DescribeTarget() =>
        $"{Objective?.Planet?.Name}/{Objective?.Name}";
}

/// <summary>
/// Tracks how much of each enemy presence is still unanswered, so that two friendly regions facing the
/// same neighbour cannot each garrison against the whole of it.
/// </summary>
/// <remarks>
/// This replaces the role played by FactionThreatAssessment.ExpectedAttackerCommitFraction, whose own
/// comment describes it as a hedge against exactly this over-count ("no attacker empties its own
/// regions to press one border"). Once the double-count is stated and removed, a blanket 50% discount
/// on every believed neighbour is no longer needed to compensate for it.
/// </remarks>
internal sealed class SharedThreatLedger
{
    private readonly Dictionary<RegionFaction, long> _unanswered = new();

    internal void Declare(RegionFaction threat, long battleValue)
    {
        if (threat == null || battleValue <= 0L) return;
        _unanswered[threat] = Math.Max(_unanswered.GetValueOrDefault(threat), battleValue);
    }

    internal long Unanswered(IReadOnlyList<RegionFaction> threats) =>
        threats == null ? 0L : threats.Sum(threat => _unanswered.GetValueOrDefault(threat));

    /// <summary>
    /// Books battle value against the threats a task answers, largest first. A garrison raised in one
    /// region reduces what its neighbour still needs against the same enemy.
    /// </summary>
    internal void Answer(IReadOnlyList<RegionFaction> threats, long battleValue)
    {
        if (threats == null || battleValue <= 0L) return;
        long remaining = battleValue;
        foreach (RegionFaction threat in threats
            .OrderByDescending(t => _unanswered.GetValueOrDefault(t)))
        {
            if (remaining <= 0L) break;
            long outstanding = _unanswered.GetValueOrDefault(threat);
            if (outstanding <= 0L) continue;
            long booked = Math.Min(outstanding, remaining);
            _unanswered[threat] = outstanding - booked;
            remaining -= booked;
        }
    }
}

/// <summary>Shared by a set of mutually exclusive tasks; the first to draw force wins it.</summary>
internal sealed class TaskExclusionGroup
{
    internal ForceTask Winner { get; set; }
}

/// <summary>One award made by the auction: this much battle value, from this region, to this task.</summary>
internal sealed record ForceTaskAward(ForceTask Task, RegionForceState Source, long BattleValue, int Hops);
