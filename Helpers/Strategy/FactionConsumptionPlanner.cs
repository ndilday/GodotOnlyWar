using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Plans the two Consumption-faction policies that consume the residual regional budget.
/// </summary>
internal sealed class FactionConsumptionPlanner
{
    /// <summary>Moves a budget-sized share toward richer adjacent ground.</summary>
    internal void PlanConsumptionExpansionOnPlanet(
        Faction faction,
        Planet planet,
        List<RegionForceState> states)
    {
        foreach (RegionForceState state in states)
        {
            if (state.SpareTroops <= 0) continue;

            (Region destination, long movers) =
                ConsumptionTurnProcessor.PlanExpansion(state.RegionFaction, state.SpareTroops);
            if (destination == null || movers <= 0) continue;

            ConsumptionTurnProcessor.ApplyExpansion(state.RegionFaction, destination, movers);
            state.SpareTroops = Math.Max(0, state.SpareTroops - movers);

            GameLog.Debug(() =>
                $"AI consumption spread {faction.Name}/{planet.Name}: "
                + $"{state.RegionFaction.Region.Name}->{destination.Name}, "
                + $"movers={movers}, sourceSpare={state.SpareTroops}");
        }
    }

    /// <summary>Commits remaining budget to squad-less feed missions.</summary>
    internal void PlanFeedMissionsOnPlanet(
        Faction faction,
        Planet planet,
        List<RegionForceState> states,
        List<Order> allOrders)
    {
        foreach (RegionForceState state in states)
        {
            if (state.SpareTroops <= 0) continue;

            long committed = state.SpareTroops;
            FeedMission mission = new FeedMission(committed, state.RegionFaction);
            allOrders.Add(new Order(new List<Squad>(), true, false, Aggression.Cautious, mission, faction));
            state.SpareTroops = 0;

            GameLog.Debug(() =>
                $"AI feed {faction.Name}/{planet.Name}/{state.RegionFaction.Region.Name}: "
                + $"committedBV={committed}, deployed={state.RegionFaction.GetDeployedStrength()}, "
                + $"defensiveReserve={state.AssignedDefensiveBattleValue}");
        }
    }
}
