using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Operations.Missions.Recon;
using OnlyWar.Operations.Turns;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Strategy.Allocation;

/// <summary>
/// Builds the task list for one planet, keyed by objective rather than by region.
/// </summary>
/// <remarks>
/// Per-planet keying is not an optimisation, it is what makes shared saturation expressible. A
/// multi-region assault has to be ONE task with one saturation that several regions bid into, and two
/// regions garrisoning against the same neighbour have to be answering ONE threat. A per-region task
/// list can state neither.
///
/// Importance is normalised within each family and then weighted by doctrine
/// (ForceAllocationConstants). That is the loose form of the common currency: it makes families
/// comparable without requiring every task to state an expected battle-value swing over a horizon.
/// The seam is left so a family can later return a real swing instead of a normalised score.
/// </remarks>
internal sealed class ForceTaskBuilder
{
    private readonly FactionBehaviorRulesProfile _behaviorRules;
    private readonly ForceDoctrineWeights _doctrine;

    // Threat lookup walks a region's own presences plus every neighbour's and tests hostility on each.
    // It is asked for four times per region across one build (declaration, the worth normaliser,
    // defence, withdrawal), so it is resolved once per region and reused.
    private readonly Dictionary<RegionForceState, List<RegionFaction>> _threatsFacing = new();

    // What each region's defence task actually asks for, recorded as defence is built so the patrol
    // screen can be sized against the same figure rather than the unbounded want.
    private readonly Dictionary<RegionForceState, long> _defenceSaturations = new();

    internal ForceTaskBuilder(
        FactionBehaviorRulesProfile behaviorRules,
        ForceDoctrineWeights doctrine = null)
    {
        _behaviorRules = behaviorRules;
        _doctrine = doctrine ?? ForceDoctrineWeights.Balanced;
    }

    internal List<ForceTask> Build(
        Faction faction,
        Planet planet,
        IReadOnlyList<RegionForceState> states,
        IReadOnlyList<PotentialOffensive> offensives,
        SharedThreatLedger threats,
        bool defensiveOnly)
    {
        var tasks = new List<ForceTask>();
        DeclareThreats(faction, states, threats);

        // Enemies sharing one of our own regions that we have an offensive option against. In a
        // contested region, DEFENDING against the local enemy and ASSAULTING it are the same fight, and
        // counting the threat in full on both sides commits the army twice over. Both sides then
        // garrison against each other at full strength and neither has anything left to finish it -
        // which is exactly what Monody Prime produced on seed 2: fifteen successful assaults, every one
        // an InvaderFoothold, not a single region cleared, and the Orks holding 72% of their strength in
        // reserve while doing it.
        HashSet<RegionFaction> engagedLocally = defensiveOnly
            ? []
            : offensives
                .Where(offensive => offensive.TargetRegion.RegionFactionMap.ContainsKey(faction.Id))
                .Select(offensive => offensive.TargetFaction)
                .ToHashSet();

        AddDefenceTasks(faction, states, threats, engagedLocally, tasks);
        // Works are raised against a declared threat, not on spec. Without this gate a quiet NPC world
        // spends its whole garrison entrenching against nobody, which is what the old development pass
        // guarded with the same test before it ran at all.
        if (FactionThreatAssessment.HasPublicEnemyOnPlanet(faction, planet))
        {
            AddConstructionTasks(faction, states, threats, tasks);
        }
        AddPatrolTasks(faction, planet, states, tasks);
        AddWithdrawTasks(faction, states, threats, tasks);

        if (!defensiveOnly)
        {
            AddOffensiveTasks(faction, offensives, tasks);
            if (faction.GrowthType == GrowthType.Consumption)
            {
                AddConsumptionTasks(faction, states, tasks);
            }
        }
        else
        {
            // A purely defensive faction never assaults, but it does scout the ground massing on its
            // borders so its garrison sizing is not blind (PRD §4.24).
            AddOffensiveTasks(faction, offensives, tasks, reconOnly: true);
        }

        return tasks;
    }

    // ---- Defence -----------------------------------------------------------------------------

    /// <summary>
    /// Registers every enemy presence that any of this faction's regions can see, ONCE, at its believed
    /// strength.
    /// </summary>
    /// <remarks>
    /// Declaring each threat once is the whole point. The old per-region requirement summed every
    /// neighbour independently, so an enemy bordering three friendly regions was garrisoned against
    /// three times over, and FactionThreatAssessment.ExpectedAttackerCommitFraction existed as a
    /// blanket 50% hedge against that over-count. Stating the sharing exactly means the hedge is no
    /// longer compensating for a defect, so the threats are declared at full believed strength here.
    /// </remarks>
    private void DeclareThreats(
        Faction faction,
        IReadOnlyList<RegionForceState> states,
        SharedThreatLedger threats)
    {
        foreach (RegionFaction threat in states.SelectMany(state => ThreatsFacing(faction, state)).Distinct())
        {
            threats.Declare(threat, FactionThreatAssessment.CalculateDefenderBattleValue(threat));
        }
    }

    private List<RegionFaction> ThreatsFacing(Faction faction, RegionForceState state)
    {
        if (_threatsFacing.TryGetValue(state, out List<RegionFaction> cached)) return cached;
        List<RegionFaction> resolved = ResolveThreatsFacing(faction, state).ToList();
        _threatsFacing[state] = resolved;
        return resolved;
    }

    private static IEnumerable<RegionFaction> ResolveThreatsFacing(Faction faction, RegionForceState state)
    {
        Region region = state.RegionFaction.Region;
        IEnumerable<Region> ground = new[] { region }.Concat(region.GetAdjacentRegions());
        return ground
            .SelectMany(candidate => candidate.RegionFactionMap.Values)
            .Where(rf => rf.IsPublic
                && rf.PlanetFaction.Faction != faction
                && FactionRelationshipService.AreHostile(
                    faction, rf.PlanetFaction.Faction, region.Planet));
    }

    private void AddDefenceTasks(
        Faction faction,
        IReadOnlyList<RegionForceState> states,
        SharedThreatLedger threats,
        HashSet<RegionFaction> engagedLocally,
        List<ForceTask> tasks)
    {
        double maxWorth = Math.Max(1.0, states.Max(RegionWorth));
        double maxNeed = 1.0;
        foreach (RegionForceState state in states)
        {
            maxNeed = Math.Max(maxNeed, threats.Unanswered(ThreatsFacing(faction, state)));
        }

        foreach (RegionForceState state in states)
        {
            Region region = state.RegionFaction.Region;
            List<RegionFaction> facing = ThreatsFacing(faction, state);

            // ExpectedAttackerCommitFraction survives, but only for the half of its old job that the
            // ledger does NOT do. It carried two things at once: a hedge against the same neighbour
            // being garrisoned against by several regions, and the plain fact that an attacker keeps a
            // reserve and never presses one border with its whole strength. Declaring each threat once
            // replaces the first. Dropping the second as well made every defence about twice as hungry
            // as it should be, and defence then outbid every offensive on the planet.
            //
            // An enemy already standing in the region is not "may attack" - it is here, and committed
            // in full - so only the adjacent share is discounted.
            // A local enemy we are going to ENGAGE is answered by the engagement, so it is counted at
            // the same held-back-reserve rate as a neighbour rather than in full. A local enemy we have
            // no offensive option against is still counted whole: it is here, it is committed, and
            // standing still is the only answer available.
            // An enemy in a NEIGHBOURING region that our own forces also stand in is pinned where it
            // is: it is contested at home and not free to march on this region. It is dropped from the
            // requirement entirely rather than discounted - the force that pins it is already committed
            // to that region's own tasks, so counting it again here is the same double-commitment as
            // the local standoff below, one border removed.
            List<RegionFaction> pinned = facing
                .Where(threat => threat.Region != region
                    && threat.Region.RegionFactionMap.ContainsKey(faction.Id)).ToList();

            List<RegionFaction> localStandOff = facing
                .Where(threat => threat.Region == region && !engagedLocally.Contains(threat)).ToList();
            List<RegionFaction> discounted = facing
                .Where(threat => !pinned.Contains(threat)
                    && (threat.Region != region || engagedLocally.Contains(threat))).ToList();

            long need = threats.Unanswered(localStandOff)
                + (long)(threats.Unanswered(discounted)
                    * FactionThreatAssessment.ExpectedAttackerCommitFraction);

            // A deployed region holds a floor even with nothing visible, so it is never left empty by
            // an auction that found something more interesting to do.
            long floor = (long)(state.RegionFaction.GetDeployedStrength()
                * FactionThreatAssessment.MinimumDefensiveReserveFraction);
            long saturation = Math.Max(1L, Math.Max(need, floor));
            _defenceSaturations[state] = saturation;

            // What is lost if the region falls, which is NOT just the people on it. A thinly populated
            // frontier region with an army massing next door matters because losing it opens the way
            // in. Scoring defence on population alone made exactly that region worthless to hold - a
            // rear province would not spare a man for the border it sits behind, because the border had
            // fewer inhabitants than it did.
            double worth = Math.Max(RegionWorth(state) / maxWorth, need / maxNeed);

            tasks.Add(new ForceTask
            {
                Kind = ForceTaskKind.Defend,
                Objective = state.RegionFaction.Region,
                Home = state.RegionFaction,
                SharedThreats = facing,
                Importance = _doctrine.Defend * worth,
                Saturation = saturation,
                // Neighbours are asked only for the shortfall: the men already in the region hold it
                // too, whether or not they were numerous enough to bid for the job themselves.
                OutsideCapacity = Math.Max(
                    0L, saturation - state.RegionFaction.GetDeployedStrength()),
                // Holding the ground, plus the standing worth of buying time even when holding is out
                // of reach. Without the second term a small garrison facing a large threat sees a flat
                // low end on the hold curve, values defence at nearly nothing, and abandons the region.
                Shape = bv =>
                {
                    double fraction = bv / (double)saturation;
                    return (ForceValueCurves.HoldProbability(fraction)
                            + ForceAllocationConstants.DefenceDelayWeight * Math.Clamp(fraction, 0.0, 1.0))
                        / (1.0 + ForceAllocationConstants.DefenceDelayWeight);
                }
            });
        }
    }

    private static double RegionWorth(RegionForceState state) =>
        Math.Max(1.0, state.RegionFaction.Population);

    // ---- Withdraw ----------------------------------------------------------------------------

    /// <summary>
    /// Pulling out of ground that cannot be held. This is the alternative the defence curve needs in
    /// order to be tuned honestly: without it, a region facing overwhelming force has only the choice
    /// between dying in place and doing nothing, and DefenceDelayWeight is a hedge rather than a
    /// doctrine.
    /// </summary>
    private void AddWithdrawTasks(
        Faction faction,
        IReadOnlyList<RegionForceState> states,
        SharedThreatLedger threats,
        List<ForceTask> tasks)
    {
        // Which regions are judged lost is settled BEFORE any refuge is chosen, so that ground being
        // abandoned cannot also serve as somewhere to abandon ground to. Observed on Grist Nine
        // 2026-09-16: two regions fell back on a third, which then fell back itself in the same pass,
        // and the Imperial line dissolved rather than shortening.
        var doomed = new HashSet<Region>();
        var holdOdds = new Dictionary<RegionForceState, double>();
        foreach (RegionForceState state in states)
        {
            double odds = HoldOdds(faction, state, states, threats);
            holdOdds[state] = odds;
            if (odds < ForceAllocationConstants.WithdrawHoldThreshold
                && state.SpareTroops > 0L)
            {
                doomed.Add(state.RegionFaction.Region);
            }
        }

        foreach (RegionForceState state in states)
        {
            long available = state.SpareTroops;
            if (available <= 0L) continue;

            List<RegionFaction> facing = ThreatsFacing(faction, state);
            long need = threats.Unanswered(facing);
            if (need <= 0L) continue;

            double bestCase = holdOdds[state];
            if (bestCase >= ForceAllocationConstants.WithdrawHoldThreshold) continue;

            // Falling back only counts as falling back if there is somewhere to fall back TO. A region
            // with no surviving neighbour stands and fights, which is what the delay term in the
            // defence curve is for.
            Region refuge = ChooseRefuge(faction, state, states, doomed);
            if (refuge == null) continue;

            // A rearguard is for buying time while falling back, not for abandoning ground. Leaving one
            // behind unconditionally meant a region could never be given up cleanly: any surviving
            // military strength keeps the faction PUBLIC, and
            // StrategicCombatResolver.HideBrokenCivilianDefender only lets the civilians go to ground
            // when MilitaryStrength reaches exactly zero. So the token force guaranteed the region
            // stayed contested for ever, however hopeless it was.
            //
            // The rearguard therefore fades as the position becomes untenable: a region that might yet
            // be held keeps one, a region that is simply lost evacuates whole.
            double rearguard = ForceAllocationConstants.WithdrawRearguardFraction
                * (bestCase / ForceAllocationConstants.WithdrawHoldThreshold);
            long saturation = Math.Max(1L, (long)(available * (1.0 - Math.Clamp(rearguard, 0.0, 1.0))));

            tasks.Add(new ForceTask
            {
                Kind = ForceTaskKind.Withdraw,
                Objective = state.RegionFaction.Region,
                Home = state.RegionFaction,
                Destination = refuge,
                // Worth what it saves, scaled by how certainly the ground is lost.
                Importance = _doctrine.Withdraw * (1.0 - bestCase),
                Saturation = saturation,
                Shape = bv => ForceValueCurves.Linear(bv / (double)saturation)
            });
        }
    }

    /// <summary>
    /// How likely this region is to be held by the faction - not by the region's own troops alone.
    /// Judging it on the garrison in place made every thin frontier region abandon ground its
    /// neighbours were willing and able to reinforce, because the retreat was scored as though help
    /// did not exist.
    /// </summary>
    private double HoldOdds(
        Faction faction,
        RegionForceState state,
        IReadOnlyList<RegionForceState> states,
        SharedThreatLedger threats)
    {
        long need = threats.Unanswered(ThreatsFacing(faction, state));
        if (need <= 0L) return 1.0;

        long reachable = state.SpareTroops + state.RegionFaction.Region.GetAdjacentRegions()
            .Select(region => states.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(neighbour => neighbour != null)
            .Sum(neighbour => neighbour.SpareTroops);
        return ForceValueCurves.HoldProbability(reachable / (double)need);
    }

    /// <summary>The adjacent friendly region with the least unanswered threat that is not itself lost.</summary>
    private static Region ChooseRefuge(
        Faction faction,
        RegionForceState source,
        IReadOnlyList<RegionForceState> states,
        HashSet<Region> doomed)
    {
        return source.RegionFaction.Region.GetAdjacentRegions()
            .Where(region => !doomed.Contains(region))
            .Select(region => states.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(state => state != null)
            .OrderBy(state => FactionThreatAssessment.VisibleAdjacentEnemyMilitary(
                faction, state.RegionFaction.Region))
            .Select(state => state.RegionFaction.Region)
            .FirstOrDefault();
    }

    // ---- Offensives --------------------------------------------------------------------------

    private void AddOffensiveTasks(
        Faction faction,
        IReadOnlyList<PotentialOffensive> offensives,
        List<ForceTask> tasks,
        bool reconOnly = false)
    {
        if (offensives.Count == 0) return;

        double maxReward = Math.Max(1.0, offensives.Max(o => o.Reward));
        long scoutSquad = Math.Max(1L, FactionReconPatrolPlanner.CheapestScoutSquadBattleValue(faction));

        foreach (PotentialOffensive offensive in offensives)
        {
            bool wellKnown = FactionOffensiveEvaluator.IsWellReconnoitred(offensive, faction.Id)
                || offensive.TargetRegion.RegionFactionMap.ContainsKey(faction.Id);

            if (!wellKnown)
            {
                // Size the sweep to be ACTIONABLE THIS TURN rather than sending a flat three squads.
                //
                // Margins pool within a turn - TurnIntelligenceLedger aggregates every participating
                // squad's every daily margin per region - and ReconIntelligenceRules.AwarenessDelta
                // takes the square root of that total. So the squad count decides whether a week of
                // scouting crosses ReconIntelThreshold at all, and a sweep that falls short buys
                // nothing: awareness decays 25% before the next attempt.
                //
                // Grist Nine, 2026-09-17. Epsilon pooled 3.00 + 3.41 = 6.41 from two squads, cleared
                // the threshold in one week and was assaulted immediately. Kappa pooled 2.83, fell
                // short, and needed a second week. Iota took three separate sweeps that returned
                // 0.04, -0.36 and -0.35 and never became actionable at all.
                //
                // This also replaces the old "scout what you know least about" factor, which was
                // backwards: it scored a region NEARER the threshold LOWER, so the planner kept
                // abandoning half-scouted ground to start somewhere new. Sizing to the remaining gap
                // makes a nearly-finished region cheap instead, so it wins on value per battle value
                // and gets completed.
                long saturation = scoutSquad
                    * ReconSquadsToCrossThreshold(
                        offensive.TargetRegion.GetFactionRegionAwareness(faction.Id));
                tasks.Add(new ForceTask
                {
                    Kind = ForceTaskKind.Recon,
                    Objective = offensive.TargetRegion,
                    Offensive = offensive,
                    // Worth what scouting it would unlock. How far from actionable the region is lives
                    // in the SATURATION now, not here.
                    Importance = _doctrine.Recon * (offensive.Reward / maxReward),
                    Saturation = saturation,
                    // Anything less than the full sweep is wasted: a pooled margin short of the
                    // threshold leaves the region unactionable and the awareness decays away before
                    // the next attempt. Send enough squads or send none.
                    MinimumViableAward = saturation,
                    // Concave, and it is the one task that earns it. A recon order fans into one
                    // independent roll per squad and the margins are POOLED under a square root
                    // (ReconIntelligenceRules.AwarenessDelta), so the sqrt here is the mechanic rather
                    // than a stand-in for one. The per-battle-value spike that made Concave wrong for
                    // feeding and patrolling is bounded here because the saturation is only a few
                    // squads, so the smallest bid is already a large fraction of it.
                    Shape = bv => ForceValueCurves.Concave(bv / (double)saturation)
                });
                // Attacking ground nobody has scouted stays off the table. The pessimistic prior in
                // CautiousDefenderEstimate would price it out anyway, but stating it keeps the
                // recon-first rule legible.
                continue;
            }

            // A defensive posture launches no offensive of its own - but retaking ground the enemy has
            // walked away from is not an offensive, it is restoring the line. A planetary defence force
            // that watches an emptied region and does nothing is not defending; it is conceding.
            //
            // The gate is that the region reads as genuinely EMPTY. Because the estimate is coarsened
            // upward, an unscouted region never reads zero, so this cannot become a blind lunge: the
            // defender has to have watched the ground it walks into.
            bool retakingOpenGround = offensive.EstimatedDefenderBattleValue <= 0L;
            if (reconOnly && !retakingOpenGround) continue;

            double ratio = FactionCapabilities.GeneratesInvasions(faction)
                ? (_behaviorRules?.DefendedLandingRatio ?? 2.0)
                : FactionOffensiveEvaluator.OffensiveForceRatioThreshold;
            // Floored at one squad's price. Force generation cannot honour a smaller request, so an
            // assault sized off a near-dead garrison would be allocated a few points of battle value
            // and then silently produce no force at all, and the target would never be attacked.
            long assaultSaturation = Math.Max(
                Math.Max(1L, faction.MinimumForceRequest),
                (long)Math.Ceiling(offensive.EstimatedDefenderBattleValue * ratio));
            // Storming a region and raiding it are alternatives for the same week, not a pair.
            TaskExclusionGroup exclusion = new();
            tasks.Add(new ForceTask
            {
                Kind = ForceTaskKind.Assault,
                Objective = offensive.TargetRegion,
                Offensive = offensive,
                Exclusion = exclusion,
                Importance = _doctrine.Assault
                    * Math.Min(1.0, FactionOffensiveEvaluator.RewardRiskScore(offensive)),
                Saturation = assaultSaturation,
                // The LAUNCH THRESHOLD, not merely a generatable squad. ForceTaskCommitter refuses an
                // assault funded below its force ratio - correctly, since a fraction of the force needed
                // to carry a region is a defeat rather than a smaller attack - so if the auction is
                // allowed to part-fund one, that battle value is debited and then thrown away.
                //
                // Grist Nine, 2026-09-17: the Orks awarded 2,376 to an assault needing 7,000, the commit
                // refused it, and half the army evaporated. They issued ONE order that week.
                MinimumViableAward = Math.Max(
                    Math.Max(1L, faction.MinimumForceRequest),
                    (long)Math.Ceiling(assaultSaturation * ForceAllocationConstants.AssaultKnee)),
                // An assault is an all-or-nothing commitment: half the force needed to carry a region
                // does not half-take it. The knee is what batch bidding exists to find.
                Shape = bv => ForceValueCurves.Threshold(
                    bv / (double)assaultSaturation, ForceAllocationConstants.AssaultKnee)
            });

            // A raid is what a faction does when it cannot take the ground but can still hurt whoever
            // holds it - the middle option between an unaffordable assault and doing nothing, which is
            // exactly what a frozen region lacked. Assault and Raid are now offered TOGETHER and priced
            // against each other, instead of the old IsWinnable branch choosing between them from
            // whatever force happened to be spare.
            if (!FactionCapabilities.GeneratesInvasions(faction)
                && offensive.DefenderBattleValue > 0L)
            {
                long raidSaturation = Math.Max(
                    FactionOffensiveEvaluator.MinimumRaidBattleValue,
                    (long)Math.Ceiling(offensive.EstimatedDefenderBattleValue * 0.5
                        / FactionOffensiveEvaluator.RaidCommitFraction));
                tasks.Add(new ForceTask
                {
                    Kind = ForceTaskKind.Raid,
                    Objective = offensive.TargetRegion,
                    Offensive = offensive,
                    Exclusion = exclusion,
                    Importance = _doctrine.Raid
                        * Math.Min(1.0, FactionOffensiveEvaluator.RaidUtilityAt(offensive, raidSaturation)),
                    Saturation = raidSaturation,
                    MinimumViableAward = Math.Max(
                        Math.Max(1L, faction.MinimumForceRequest),
                        FactionOffensiveEvaluator.MinimumRaidBattleValue),
                    Shape = bv => ForceValueCurves.Linear(bv / (double)raidSaturation)
                });
            }
        }
    }

    /// <summary>
    /// Scout squads needed for one week's pooled sweep to carry a region over the reconnaissance
    /// threshold from where its awareness stands now.
    /// </summary>
    /// <remarks>
    /// Inverts ReconIntelligenceRules.AwarenessDelta, which is
    /// <c>AwarenessGainCoefficient * sqrt(pooled margin)</c>. To gain <c>g</c> awareness in one turn
    /// the sweep must pool <c>(g / coefficient)^2</c>, and each squad contributes about
    /// <see cref="ForceAllocationConstants.ExpectedSweepMarginAtNormal"/> adjusted for the faction's
    /// recon aggression.
    ///
    /// The square is why this matters: closing the last 0.4 of the gap costs a single squad, while
    /// starting from nothing costs four times as much. A flat squad count could only be wrong in one
    /// direction or the other.
    /// </remarks>
    private int ReconSquadsToCrossThreshold(float currentAwareness)
    {
        double gap = Math.Max(
            0.0, FactionStrategyPlanningConstants.ReconIntelThreshold - currentAwareness);
        if (gap <= 0.0) return 1;

        double requiredPooledMargin = Math.Pow(
            gap / ReconIntelligenceRules.AwarenessGainCoefficient, 2.0);
        // Past the point where AwarenessDelta clamps, additional squads buy nothing at all.
        double mostThatCanHelp = Math.Pow(
            ReconIntelligenceRules.MaximumAwarenessDelta
                / ReconIntelligenceRules.AwarenessGainCoefficient,
            2.0);
        requiredPooledMargin = Math.Min(requiredPooledMargin, mostThatCanHelp);

        // What a squad is expected to bring back depends on how boldly this faction scouts, so the
        // squad count has to be read against the doctrine rather than a flat figure. Floored well
        // above zero: a faction scouting at Avoid learns very little per squad, and without a floor the
        // arithmetic would demand an implausible sweep and then be clamped anyway.
        int steps = (int)_doctrine.ReconAggression - (int)Aggression.Normal;
        double expectedPerSquad = Math.Max(
            0.25,
            ForceAllocationConstants.ExpectedSweepMarginAtNormal
                + steps * ForceAllocationConstants.MarginPerAggressionStep);

        int squads = (int)Math.Ceiling(requiredPooledMargin / expectedPerSquad);
        return Math.Clamp(squads, 1, ForceAllocationConstants.MaxReconSquadsPerSweep);
    }

    // ---- Patrol ------------------------------------------------------------------------------

    private void AddPatrolTasks(
        Faction faction,
        Planet planet,
        IReadOnlyList<RegionForceState> states,
        List<ForceTask> tasks)
    {
        // A screen is a scout tasking (ForceCompositionProfile.ScoutPatrol). A faction with no scout
        // formation can never field one, so allocating battle value to it would simply lose that value:
        // the auction debits the region, the generator produces nothing, and the budget is gone.
        long scoutSquad = FactionReconPatrolPlanner.CheapestScoutSquadBattleValue(faction);
        if (scoutSquad <= 0L) return;

        foreach (RegionForceState state in states)
        {
            double fraction = FactionReconPatrolPlanner.CalculatePatrolFraction(faction, planet, state);
            if (fraction <= 0.0) continue;

            // The fraction is applied to strength BEYOND the defensive requirement, which is what it
            // was authored against: CalculatePatrolFraction's values came from the old planner, where
            // they were a share of SpareTroops - and spare then meant what survived the defensive
            // reserve. SpareTroops now means a region's whole strength, so applying the same fraction
            // to it silently multiplied every screen by three to five times.
            //
            // Grist Nine, 2026-09-16: the Orks posted roughly a thousand battle value of screen every
            // week, about a fifth of their army, without anyone choosing to.
            // Measured against the DEFENCE TASK'S saturation, not RequiredDefensiveBattleValue. The
            // latter is the unbounded want - on Grist Nine it read 18,550 against an army of 4,818 -
            // so subtracting it left every region with nothing screenable and the Orks posted no
            // screen at all. The defence task's saturation is the same want already reconciled against
            // the shared threat ledger and the minimum reserve floor.
            long defenceNeed = _defenceSaturations.GetValueOrDefault(state, 0L);
            long screenable = Math.Max(
                0L, state.RegionFaction.GetDeployedStrength() - defenceNeed);
            long saturation = (long)(screenable * fraction);
            if (saturation < scoutSquad) continue;

            tasks.Add(new ForceTask
            {
                Kind = ForceTaskKind.Patrol,
                Objective = state.RegionFaction.Region,
                Home = state.RegionFaction,
                // The fraction the old code spent is reused as the importance: it already encodes
                // "local enemy, adjacent enemy, or works worth screening" in one number.
                Importance = _doctrine.Patrol * Math.Min(1.0, fraction / 0.20),
                Saturation = saturation,
                // A screen is built from ScoutPatrol formations, so it too costs a scout squad or
                // produces nothing.
                MinimumViableAward = scoutSquad,
                Shape = bv => ForceValueCurves.Linear(bv / (double)saturation)
            });
        }
    }

    // ---- Construction ------------------------------------------------------------------------

    /// <summary>
    /// Works, priced against the ground they stand on rather than against each other.
    /// </summary>
    /// <remarks>
    /// Why construction was outbidding everything despite the smallest weight but one, which is not
    /// obvious from the weights at all. Bids rank on value PER BATTLE VALUE, which is
    /// <c>Importance / Saturation</c> - so a task's attractiveness is inversely proportional to its
    /// size, and construction is by far the smallest task in the game:
    ///
    ///   first level of works  0.40 / 200   = 2.0e-3      <- wins
    ///   assault at its knee   0.85 * 0.5 / 350 = 1.2e-3
    ///   garrison a region     1.00 * 0.26 / 1000 = 2.6e-4
    ///
    /// A weight of 0.40 over a 200-point saturation beats a weight of 1.00 over a 1,000-point one. The
    /// build economy is logarithmic, so a FIRST level is genuinely cheap, and the model was reading
    /// "cheap" as "valuable" while the thing it bought stayed small: one level of entrenchment is
    /// roughly a fifteen percent improvement to a defence, not a defence.
    ///
    /// So importance is now the value DELIVERED, on the same scale the other families use. It is
    /// anchored to what defending the region is worth and multiplied down by how much of that a level
    /// of works actually adds - which also means works are worth less on ground worth less, and a
    /// sensor over a quiet backwater stops outbidding the garrison of a contested border.
    /// </remarks>
    private void AddConstructionTasks(
        Faction faction,
        IReadOnlyList<RegionForceState> states,
        SharedThreatLedger threats,
        List<ForceTask> tasks)
    {
        double maxWorth = Math.Max(1.0, states.Max(RegionWorth));
        double maxNeed = Math.Max(1.0, states.Max(
            state => (double)threats.Unanswered(ThreatsFacing(faction, state))));
        var raw = new List<(RegionForceState State, DefenseType Type, long Cost, double Benefit, double Amount)>();
        foreach (RegionForceState state in states)
        {
            raw.AddRange(FactionDevelopmentPlanner.EnumerateDevelopmentOptions(faction, state));
        }
        if (raw.Count == 0) return;

        foreach ((RegionForceState state, DefenseType type, long cost, double benefit, double amount) in raw)
        {
            long saturation = Math.Max(1L, (long)Math.Ceiling(amount * cost * 100L));
            tasks.Add(new ForceTask
            {
                Kind = ForceTaskKind.Construct,
                Objective = state.RegionFaction.Region,
                Home = state.RegionFaction,
                Construction = type,
                ConstructionAmount = amount,
                // Worth of the ground, times the share of its defence this level of works adds. Cost
                // still decides how far the battle value goes, through the saturation; benefit and the
                // ground's worth decide whether the build is worth bidding for at all.
                Importance = _doctrine.Construct
                    * Math.Max(RegionWorth(state) / maxWorth,
                        threats.Unanswered(ThreatsFacing(faction, state)) / maxNeed)
                    * Math.Min(1.0, benefit / ForceAllocationConstants.ReferenceConstructionBenefit)
                    * ForceAllocationConstants.ConstructionLevelShareOfDefence,
                Saturation = saturation,
                // Works are bought by the point, so value accrues linearly to the next whole level.
                Shape = bv => Math.Clamp(bv / (double)saturation, 0.0, 1.0)
            });
        }
    }

    // ---- Consumption -------------------------------------------------------------------------

    private void AddConsumptionTasks(
        Faction faction,
        IReadOnlyList<RegionForceState> states,
        List<ForceTask> tasks)
    {
        foreach (RegionForceState state in states)
        {
            long available = state.RegionFaction.GetDeployedStrength();
            if (available <= 0L) continue;

            (Region destination, long movers) =
                ConsumptionTurnProcessor.PlanExpansion(state.RegionFaction, available);
            if (destination != null && movers > 0L)
            {
                tasks.Add(new ForceTask
                {
                    Kind = ForceTaskKind.ConsumptionSpread,
                    Objective = state.RegionFaction.Region,
                    Home = state.RegionFaction,
                    Destination = destination,
                    Importance = _doctrine.Spread,
                    Saturation = movers,
                    Shape = bv => ForceValueCurves.Linear(bv / (double)movers)
                });
            }

            // Feeding is no longer the terminal sink that swallowed whatever survived every other
            // policy - it carries an importance and competes - but its saturation is the swarm's own
            // strength rather than the ground's biomass. Sizing it off carrying capacity alone meant a
            // swarm on ground with none simply never ate, which is a balance decision the allocator
            // should not be making on its own.
            long feedSaturation = Math.Max(1L, available);
            tasks.Add(new ForceTask
            {
                Kind = ForceTaskKind.Feed,
                Objective = state.RegionFaction.Region,
                Home = state.RegionFaction,
                Importance = _doctrine.Feed,
                Saturation = feedSaturation,
                Shape = bv => ForceValueCurves.Linear(bv / (double)feedSaturation)
            });
        }
    }
}
