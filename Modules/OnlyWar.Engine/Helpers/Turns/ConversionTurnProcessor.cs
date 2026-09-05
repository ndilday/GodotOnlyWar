using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using System;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves maneuvers, revolts, and suppression for factions whose growth trait is Conversion.
    /// </summary>
    internal sealed class ConversionTurnProcessor
    {
        private const double ConversionRelocationRate = 0.25;
        private static readonly DefenseType[] RevoltSeizureOrder =
        [
            DefenseType.ListeningPost,
            DefenseType.AntiAir,
            DefenseType.Entrenchment
        ];

        private readonly GameSession _session;

        internal ConversionTurnProcessor(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        internal void ProcessPlanet(Planet planet)
        {
            foreach (Region region in planet.Regions)
            {
                ResolveManeuvers(region);
            }
            CheckForPlanetaryRevolt(planet);
            CheckForRevoltSuppression(planet);
        }

        internal static void ResolveManeuvers(Region region)
        {
            RegionFaction converter = region.RegionFactionMap.Values.FirstOrDefault(
                rf => rf.IsPublic && rf.PlanetFaction.Faction.GrowthType == GrowthType.Conversion);
            if (converter == null || converter.Population <= 0) return;

            int organization = Math.Max(converter.Organization, 0);
            long organized = (long)(converter.Population * (organization / 100.0));
            if (organized <= 0 || HasActiveImperialEnemyNearby(region)) return;

            Region frontward = region.GetAdjacentRegions().FirstOrDefault(HasActiveImperialEnemyNearby);
            if (frontward != null)
            {
                RelocateConversionForce(converter, frontward, organized);
                return;
            }

            ConsumptionTurnProcessor.ResolvePredation(
                region,
                organized,
                converter.PlanetFaction.Faction);
        }

        private static bool HasActiveImperialEnemyNearby(Region region)
        {
            return region.GetSelfAndAdjacentRegions().Any(r => r.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && FactionRelationshipService.IsImperial(rf.PlanetFaction.Faction)
                && (rf.Garrison > 0 || rf.LandedSquads.Count > 0)));
        }

        private static void RelocateConversionForce(
            RegionFaction converter,
            Region destination,
            long organized)
        {
            long movers = Math.Min(
                converter.Population,
                (long)(organized * ConversionRelocationRate));
            if (movers <= 0) return;
            converter.Population -= movers;

            Faction faction = converter.PlanetFaction.Faction;
            if (!destination.RegionFactionMap.TryGetValue(faction.Id, out RegionFaction destinationForce))
            {
                if (!destination.Planet.PlanetFactionMap.TryGetValue(
                    faction.Id,
                    out PlanetFaction destinationPlanetFaction))
                {
                    destinationPlanetFaction = new PlanetFaction(faction) { IsPublic = true };
                    destination.Planet.PlanetFactionMap[faction.Id] = destinationPlanetFaction;
                }
                destinationForce = new RegionFaction(destinationPlanetFaction, destination)
                {
                    IsPublic = true,
                    Organization = Math.Max(converter.Organization, 0)
                };
                destination.RegionFactionMap[faction.Id] = destinationForce;
            }
            destinationForce.IsPublic = true;
            destinationForce.Population += movers;
        }

        private void CheckForPlanetaryRevolt(Planet planet)
        {
            foreach (Region region in planet.Regions.Where(region => region != null))
            {
                RegionFaction loyalist = region.RegionFactionMap.Values.FirstOrDefault(rf =>
                    rf.IsPublic
                    && (rf.PlanetFaction.Faction.IsDefaultFaction
                        || rf.PlanetFaction.Faction.IsPlayerFaction));
                if (loyalist == null) continue;

                bool externalEnemyPresent = region.RegionFactionMap.Values.Any(rf =>
                    rf.IsPublic
                    && FactionRelationshipService.IsExternalEnemy(rf.PlanetFaction.Faction));
                foreach (RegionFaction infiltrator in region.RegionFactionMap.Values
                    .Where(rf => !rf.IsPublic
                        && rf.PlanetFaction.Faction.GrowthType == GrowthType.Conversion)
                    .ToList())
                {
                    if (externalEnemyPresent
                        || infiltrator.MilitaryStrength <= loyalist.MilitaryStrength)
                    {
                        continue;
                    }

                    FactionRevealService.Reveal(infiltrator);
                    TransferRevoltDefenses(loyalist, infiltrator);
                }
            }
        }

        private void CheckForRevoltSuppression(Planet planet)
        {
            foreach (Region region in planet.Regions.Where(region => region != null))
            {
                RegionFaction loyalist = region.RegionFactionMap.Values.FirstOrDefault(rf =>
                    rf.IsPublic
                    && (rf.PlanetFaction.Faction.IsDefaultFaction
                        || rf.PlanetFaction.Faction.IsPlayerFaction));
                if (loyalist == null) continue;

                foreach (RegionFaction insurgent in region.RegionFactionMap.Values
                    .Where(rf => rf.IsPublic
                        && rf.PlanetFaction.Faction.GrowthType == GrowthType.Conversion)
                    .ToList())
                {
                    if (insurgent.MilitaryStrength >= 0.7f * loyalist.MilitaryStrength) continue;
                    insurgent.IsPublic = false;
                    insurgent.HalveDefensesOnGoingToGround();
                }
            }

            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values
                .Where(pf => pf.Faction.GrowthType == GrowthType.Conversion))
            {
                planetFaction.IsPublic = planet.Regions.Where(region => region != null).Any(region =>
                    region.RegionFactionMap.TryGetValue(
                        planetFaction.Faction.Id,
                        out RegionFaction regionFaction)
                    && regionFaction.IsPublic);
            }
        }

        private void TransferRevoltDefenses(RegionFaction loyalist, RegionFaction revolting)
        {
            // The order is load-bearing because each draw consumes the seeded random stream.
            foreach (DefenseType defenseType in RevoltSeizureOrder)
            {
                double share = DrawRevoltDefenseShare(loyalist.GetDefense(defenseType));
                loyalist.AddDefense(defenseType, -share);
                revolting.AddDefense(defenseType, share);
            }
            loyalist.Organization = (int)(_session.Random.GetLinearDouble() * 100);
        }

        private double DrawRevoltDefenseShare(double defense) => defense <= 0
            ? 0
            : Math.Clamp(defense / 2.0 + _session.Random.NextRandomZValue(), 0.0, defense);
    }
}
