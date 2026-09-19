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
        var queue = new PriorityQueue<Pairing, (double Score, double Tie)>();
        foreach (ForceTask task in tasks)
        {
            List<(RegionForceState Source, int Hops)> bidders = EligibleBidders(task, stateByRegion);

            // A task its bidders cannot collectively fund never enters the auction at all.
            //
            // While a bid had to cover the whole viable award alone, this was implicit: an unaffordable
            // task simply received no bids. Once several regions can share one, an unaffordable task
            // starts drawing force it will never be able to use, and the refund pass only returns that
            // force AFTER the auction has ended - by which time whatever it was outbidding has lost.
            //
            // The case that showed it: an assault on a target too strong to storm outranks the raid it
            // excludes, because a threshold curve priced at its knee beats a linear one. The assault
            // took the force, never reached its launch threshold, was refunded, and the region was
            // neither stormed nor raided. Refusing it up front leaves the raid to win, which is the
            // whole point of offering the two together.
            //
            // This is an upper bound: those budgets are also being spent on other tasks, so a task can
            // still pass here and fall short later. The refund pass remains the backstop.
            if (task.MinimumViableAward > 1L
                && bidders.Sum(bidder => bidder.Source.SpareTroops) < task.MinimumViableAward)
            {
                GameLog.Trace(() =>
                    $"AI skip {faction.Name}/{planet.Name}: {task.Kind} {task.DescribeTarget()} "
                    + $"needs {task.MinimumViableAward}, bidders hold "
                    + $"{bidders.Sum(bidder => bidder.Source.SpareTroops)}");
                continue;
            }

            foreach ((RegionForceState source, int hops) in bidders)
            {
                double flexibility = PaysFlexibilityPrice(task.Kind)
                    ? ForceAllocationConstants.SourceFlexibilityPenalty
                        * ReachableEnemyTargets(source, task.Objective)
                    : 0.0;
                double discount = Math.Pow(ForceAllocationConstants.TransitDiscountPerHop, hops)
                    / (1.0 + flexibility);
                Pairing pairing = new(task, source, hops, discount);
                Bid bid = BestBid(pairing);
                if (bid.BattleValue > 0L) queue.Enqueue(pairing, PriorityOf(task, bid));
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
            if (queue.TryPeek(out _, out (double, double) nextPriority)
                && PriorityOf(task, bid).CompareTo(nextPriority) > 0)
            {
                queue.Enqueue(pairing, PriorityOf(task, bid));
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
                    queue.Enqueue(pairing, PriorityOf(task, next));
                }
            }
        }

        // A task left short of the award that makes it work at all gives everything back.
        //
        // This is the other half of letting several regions fund one task. A bid no longer has to
        // cover the whole viable award on its own, so a task CAN end the auction part-funded - and
        // committing that is the worst failure this design has: the regions are debited, the executor
        // produces nothing, and the force is gone rather than merely misspent. Checking once, here, is
        // the only point at which the total is known.
        //
        // It runs before the reserve sink below, so what comes back is not left idle - it garrisons the
        // region that offered it.
        foreach (ForceTask task in tasks)
        {
            if (task.Assigned <= 0L || !task.ShortOfViableAward) continue;

            long returned = 0L;
            foreach (ForceTaskAward award in awards.Where(a => ReferenceEquals(a.Task, task)))
            {
                award.Source.SpareTroops += award.BattleValue;
                returned += award.BattleValue;
            }
            awards.RemoveAll(award => ReferenceEquals(award.Task, task));
            task.Refund();

            long refunded = returned;
            GameLog.Debug(() =>
                $"AI refund {faction.Name}/{planet.Name}: {task.Kind} {task.DescribeTarget()} "
                + $"held {refunded} of a {task.MinimumViableAward} minimum; returned unspent");
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

    /// <summary>
    /// Queue priority: best value per battle value first, ties broken by the task's own preference.
    /// </summary>
    /// <remarks>
    /// Both terms are negated because PriorityQueue pops the SMALLEST. The tie-break only ever
    /// separates bids that score identically, so it cannot reorder work - which is exactly what makes
    /// it the right place for a preference that must not distort the ranking. Recon puts its target's
    /// population here; with a flat recon importance, equal scores are common rather than a rounding
    /// accident, so the tie-break genuinely decides which of two equally cheap sweeps goes first.
    /// </remarks>
    private static (double, double) PriorityOf(ForceTask task, Bid bid) =>
        (-bid.ScorePerBattleValue, -task.TieBreakValue);

    /// <summary>
    /// Whether this kind of work is charged for the other enemies its source region could have
    /// answered instead.
    /// </summary>
    /// <remarks>
    /// The price came from FactionStagingPlanner's opportunity-cost ordering, and it prices one thing:
    /// committing a region's force to a fight, which is then not available for another border. A
    /// reconnaissance sweep and a standing screen are not that. Their squads are GENERATED
    /// (ForceGenerator, discarded again next turn) rather than drawn from the garrison, and the region's
    /// military strength is untouched, so a frontier region loses no ability to answer its other
    /// neighbours by scouting one of them.
    ///
    /// Charging them anyway hit reconnaissance hardest, because it pays a transit hop as well: a region
    /// facing three other enemies kept 0.6 / (1 + 0.35 x 3) = 0.29 of its bid, against the 0.49 the
    /// works on its own ground kept. That is most of a factor of two, applied to precisely the task
    /// that was already losing to those works.
    /// </remarks>
    private static bool PaysFlexibilityPrice(ForceTaskKind kind) =>
        kind is not (ForceTaskKind.Recon or ForceTaskKind.Patrol);

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
        // An offensive physically removes its force from the staging region, so a region may not
        // offer the strength it needs to keep standing where it is. Only Assault and Raid draw this
        // way; Defend and Move relocate strength between two presences that both survive, and Move
        // already holds a garrison back by construction.
        if (task.Kind is ForceTaskKind.Assault or ForceTaskKind.Raid)
        {
            budget = Math.Min(budget, Math.Max(0L, source.SpareTroops - source.OffensiveGarrisonFloor));
            if (budget <= 0L) return default;
        }
        // A bid must be big enough for the TASK to produce something, not merely big enough for the
        // faction to build its cheapest squad of any kind. A bid below that is burned battle value.
        long floor = task.MinimumBidGiven(Math.Max(1L, source.MinimumBid));
        // ...except the top-up that COMPLETES a task, which is always legal however small. A task's
        // viable award is not a multiple of any region's quantum, so the last sliver is routinely
        // under the source floor: an Ork assault 13 short of its 1,400 threshold could not be finished
        // by a region whose own minimum bid is 30, and was refunded at 99% instead.
        long completion = CompletionAmount(task);
        if (completion > 0L) floor = Math.Min(floor, completion);
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
            // BidValue, not MarginalValue: while a task is short of its viable award, every point is
            // priced at the rate the completed task earns, so several regions can fill it between them
            // without a smaller share ever looking like the better buy.
            double value = task.BidValue(candidate) * discount;
            if (value <= 0.0) continue;
            double perBattleValue = value / candidate;
            // Ties go to the LARGER amount, and CandidateAmounts is ascending. Below a task's viable
            // award every amount scores the same per point, so preferring the smaller one would have a
            // region creep toward the floor a quantum per auction round - many more iterations for the
            // same plan, and a real risk of hitting MaxAuctionIterations on a busy planet.
            if (perBattleValue >= best.ScorePerBattleValue && best.BattleValue > 0L
                || perBattleValue > best.ScorePerBattleValue)
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

        // The amount that would carry the task over its viable award is always on the table, whatever
        // the grid. A region able to finish a task outright should be able to offer exactly that
        // rather than the nearest quantum below it, which would leave the task short and send it to
        // the refund pass.
        long completion = CompletionAmount(task);
        if (completion > 0L && completion <= budget) amounts.Add(completion);

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

    /// <summary>
    /// What it would take to carry this task over its viable award, or zero when it is already there.
    /// </summary>
    private static long CompletionAmount(ForceTask task) =>
        task.ShortOfViableAward ? Math.Max(0L, task.MinimumViableAward - task.Assigned) : 0L;

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
        // Move takes no outside bids either: the task IS one region's own force marching, so the only
        // region that can offer it is the one holding it.
        ForceTaskKind.Patrol
            or ForceTaskKind.Construct
            or ForceTaskKind.Feed
            or ForceTaskKind.Withdraw
            or ForceTaskKind.Move
            or ForceTaskKind.ConsumptionSpread => 0,
        ForceTaskKind.Defend => 1,
        _ => ForceAllocationConstants.MaxBidHops
    };
}
