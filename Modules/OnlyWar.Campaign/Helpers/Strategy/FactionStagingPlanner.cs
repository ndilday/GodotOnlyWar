using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Selects the order in which public staging regions should contribute to an operation.
/// </summary>
/// <remarks>
/// Reconnaissance and tactical offensives use the same opportunity-cost ordering. A null state list
/// is intentional for defensive recon, whose generated force has no shared planning budget to debit.
/// </remarks>
internal static class FactionStagingPlanner
{
    internal static List<Region> ChooseStagingRegionsByOpportunityCost(
        PotentialOffensive offensive,
        List<RegionForceState> regionalForceStates)
    {
        if (regionalForceStates == null) return offensive.AttackingRegions.ToList();

        Faction attacker = regionalForceStates
            .FirstOrDefault(s => offensive.AttackingRegions.Contains(s.RegionFaction.Region))
            ?.RegionFaction.PlanetFaction.Faction;
        if (attacker == null) return offensive.AttackingRegions.ToList();

        return offensive.AttackingRegions
            .Select(region => regionalForceStates.FirstOrDefault(s => s.RegionFaction.Region == region))
            .Where(state => state != null && state.SpareTroops > 0)
            .OrderBy(state => CountReachableEnemyTargets(attacker, state.RegionFaction.Region, offensive.TargetRegion))
            .ThenBy(state => FactionThreatAssessment.HasLocalEnemyMilitary(attacker, state.RegionFaction.Region) ? 1 : 0)
            .ThenByDescending(state => state.SpareTroops)
            .Select(state => state.RegionFaction.Region)
            .ToList();
    }

    private static int CountReachableEnemyTargets(Faction faction, Region sourceRegion, Region currentTarget)
    {
        return sourceRegion.GetAdjacentRegions()
            .Where(region => region != currentTarget)
            .Count(region => region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic && FactionRelationshipService.AreHostile(
                    faction, rf.PlanetFaction.Faction, region.Planet)));
    }
}
