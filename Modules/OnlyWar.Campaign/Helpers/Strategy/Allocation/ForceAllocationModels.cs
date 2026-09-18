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

    /// <summary>Fraction of its saturation at which an assault becomes likely to carry the region.</summary>
    internal const double AssaultKnee = 0.7;

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

    // NOT IMPLEMENTED, recorded as a proposal: a faction that mounts no offensives - the planetary
    // defence force, which TurnOrderPlanner always plans with defensiveOnly set (PRD §4.24) - is being
    // garrisoned against at the same rate as one that attacks. On Grist Nine that cost the Orks over a
    // third of their army guarding a neighbour that never moves. Discounting it would free that force,
    // but it is a doctrine decision (may a faction know its enemy's posture?) rather than a defect, and
    // it invalidates the reinforcement behaviour two tests currently pin. Raise it deliberately.
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
    /// Best-of-n: each added observer is an independent draw, and is worth the chance it beats every
    /// observer already assigned. Steeply concave, which is why reconnaissance groups rather than
    /// either going alone or scaling indefinitely.
    /// </summary>
    internal static double BestOfN(double squads, double residualPerSquad)
    {
        if (squads <= 0.0) return 0.0;
        return 1.0 - Math.Pow(residualPerSquad, squads);
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
    internal long MinimumBidGiven(long sourceFloor) =>
        Assigned >= MinimumViableAward
            ? sourceFloor
            : Math.Max(sourceFloor, MinimumViableAward - Assigned);

    /// <summary>Cumulative fraction of Importance earned by a given battle value.</summary>
    internal Func<long, double> Shape { get; init; }

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
        if (Exclusion != null) Exclusion.Winner ??= this;
    }

    /// <summary>True when an alternative to this task has already taken the force.</summary>
    internal bool ExcludedByRival => Exclusion?.Winner != null && !ReferenceEquals(Exclusion.Winner, this);

    internal double TotalValue(long battleValue)
    {
        if (battleValue <= 0L || Saturation <= 0L) return 0.0;
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
