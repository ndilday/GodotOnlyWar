using OnlyWar.Domain;
using OnlyWar.Domain.Extensions;
using OnlyWar.Campaign.Turns;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Squads;
using OnlyWar.Runtime.Allocators;
using System;
using System.Collections.Generic;

namespace OnlyWar.Campaign.Strategy;

/// <summary>
/// Issues the squad-less feed orders the force-allocation auction awards to Consumption factions.
/// </summary>
internal sealed class FactionConsumptionPlanner
{
    private readonly IPersistentIdAllocator _identity;

    internal FactionConsumptionPlanner(IPersistentIdAllocator identity = null)
    {
        _identity = identity ?? new PersistentIdAllocator();
    }

    /// <summary>
    /// Commits the battle value the auction awarded to a squad-less feed mission.
    /// </summary>
    /// <remarks>
    /// Feeding used to be the terminal sink: it ran last, took 100% of whatever survived every other
    /// policy, and zeroed the budget. It now carries an importance and a saturation drawn from the
    /// biomass actually on hand, so "spread or graze" is a real comparison rather than an ordering.
    /// </remarks>
    internal void IssueAllocatedFeed(
        Faction faction,
        RegionFaction regionFaction,
        long budget,
        List<Order> allOrders)
    {
        if (regionFaction == null || budget <= 0) return;

        FeedMission mission = new FeedMission(
            _identity.GetNextMissionId(), budget, regionFaction);
        allOrders.Add(new Order(
            _identity.GetNextOrderId(),
            new List<Squad>(),
            true,
            false,
            Aggression.Cautious,
            mission,
            faction));

        GameLog.Debug(() =>
            $"AI feed {faction.Name}/{regionFaction.Region.Planet.Name}/{regionFaction.Region.Name}: "
            + $"committedBV={budget}, deployed={regionFaction.GetDeployedStrength()}, "
            + $"defensiveReserve={regionFaction.AssignedDefensiveBattleValue}");
    }
}
