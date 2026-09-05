using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Belief-backed target queries for planning and presentation. This service deliberately never
    /// enumerates a region's real enemy map to discover targets; a current RegionFaction is only
    /// attached to the returned value when execution needs to resolve contact.
    /// </summary>
    public static class IntelligenceTargetService
    {
        public static IReadOnlyList<StrategicTarget> GetTargets(
            PlanetFaction observer,
            IntelLevel minimumLevel = IntelLevel.Confirmed,
            bool hostileOnly = true)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            return observer.TargetIntel.Values
                .Where(belief => belief.Level >= minimumLevel)
                .Where(belief => !hostileOnly
                    || FactionRelationshipService.GetEffectiveStance(
                        observer.Faction,
                        belief.TargetFaction,
                        belief.Region.Planet) == FactionStance.Hostile)
                .OrderBy(belief => belief.Region.Id)
                .ThenBy(belief => belief.TargetFaction.Id)
                .Select(belief => new StrategicTarget(
                    belief.Region,
                    belief.TargetFaction,
                    belief.Region.RegionFactionMap.GetValueOrDefault(belief.TargetFaction.Id),
                    belief))
                .ToList();
        }

        public static IReadOnlyList<FactionIntelBelief> GetPlayerVisibleBeliefs(Region region)
        {
            if (region == null) return Array.Empty<FactionIntelBelief>();

            return region.Planet.PlanetFactionMap.Values
                .Where(planetFaction =>
                    planetFaction.Faction.IsPlayerFaction
                    || planetFaction.Faction.IsDefaultFaction)
                .SelectMany(planetFaction => planetFaction.TargetIntel.Values
                    .Where(belief => belief.Region == region))
                .Where(belief => belief != null)
                .GroupBy(belief => belief.TargetFaction.Id)
                .Select(group => group
                    .OrderByDescending(belief => belief.Evidence)
                    .ThenByDescending(belief => belief.Level)
                    .First())
                .OrderBy(belief => belief.TargetFaction.Id)
                .ToList();
        }

        public static IReadOnlyList<StrategicTarget> GetPlayerVisibleTargets(
            Region region,
            IntelLevel minimumLevel = IntelLevel.Confirmed,
            bool hostileOnly = true)
        {
            if (region == null) return Array.Empty<StrategicTarget>();

            Faction observerFaction = region.Planet.PlanetFactionMap.Values
                .Select(planetFaction => planetFaction.Faction)
                .FirstOrDefault(faction => faction.IsPlayerFaction)
                ?? region.Planet.PlanetFactionMap.Values
                    .Select(planetFaction => planetFaction.Faction)
                    .FirstOrDefault(faction => faction.IsDefaultFaction);

            return GetPlayerVisibleBeliefs(region)
                .Where(belief => belief.Level >= minimumLevel)
                .Where(belief => !hostileOnly
                    || observerFaction == null
                    || FactionRelationshipService.GetEffectiveStance(
                        observerFaction,
                        belief.TargetFaction,
                        region.Planet) == FactionStance.Hostile)
                .Select(belief => new StrategicTarget(
                    belief.Region,
                    belief.TargetFaction,
                    belief.Region.RegionFactionMap.GetValueOrDefault(belief.TargetFaction.Id),
                    belief))
                .ToList();
        }

        public static FactionIntelBelief GetBestPlayerVisibleBelief(
            Region region,
            Faction targetFaction)
        {
            if (region == null || targetFaction == null) return null;

            return region.Planet.PlanetFactionMap.Values
                .Where(planetFaction =>
                    planetFaction.Faction.IsPlayerFaction
                    || planetFaction.Faction.IsDefaultFaction)
                .Select(planetFaction => planetFaction.GetTargetBelief(region, targetFaction))
                .Where(belief => belief != null)
                .OrderByDescending(belief => belief.Evidence)
                .FirstOrDefault();
        }

        public static FactionIntelBelief GetBestBelief(
            PlanetFaction observer,
            Region region,
            Faction targetFaction)
        {
            return observer?.GetTargetBelief(region, targetFaction);
        }
    }
}
