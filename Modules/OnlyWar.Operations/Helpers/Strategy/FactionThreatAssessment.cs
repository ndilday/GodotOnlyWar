using OnlyWar.Domain.Extensions;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Operations.Strategy;

/// <summary>
/// Belief-backed and detached-fixture threat queries used by faction planning policies.
/// </summary>
public static class FactionThreatAssessment
{
    // Defender awareness at which an adjacent threat is treated as fully visible.
    public const float GarrisonFullSightIntel = 2.0f;

    // Every deployed region keeps this fraction as a minimum defensive reserve, even when no threat
    // is currently visible. The threat-derived requirement remains intentionally unbounded.
    public const double MinimumDefensiveReserveFraction = 0.20;

    // Share of a believed adjacent enemy that a defender sizes its reserve against. An attacker
    // keeps a reserve of its own and never throws its whole regional strength at one border, so
    // matching a neighbour one-for-one over-garrisons - and it is the attacker's own thresholds that
    // say so: FactionOffensiveEvaluator wants 1.5x the (cautiously inflated) defender before it will
    // commit at all.
    public const double ExpectedAttackerCommitFraction = 0.50;

    public static bool HasPublicEnemyOnPlanet(Faction faction, Planet planet)
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

    public static bool HasLocalEnemyCiviliansButNoMilitary(Faction faction, Region region)
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

    public static bool HasLocalEnemyMilitary(Faction faction, Region region)
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

    public static long VisibleAdjacentEnemyMilitary(Faction faction, Region region)
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

    public static IReadOnlyList<StrategicTarget> GetBelievedTargets(
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
    public static long CalculateRequiredDefensiveBattleValue(RegionFaction defender)
    {
        Faction defenderFaction = defender.PlanetFaction.Faction;
        Region region = defender.Region;

        if (region.Planet.RelationshipLedger != null)
        {
            FactionIntelligenceService.ObservePublicActivity(region.Planet, 0);
        }

        // An enemy sharing this region is the most urgent threat there is, and needs no sight
        // scaling: a force standing on your ground is not something you can fail to notice. This
        // used to be omitted entirely - the scan below looks only at neighbours - so a region with a
        // horde living in it reserved nothing but the minimum floor, fielded no defence the
        // generator could build, and was overrun every week without ever losing a soldier.
        long localThreat;
        if (region.Planet.RelationshipLedger != null)
        {
            localThreat = GetBelievedTargets(defenderFaction, region.Planet, IntelLevel.Suspected)
                .Where(target => target.Region == region
                    && target.CurrentPresence?.IsPublic == true)
                .Sum(target => target.Belief?.EstimatedMilitaryStrength ?? 0);
        }
        else
        {
            localThreat = region.RegionFactionMap.Values
                .Where(rf => !ReferenceEquals(rf, defender)
                    && FactionRelationshipService.AreHostile(
                        defenderFaction, rf.PlanetFaction.Faction, region.Planet))
                .Sum(CalculateDefenderBattleValue);
        }

        // The requirement is a share of EVERY believed neighbour, not all of the largest one. A
        // region with three enemies on its borders is in more danger than one facing a single enemy
        // of the same size, and the old "strongest single threat" rule could not say so - it read
        // both as identical. The share is what keeps the sum honest: no attacker empties its own
        // regions to press one border.
        long adjacentThreatTotal = 0;
        foreach (Region adjacentRegion in region.GetAdjacentRegions())
        {
            // Walking the ground sharpens the estimate, but it is no longer what decides whether a
            // neighbour counts at all. That gate used to be `if (sight <= 0f) continue;`, and it put
            // garrisoning on a different footing from attacking: the offensive planner targets from
            // BELIEFS, which ObservePublicActivity refreshes to Confirmed for free every turn, while
            // the reserve read REGION AWARENESS, which only recon, listening posts and combat
            // produce and which decays 25% a turn. A faction could therefore know an enemy was next
            // door well enough to assault it and still reserve nothing against it. A neighbour the
            // defender can name is a neighbour it can garrison against.
            float sight = Math.Min(1.0f,
                adjacentRegion.GetFactionRegionAwareness(defenderFaction.Id) / GarrisonFullSightIntel);

            long adjacentThreat;
            if (region.Planet.RelationshipLedger != null)
            {
                adjacentThreat = GetBelievedTargets(defenderFaction, region.Planet, IntelLevel.Suspected)
                    .Where(target => target.Region == adjacentRegion
                        && target.CurrentPresence?.IsPublic == true)
                    .Sum(target => (long)((target.Belief?.EstimatedMilitaryStrength ?? 0)
                        * Math.Max(sight, BeliefConfidence(target.Belief))));
            }
            else
            {
                // Detached domain fixtures have no belief store to read a confidence from, so sight
                // remains their only stand-in for what the defender knows. The alignment above
                // applies to the ledger path, which is what a real campaign runs.
                adjacentThreat = adjacentRegion.RegionFactionMap.Values
                    .Where(rf => FactionRelationshipService.AreHostile(
                        defenderFaction, rf.PlanetFaction.Faction, region.Planet))
                    .Sum(rf => (long)(CalculateDefenderBattleValue(rf) * sight));
            }

            adjacentThreatTotal += adjacentThreat;
        }

        // An enemy already standing in this region is not "may attack" - it is here, and committed
        // in full - so it is added at its whole believed strength rather than discounted.
        long required = localThreat
            + (long)(adjacentThreatTotal * ExpectedAttackerCommitFraction);

        // This is a want and is intentionally unbounded. The planner clamps the assigned reserve to
        // the troops the region actually has, so a region facing much more than it can match simply
        // commits everything to holding and does nothing else.
        long floor = (long)(defender.GetDeployedStrength() * MinimumDefensiveReserveFraction);
        return Math.Max(required, floor);
    }

    /// <summary>
    /// How much of a believed enemy strength a defender is willing to reserve against, from the
    /// belief's own evidence.
    /// </summary>
    /// <remarks>
    /// Anchored on <see cref="FactionIntelligenceRules.ConfirmedThreshold"/> because Confirmed is
    /// exactly the level the offensive planner demands before it will target a region
    /// (IntelligenceTargetService.GetTargets). So anything good enough to attack is good enough to
    /// garrison against in full, and weaker beliefs count in proportion to what is actually known.
    /// </remarks>
    public static float BeliefConfidence(FactionIntelBelief belief) =>
        belief == null
            ? 0f
            : Math.Min(1f, belief.Evidence / FactionIntelligenceRules.ConfirmedThreshold);

    /// <summary>
    /// Strategy's estimate of a defender's battle value, distinct from resolver-side fieldable value.
    /// </summary>
    public static long CalculateDefenderBattleValue(RegionFaction defender)
    {
        return defender.MilitaryStrength
             + defender.LandedSquads.Sum(s => s.Members.Sum(m => (long)m.Template.BattleValue));
    }
}
