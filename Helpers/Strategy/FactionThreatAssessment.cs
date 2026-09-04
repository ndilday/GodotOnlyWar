using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Strategy;

/// <summary>
/// Belief-backed and detached-fixture threat queries used by faction planning policies.
/// </summary>
internal static class FactionThreatAssessment
{
    // Defender awareness at which an adjacent threat is treated as fully visible.
    internal const float GarrisonFullSightIntel = 2.0f;

    // Every deployed region keeps this fraction as a minimum defensive reserve, even when no threat
    // is currently visible. The threat-derived requirement remains intentionally unbounded.
    internal const double MinimumDefensiveReserveFraction = 0.20;

    internal static bool HasPublicEnemyOnPlanet(Faction faction, Planet planet)
    {
        if (planet?.RelationshipLedger != null)
        {
            return GetBelievedTargets(faction, planet)
                .Any(target => target.CurrentPresence?.IsPublic == true);
        }

        return planet.Regions
            .SelectMany(region => region.RegionFactionMap.Values)
            .Any(regionFaction => regionFaction.IsPublic
                                  && FactionRelationshipService.AreHostile(
                                      faction, regionFaction.PlanetFaction.Faction, planet));
    }

    internal static bool HasLocalEnemyCiviliansButNoMilitary(Faction faction, Region region)
    {
        if (region?.Planet?.RelationshipLedger != null)
        {
            List<StrategicTarget> believedTargets = GetBelievedTargets(faction, region.Planet)
                .Where(target => target.Region == region && target.CurrentPresence?.IsPublic == true)
                .ToList();
            return believedTargets.Any(target =>
                target.Belief?.EstimatedPopulation > 0
                && (target.Belief?.EstimatedMilitaryStrength ?? 0) <= 0);
        }

        List<RegionFaction> enemies = region.RegionFactionMap.Values
            .Where(rf => rf.IsPublic && FactionRelationshipService.AreHostile(
                faction, rf.PlanetFaction.Faction, region.Planet))
            .ToList();
        return enemies.Any(rf => rf.Population > 0)
               && enemies.All(rf => CalculateDefenderBattleValue(rf) <= 0);
    }

    internal static bool HasLocalEnemyMilitary(Faction faction, Region region)
    {
        if (region?.Planet?.RelationshipLedger != null)
        {
            return GetBelievedTargets(faction, region.Planet)
                .Any(target => target.Region == region
                    && target.CurrentPresence?.IsPublic == true
                    && target.Belief?.EstimatedMilitaryStrength > 0);
        }

        return region.RegionFactionMap.Values.Any(rf =>
            rf.IsPublic
            && FactionRelationshipService.AreHostile(faction, rf.PlanetFaction.Faction, region.Planet)
            && CalculateDefenderBattleValue(rf) > 0);
    }

    internal static long VisibleAdjacentEnemyMilitary(Faction faction, Region region)
    {
        if (region?.Planet?.RelationshipLedger != null)
        {
            return GetBelievedTargets(faction, region.Planet)
                .Where(target => target.CurrentPresence?.IsPublic == true
                    && target.Region.GetAdjacentRegions().Contains(region))
                .Sum(target => target.Belief?.EstimatedMilitaryStrength ?? 0);
        }

        return region.GetAdjacentRegions()
            .SelectMany(adjacent => adjacent.RegionFactionMap.Values)
            .Where(rf => rf.IsPublic && FactionRelationshipService.AreHostile(
                faction, rf.PlanetFaction.Faction, region.Planet))
            .Sum(CalculateDefenderBattleValue);
    }

    internal static IReadOnlyList<StrategicTarget> GetBelievedTargets(
        Faction observerFaction,
        Planet planet,
        IntelLevel minimumLevel = IntelLevel.Confirmed)
    {
        if (planet?.RelationshipLedger == null || observerFaction == null)
        {
            return Array.Empty<StrategicTarget>();
        }

        PlanetFaction observer = planet.PlanetFactionMap.GetValueOrDefault(observerFaction.Id);
        return observer == null
            ? Array.Empty<StrategicTarget>()
            : IntelligenceTargetService.GetTargets(observer, minimumLevel);
    }

    /// <summary>
    /// Calculates the strategic requirement for a region's defensive reserve.
    /// </summary>
    /// <remarks>
    /// This is deliberately a planning want, not a promise that the troops exist. It observes public
    /// activity when a ledger is present and falls back to live detached-fixture values otherwise.
    /// </remarks>
    internal static long CalculateRequiredDefensiveBattleValue(RegionFaction defender)
    {
        Faction defenderFaction = defender.PlanetFaction.Faction;
        Region region = defender.Region;

        if (region.Planet.RelationshipLedger != null)
        {
            FactionIntelligenceService.ObservePublicActivity(region.Planet, 0);
        }

        long highestThreat = 0;
        foreach (Region adjacentRegion in region.GetAdjacentRegions())
        {
            // A blind defender under-reserves because it cannot see what is massing next door. Awareness
            // is opened either by deliberate recon or by the reactive attack path.
            float sight = Math.Min(1.0f,
                adjacentRegion.GetFactionRegionAwareness(defenderFaction.Id) / GarrisonFullSightIntel);
            if (sight <= 0f) continue;

            long adjacentThreat;
            if (region.Planet.RelationshipLedger != null)
            {
                adjacentThreat = GetBelievedTargets(defenderFaction, region.Planet, IntelLevel.Suspected)
                    .Where(target => target.Region == adjacentRegion
                        && target.CurrentPresence?.IsPublic == true)
                    .Sum(target => (long)((target.Belief?.EstimatedMilitaryStrength ?? 0) * sight));
            }
            else
            {
                // Detached domain fixtures have no observation boundary; retain direct combat values so
                // this helper remains useful in isolation.
                adjacentThreat = adjacentRegion.RegionFactionMap.Values
                    .Where(rf => FactionRelationshipService.AreHostile(
                        defenderFaction, rf.PlanetFaction.Faction, region.Planet))
                    .Sum(rf => (long)(CalculateDefenderBattleValue(rf) * sight));
            }

            if (adjacentThreat > highestThreat) highestThreat = adjacentThreat;
        }

        // This is a want, derived from the strongest visible adjacent threat, and is intentionally
        // unbounded. The planner clamps the assigned reserve to deployed strength.
        long floor = (long)(defender.GetDeployedStrength() * MinimumDefensiveReserveFraction);
        return Math.Max(highestThreat, floor);
    }

    /// <summary>
    /// Strategy's estimate of a defender's battle value, distinct from resolver-side fieldable value.
    /// </summary>
    internal static long CalculateDefenderBattleValue(RegionFaction defender)
    {
        return defender.MilitaryStrength
             + defender.LandedSquads.Sum(s => s.Members.Sum(m => (long)m.Template.BattleValue));
    }
}
