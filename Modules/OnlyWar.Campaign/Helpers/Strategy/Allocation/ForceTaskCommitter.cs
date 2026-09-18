using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Operations.Turns;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Strategy.Allocation;

/// <summary>
/// Turns the auction's awards into orders and state changes.
/// </summary>
/// <remarks>
/// Commit is deliberately polymorphic. Recon, Patrol, Assault, Raid, Construction and Feed produce an
/// Order; Defend, Withdraw and consumption spreading mutate regional strength directly and produce
/// nothing. Designing for both from the start avoids the trap of an interface that fits the six tasks
/// that issue orders and then has to be bent for the three that do not.
///
/// The executors themselves are the existing planners, unchanged in what they build. What changed is
/// that each now receives an explicit battle-value budget decided by the auction, instead of reading a
/// residual and helping itself to a fraction of it.
/// </remarks>
internal sealed class ForceTaskCommitter
{
    private readonly FactionDevelopmentPlanner _development;
    private readonly FactionConsumptionPlanner _consumption;
    private readonly FactionReconPatrolPlanner _reconPatrol;
    private readonly FactionOffensiveOrderBuilder _offensive;

    internal ForceTaskCommitter(
        FactionDevelopmentPlanner development,
        FactionConsumptionPlanner consumption,
        FactionReconPatrolPlanner reconPatrol,
        FactionOffensiveOrderBuilder offensive)
    {
        _development = development;
        _consumption = consumption;
        _reconPatrol = reconPatrol;
        _offensive = offensive;
    }

    internal void Commit(
        Faction faction,
        Planet planet,
        IReadOnlyList<ForceTaskAward> awards,
        IReadOnlyList<RegionForceState> states,
        List<Order> allOrders,
        IRNG random,
        ForceDoctrineWeights doctrine)
    {
        // Several regions can feed one objective, so awards are committed per TASK, not per award.
        // This is what makes a multi-region assault a single order rather than several small ones.
        foreach (IGrouping<ForceTask, ForceTaskAward> group in awards.GroupBy(award => award.Task))
        {
            ForceTask task = group.Key;
            long total = group.Sum(award => award.BattleValue);
            if (total <= 0L) continue;

            switch (task.Kind)
            {
                case ForceTaskKind.Defend:
                    CommitDefence(faction, planet, task, group, total);
                    break;
                case ForceTaskKind.Withdraw:
                    CommitWithdraw(faction, planet, task, total, states);
                    break;
                case ForceTaskKind.Patrol:
                    _reconPatrol.IssueAllocatedPatrol(
                        faction, planet, task.Home, total, allOrders, random);
                    break;
                case ForceTaskKind.Recon:
                    _reconPatrol.IssueAllocatedRecon(
                        faction, task.Offensive, LargestSource(group), total,
                        doctrine.ReconAggression, allOrders, random);
                    break;
                case ForceTaskKind.Assault:
                case ForceTaskKind.Raid:
                    CommitOffensive(faction, task, group, total, allOrders, random);
                    break;
                case ForceTaskKind.Construct:
                    _development.IssueAllocatedConstruction(
                        faction, task.Home, task.Construction.Value, total, allOrders);
                    break;
                case ForceTaskKind.ConsumptionSpread:
                    ConsumptionTurnProcessor.ApplyExpansion(task.Home, task.Destination, total);
                    break;
                case ForceTaskKind.Feed:
                    _consumption.IssueAllocatedFeed(faction, task.Home, total, allOrders);
                    break;
            }
        }
    }

    /// <summary>
    /// The reserve the region actually committed. Persisted because the tactical assault path
    /// materialises the defence days later and needs the assignment, not the unbounded want.
    /// </summary>
    private static void CommitDefence(
        Faction faction,
        Planet planet,
        ForceTask task,
        IEnumerable<ForceTaskAward> awards,
        long battleValue)
    {
        // A neighbour that won a share of this region's defence is reinforcing it, so the strength has
        // to physically move. This is the whole of troop movement in the new system: there is no
        // separate reinforcement pass, only a region outbidding its neighbours for their force.
        foreach (ForceTaskAward award in awards)
        {
            RegionFaction source = award.Source.RegionFaction;
            if (ReferenceEquals(source, task.Home)) continue;

            long moved = Math.Min(award.BattleValue, source.MilitaryStrength);
            if (moved <= 0L) continue;
            source.RemoveMilitaryStrength(moved);
            task.Home.AddMilitaryStrength(moved);

            GameLog.Debug(() =>
                $"AI reinforce {faction.Name}/{planet.Name}: "
                + $"{source.Region.Name}->{task.Home.Region.Name}, moved={moved}");
        }

        long organized = task.Home.GetDeployedStrength();
        task.Home.AssignedDefensiveBattleValue = Math.Min(organized, battleValue);
    }

    private static void CommitWithdraw(
        Faction faction,
        Planet planet,
        ForceTask task,
        long battleValue,
        IReadOnlyList<RegionForceState> states)
    {
        RegionForceState destination = states
            .FirstOrDefault(state => state.RegionFaction.Region == task.Destination);
        if (destination == null) return;

        long moved = Math.Min(battleValue, task.Home.MilitaryStrength);
        if (moved <= 0L) return;

        task.Home.RemoveMilitaryStrength(moved);
        destination.RegionFaction.AddMilitaryStrength(moved);
        // Deliberately NOT returned to the destination's planning budget. The auction has already
        // finished, so nothing can spend it; crediting it only made the plan's own log report the
        // withdrawn force as unallocated, which read as a leak (Grist Nine: unallocated matched the
        // Withdraw spend exactly, three weeks running). The troops arrive and hold.

        GameLog.Debug(() =>
            $"AI withdraw {faction.Name}/{planet.Name}: {task.Home.Region.Name}->{task.Destination.Name}, "
            + $"moved={moved}, remaining={task.Home.MilitaryStrength}");
    }

    private void CommitOffensive(
        Faction faction,
        ForceTask task,
        IEnumerable<ForceTaskAward> group,
        long total,
        List<Order> allOrders,
        IRNG random)
    {
        // An offensive that the auction did not fund to its threshold does not go ahead. Saturation is
        // the force ratio the attack needs, so committing a fraction of it is not a smaller attack, it
        // is a defeat - and under the old code this was what IsWinnableForFaction refused. Behaviour
        // rules reach the decision through the saturation: a high DefendedLandingRatio makes the
        // threshold unaffordable, and the attack simply never clears it.
        // The task's own minimum viable award IS the launch threshold. Computing it separately here is
        // what let the two drift apart: the auction would part-fund an assault it considered viable,
        // this refused it, and the battle value was debited and lost.
        long threshold = task.MinimumViableAward;
        if (total < threshold)
        {
            GameLog.Debug(() =>
                $"AI {task.Kind} {faction.Name}: target={task.DescribeTarget()}, "
                + $"awarded={total} below threshold={threshold}; not launched");
            return;
        }

        List<(RegionFaction Staging, long BattleValue)> contributions = group
            .GroupBy(award => award.Source.RegionFaction)
            .Select(regionGroup => (regionGroup.Key, regionGroup.Sum(award => award.BattleValue)))
            .Where(contribution => contribution.Item2 > 0L)
            .ToList();
        if (contributions.Count == 0) return;

        bool raid = task.Kind == ForceTaskKind.Raid;
        _offensive.IssueAllocatedOffensive(
            faction,
            task.Offensive,
            contributions,
            total,
            raid ? MissionType.LightningRaid : MissionType.Advance,
            raid ? Aggression.Cautious : Aggression.Normal,
            allOrders,
            random);
    }

    private static RegionFaction LargestSource(IEnumerable<ForceTaskAward> group) =>
        group.OrderByDescending(award => award.BattleValue)
            .Select(award => award.Source.RegionFaction)
            .FirstOrDefault();
}
