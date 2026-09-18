using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Strategy.Allocation;

/// <summary>
/// Allocates a faction's battle value across one planet's tasks by marginal value, replacing the fixed
/// priority ladder that used to take the defensive reserve off the top.
/// </summary>
/// <remarks>
/// Three properties make this work, and none of them are optional:
///
/// 1. BATTLE VALUE IS THE CURRENCY and bids rank by marginal value PER battle value. Ranking by a
///    bid's total value would let a coarse region's larger bid outrank a fine region's smaller one on
///    size alone, which is "the strong claim first" arrived at from a new direction.
///
/// 2. TASKS BID IN BATCHES. Greedy on single increments is correct only for concave curves, and both
///    the defence and assault curves have thresholds - a task whose first increment is worthless would
///    never start. Each pairing offers the batch that maximises total/battleValue.
///
/// 3. RE-SCORING IS LAZY. Shared saturation means one award changes every task answering the same
///    threat, so a full re-score per award is order 10^4-10^5 scoring calls per faction, per planet,
///    per turn - far too slow beside sector generation. Popping the top bid, re-scoring only it, and
///    pushing it back when it falls below the new top is exact for concave curves and approximate for
///    the defence threshold. That approximation is the deliberate trade.
/// </remarks>
internal sealed class ForceAllocationAuction
{
    // A backstop only. The loop terminates because every award either drains a region's budget or fills
    // a task, and both are finite.
    private const int MaxAuctionIterations = 4096;

    private readonly SharedThreatLedger _threats;

    internal ForceAllocationAuction(SharedThreatLedger threats)
    {
        _threats = threats ?? new SharedThreatLedger();
    }

    internal List<ForceTaskAward> Run(
        Faction faction,
        Planet planet,
        IReadOnlyList<ForceTask> tasks,
        IReadOnlyList<RegionForceState> states)
    {
        var awards = new List<ForceTaskAward>();
        if (tasks.Count == 0 || states.Count == 0) return awards;

        Dictionary<Region, RegionForceState> stateByRegion = states
            .GroupBy(state => state.RegionFaction.Region)
            .ToDictionary(group => group.Key, group => group.First());

        // Which regions may bid on which task, and the price each pays to get there. The discount is
        // constant for a (task, source) pair, so it is computed ONCE here rather than on every bid
        // evaluation - it walks region adjacency and tests faction hostility, and doing that inside the
        // scoring loop made full sector generation several times slower on its own.
        var queue = new PriorityQueue<Pairing, double>();
        foreach (ForceTask task in tasks)
        {
            foreach ((RegionForceState source, int hops) in EligibleBidders(task, stateByRegion))
            {
                double discount = Math.Pow(ForceAllocationConstants.TransitDiscountPerHop, hops)
                    / (1.0 + ForceAllocationConstants.SourceFlexibilityPenalty
                        * ReachableEnemyTargets(source, task.Objective));
                Pairing pairing = new(task, source, hops, discount);
                Bid bid = BestBid(pairing);
                if (bid.BattleValue > 0L) queue.Enqueue(pairing, -bid.ScorePerBattleValue);
            }
        }

        for (int i = 0; i < MaxAuctionIterations && queue.Count > 0; i++)
        {
            Pairing pairing = queue.Dequeue();
            (ForceTask task, RegionForceState source, int hops, _) = pairing;
            Bid bid = BestBid(pairing);
            if (bid.BattleValue <= 0L) continue;

            // Once the best bid on the planet is down in the reserve sink's epsilon tail, every
            // remaining pairing scores within rounding of every other. Auctioning that plateau is both
            // pointless and slow: the lazy queue spends its time popping, re-scoring and pushing back
            // bids that are all equally worthless, which is where full sector generation lost most of
            // its time. Stop, and hand each region's leftover straight to its own garrison.
            if (bid.ScorePerBattleValue <= EpsilonScoreFloor) break;

            // Lazy re-scoring: if this pairing is no longer the best, put it back at its real score and
            // let whatever now leads be examined instead.
            if (queue.TryPeek(out _, out double nextPriority)
                && -bid.ScorePerBattleValue > nextPriority)
            {
                queue.Enqueue(pairing, -bid.ScorePerBattleValue);
                continue;
            }

            task.Award(bid.BattleValue, fromOutside: hops > 0);
            _threats.Answer(task.SharedThreats, bid.BattleValue);
            source.SpareTroops = Math.Max(0L, source.SpareTroops - bid.BattleValue);
            awards.Add(new ForceTaskAward(task, source, bid.BattleValue, hops));

            GameLog.Trace(() =>
                $"AI allocate {faction.Name}/{planet.Name}: {task.Kind} {task.DescribeTarget()} "
                + $"<- {source.RegionFaction.Region.Name} bv={bid.BattleValue} hops={hops}, "
                + $"perBv={bid.ScorePerBattleValue:E2}, assigned={task.Assigned}/{task.Saturation}");

            if (source.SpareTroops > 0L && task.AcceptsMore)
            {
                Bid next = BestBid(pairing);
                if (next.BattleValue > 0L)
                {
                    queue.Enqueue(pairing, -next.ScorePerBattleValue);
                }
            }
        }

        // The reserve sink, applied in one pass rather than auctioned a quantum at a time. Force that
        // nothing else wanted stays home and holds the ground it is standing on.
        foreach (RegionForceState state in states)
        {
            if (state.SpareTroops <= 0L) continue;
            ForceTask garrison = tasks.FirstOrDefault(task =>
                task.Kind == ForceTaskKind.Defend
                && ReferenceEquals(task.Home, state.RegionFaction));
            if (garrison == null) continue;

            long remainder = state.SpareTroops;
            garrison.Award(remainder, fromOutside: false);
            state.SpareTroops = 0L;
            awards.Add(new ForceTaskAward(garrison, state, remainder, 0));
        }

        return awards;
    }

    /// <summary>
    /// Score at or below which a bid is indistinguishable from the reserve sink. Real work scores
    /// several orders of magnitude above it.
    /// </summary>
    private const double EpsilonScoreFloor =
        ForceAllocationConstants.DefenceEpsilonTailPerBattleValue * 10.0;

    /// <summary>One region's standing offer to one task, with its price already resolved.</summary>
    private readonly record struct Pairing(
        ForceTask Task, RegionForceState Source, int Hops, double Discount);

    private readonly record struct Bid(long BattleValue, double ScorePerBattleValue);

    /// <summary>
    /// Enemy-held regions this source could have acted against instead of this one. A region bordering
    /// several enemies is worth more left where it is, because it is the only region that can answer
    /// any of them; a cornered region loses nothing by committing. This is the judgement
    /// FactionStagingPlanner used to make by ordering staging regions, expressed as a price so it
    /// competes rather than pre-empting.
    /// </summary>
    private static int ReachableEnemyTargets(RegionForceState source, Region currentTarget)
    {
        Faction faction = source.RegionFaction.PlanetFaction.Faction;
        Region region = source.RegionFaction.Region;
        return region.GetAdjacentRegions()
            .Where(adjacent => adjacent != currentTarget)
            .Count(adjacent => adjacent.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic && FactionRelationshipService.AreHostile(
                    faction, rf.PlanetFaction.Faction, adjacent.Planet)));
    }

    /// <summary>
    /// The batch of battle value from this region that earns the most per point, given what the task
    /// already holds.
    /// </summary>
    private Bid BestBid(Pairing pairing)
    {
        (ForceTask task, RegionForceState source, int hops, double discount) = pairing;
        long budget = source.SpareTroops;
        // A bid must be big enough for the TASK to produce something, not merely big enough for the
        // faction to build its cheapest squad of any kind. A bid below that is burned battle value.
        long floor = task.MinimumBidGiven(Math.Max(1L, source.MinimumBid));
        if (budget < floor || !task.AcceptsMore) return default;

        // The reserve sink absorbs from its OWN region only. Defend accepts battle value past its
        // saturation so that force nothing else wants still has somewhere to go, but that tail must not
        // pull reinforcements across a border: a neighbour kept marching troops into a garrison that
        // was already at full strength, for a marginal value of almost nothing.
        long outsideRoom = hops > 0 ? task.RemainingFromOutside : long.MaxValue;
        if (outsideRoom <= 0L) return default;
        budget = Math.Min(budget, outsideRoom);
        if (budget < floor) return default;

        Bid best = default;
        foreach (long candidate in CandidateAmounts(task, source, budget, floor))
        {
            double marginal = task.MarginalValue(candidate) * discount;
            if (marginal <= 0.0) continue;
            double perBattleValue = marginal / candidate;
            if (perBattleValue > best.ScorePerBattleValue)
            {
                best = new Bid(candidate, perBattleValue);
            }
        }
        return best;
    }

    /// <summary>
    /// The battle values this region offers a task. The quantum only PROPOSES these amounts; the curve
    /// is continuous and is evaluated at the exact figure, so a coarse region samples the same curve
    /// less densely rather than being confined to a grid.
    /// </summary>
    private IEnumerable<long> CandidateAmounts(
        ForceTask task,
        RegionForceState source,
        long budget,
        long floor)
    {
        var amounts = new SortedSet<long>();
        long quantum = Math.Max(floor, source.Quantum);

        for (long amount = quantum; amount <= budget; amount += quantum)
        {
            amounts.Add(amount);
        }
        // The remainder below one whole quantum is still worth offering: the quantum governs the
        // increment, not the final tranche.
        amounts.Add(budget);

        // Clamped to what the task can still absorb, so a task needing 37 more takes 37 from a coarse
        // region rather than a whole 100 and wasting the rest.
        long remaining = task.Remaining;
        if (remaining > 0L) amounts.Add(Math.Min(remaining, budget));

        // The threshold of a non-concave curve is always a candidate whatever the grid. Without this a
        // coarse region can step over the knee of a defence or an assault and badly over- or
        // under-commit - the one case where a coarse quantum causes a wrong decision and not merely a
        // rough one.
        foreach (double kneeFraction in KneeFractions(task))
        {
            long knee = (long)Math.Ceiling(task.Saturation * kneeFraction) - task.Assigned;
            if (knee > 0L && knee <= budget) amounts.Add(knee);
        }

        return amounts.Where(amount => amount >= floor && amount <= budget && amount > 0L);
    }

    private static IEnumerable<double> KneeFractions(ForceTask task)
    {
        switch (task.Kind)
        {
            case ForceTaskKind.Defend:
                yield return ForceAllocationConstants.DefenceHoldKnee;
                yield return 1.0;
                break;
            case ForceTaskKind.Assault:
                yield return ForceAllocationConstants.AssaultKnee;
                yield return 1.0;
                break;
            default:
                yield return 1.0;
                break;
        }
    }

    /// <summary>
    /// Regions that may stage this task, with the hop count each pays. A local task takes bids only
    /// from its own region; everything else accepts them from within MaxBidHops, which is what makes a
    /// multi-region assault one task rather than several and what turns troop movement into an
    /// ordinary outcome of bidding rather than a separate planning pass.
    /// </summary>
    private static List<(RegionForceState Source, int Hops)> EligibleBidders(
        ForceTask task,
        Dictionary<Region, RegionForceState> stateByRegion)
    {
        var bidders = new List<(RegionForceState, int)>();
        if (task.Objective == null) return bidders;

        int reach = BidReach(task.Kind);
        if (reach == 0)
        {
            if (task.Home != null && stateByRegion.TryGetValue(task.Home.Region, out RegionForceState home))
            {
                bidders.Add((home, 0));
            }
            return bidders;
        }

        // Breadth-first over region adjacency, so a region two borders away bids at two hops' discount.
        var seen = new HashSet<Region> { task.Objective };
        var frontier = new List<Region> { task.Objective };
        for (int hops = 0; hops <= reach && frontier.Count > 0; hops++)
        {
            var next = new List<Region>();
            foreach (Region region in frontier)
            {
                if (stateByRegion.TryGetValue(region, out RegionForceState state))
                {
                    // Staging out of the region being attacked is the "already standing on the ground"
                    // case and costs nothing; everything else pays for the distance.
                    bidders.Add((state, hops));
                }
                foreach (Region adjacent in region.GetAdjacentRegions())
                {
                    if (seen.Add(adjacent)) next.Add(adjacent);
                }
            }
            frontier = next;
        }
        return bidders;
    }

    /// <summary>
    /// How many region hops away a task will take bids from.
    /// </summary>
    /// <remarks>
    /// Defence reaches one hop, and that is where troop MOVEMENT comes from. A neighbour winning a
    /// share of a region's defence is a garrison reinforcement, and the committer moves the strength
    /// to make it real - so the two hand-written reinforcement passes that used to run before every
    /// offensive are replaced by ordinary bidding, priced against everything else those troops could
    /// have done. One hop rather than two because that is the range the old passes worked at, and
    /// because defence tasks are the most numerous kind.
    ///
    /// Work that is physically tied to a region - screening it, building on it, grazing it, pulling
    /// out of it - takes no outside bids at all.
    /// </remarks>
    private static int BidReach(ForceTaskKind kind) => kind switch
    {
        ForceTaskKind.Patrol
            or ForceTaskKind.Construct
            or ForceTaskKind.Feed
            or ForceTaskKind.Withdraw
            or ForceTaskKind.ConsumptionSpread => 0,
        ForceTaskKind.Defend => 1,
        _ => ForceAllocationConstants.MaxBidHops
    };
}
