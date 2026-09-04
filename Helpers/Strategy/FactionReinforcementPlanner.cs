using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Performs the two pre-offensive force-relocation passes for one planet.
/// </summary>
internal sealed class FactionReinforcementPlanner
{
    internal void PlanGarrisonReinforcement(
        Faction faction,
        Planet planet,
        List<RegionForceState> states)
    {
        foreach (RegionForceState source in states)
        {
            if (source.SpareTroops <= 0) continue;

            // Adjacent friendly regions still short of their garrison minimum, neediest first.
            List<RegionForceState> needy = source.RegionFaction.Region.GetAdjacentRegions()
                .Select(region => states.FirstOrDefault(s => s.RegionFaction.Region == region))
                .Where(state => state != null && state.DefensiveShortfall > 0)
                .OrderByDescending(state => state.DefensiveShortfall)
                .ToList();

            foreach (RegionForceState destination in needy)
            {
                if (source.SpareTroops <= 0) break;

                long transfer = System.Math.Min(source.SpareTroops, destination.DefensiveShortfall);
                if (transfer <= 0) continue;

                source.RegionFaction.RemoveMilitaryStrength(transfer);
                destination.RegionFaction.AddMilitaryStrength(transfer);
                source.SpareTroops -= transfer;
                destination.DefensiveShortfall -= transfer;

                GameLog.Debug(() =>
                    $"AI garrison reinforce {faction.Name}/{planet.Name}: "
                    + $"{source.RegionFaction.Region.Name}->{destination.RegionFaction.Region.Name}, "
                    + $"transfer={transfer}, sourceSpare={source.SpareTroops}, "
                    + $"destShortfall={destination.DefensiveShortfall}");
            }
        }
    }

    internal void PlanFrontReinforcement(
        Faction faction,
        Planet planet,
        List<RegionForceState> states)
    {
        foreach (RegionForceState source in states.ToList())
        {
            if (source.SpareTroops <= 0) continue;
            if (!FactionThreatAssessment.HasLocalEnemyCiviliansButNoMilitary(
                    faction, source.RegionFaction.Region)) continue;

            RegionForceState destination = ChooseFrontReinforcementDestination(faction, source, states);
            if (destination == null || destination == source) continue;

            long reserve = System.Math.Max(source.RequiredDefensiveBattleValue, (long)(source.SpareTroops * 0.30));
            long transfer = System.Math.Max(0, source.SpareTroops - reserve);
            if (transfer <= 0) continue;

            source.RegionFaction.RemoveMilitaryStrength(transfer);
            destination.RegionFaction.AddMilitaryStrength(transfer);
            source.SpareTroops -= transfer;
            destination.SpareTroops += transfer;

            GameLog.Debug(() =>
                $"AI reinforce {faction.Name}/{planet.Name}: "
                + $"{source.RegionFaction.Region.Name}->{destination.RegionFaction.Region.Name}, "
                + $"transfer={transfer}, sourceSpare={source.SpareTroops}, destSpare={destination.SpareTroops}");
        }
    }

    private static RegionForceState ChooseFrontReinforcementDestination(
        Faction faction,
        RegionForceState source,
        List<RegionForceState> states)
    {
        List<RegionForceState> adjacentFriendly = source.RegionFaction.Region.GetAdjacentRegions()
            .Select(region => states.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(state => state != null)
            .ToList();
        if (adjacentFriendly.Count == 0) return null;

        return adjacentFriendly
            .OrderByDescending(state => FactionThreatAssessment.VisibleAdjacentEnemyMilitary(
                faction, state.RegionFaction.Region))
            .ThenByDescending(state => state.SpareTroops)
            .FirstOrDefault(state => FactionThreatAssessment.VisibleAdjacentEnemyMilitary(
                faction, state.RegionFaction.Region) > 0);
    }
}
