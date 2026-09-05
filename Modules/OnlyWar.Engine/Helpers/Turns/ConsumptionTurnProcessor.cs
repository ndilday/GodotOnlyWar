using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves expansion, feeding, predation, and ecological recovery for factions whose
    /// <see cref="Faction.GrowthType"/> is <see cref="GrowthType.Consumption"/>.
    /// </summary>
    internal static class ConsumptionTurnProcessor
    {
        private const double BiomassReferenceAvailability = 1_000_000.0;
        private const double BiomassAppetitePerTroop = 0.5;
        private const double ConsumptionDiminishingExponent = 0.5;
        private const double BiomassFeedEfficiency = 0.5;
        private const int BiomassAllocationSteps = 128;
        private const float CarryingCapacityRecoveryRate = 0.01f;
        private const double ConsumptionExpansionShare = 0.5;

        private static double PredationMarginalYield(double preyRemaining)
        {
            if (preyRemaining <= 0) return 0;
            return BiomassAppetitePerTroop * Math.Log(1 + preyRemaining)
                / Math.Log(1 + BiomassReferenceAvailability);
        }

        private static double ConsumptionMarginalYield(double biomassRemaining)
        {
            if (biomassRemaining <= 0) return 0;
            return BiomassAppetitePerTroop * Math.Pow(
                biomassRemaining / BiomassReferenceAvailability,
                ConsumptionDiminishingExponent);
        }

        /// <summary>
        /// Spreads every consumption faction on the planet toward richer ground, each drawing on
        /// its whole deployed strength.
        /// </summary>
        internal static void ResolveExpansion(Planet planet)
        {
            ResolveExpansion(planet, _ => true);
        }

        internal static void ResolveHiddenExpansion(Planet planet)
        {
            ResolveExpansion(planet, consumer => !consumer.IsPublic);
        }

        private static void ResolveExpansion(
            Planet planet,
            Func<RegionFaction, bool> consumerFilter)
        {
            var moves = new List<(RegionFaction Source, Region Destination, long Amount)>();
            foreach (Region region in planet.Regions)
            {
                foreach (RegionFaction consumer in region.RegionFactionMap.Values
                    .Where(rf => rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption
                                 && rf.Population > 0
                                 && consumerFilter(rf)))
                {
                    (Region destination, long movers) =
                        PlanExpansion(consumer, consumer.GetDeployedStrength());
                    if (destination != null && movers > 0)
                    {
                        moves.Add((consumer, destination, movers));
                    }
                }
            }

            // Plan the whole planet before moving anything so a newly arrived force is not pushed
            // onward again in the same pass.
            foreach ((RegionFaction source, Region destination, long amount) in moves)
            {
                ApplyExpansion(source, destination, amount);
            }
        }

        /// <summary>Returns where a consumer would expand and how much of its budget would move.</summary>
        internal static (Region Destination, long Movers) PlanExpansion(
            RegionFaction consumer,
            long availableTroops)
        {
            if (consumer == null || consumer.Population <= 0 || availableTroops <= 0)
            {
                return (null, 0);
            }

            Region region = consumer.Region;
            double homeBiomass = RegionBiomass(region);
            Region richest = region.GetAdjacentRegions().OrderByDescending(RegionBiomass).FirstOrDefault();
            if (richest == null || RegionBiomass(richest) <= homeBiomass) return (null, 0);

            long movers = Math.Min(
                Math.Min(consumer.Population, availableTroops),
                (long)(availableTroops * RegionDepletion(region) * ConsumptionExpansionShare));
            return movers > 0 ? (richest, movers) : (null, 0);
        }

        internal static void ApplyExpansion(RegionFaction source, Region destination, long amount)
        {
            if (source == null || destination == null || amount <= 0) return;

            long sourceBefore = source.Population;
            source.Population -= amount;
            InvaderPresenceService.Establish(source.PlanetFaction.Faction, destination, amount);
            GameLog.Trace(() =>
                $"Consumption expansion {source.PlanetFaction.Faction.Name} "
                + $"{source.Region.Planet.Name}/{source.Region.Name}->{destination.Name}: "
                + $"moved={amount} (sourcePop {sourceBefore}->{source.Population}), "
                + $"depletion={RegionDepletion(source.Region):F2}, "
                + $"destBiomass={RegionBiomass(destination):F0}");
        }

        private static double RegionBiomass(Region region)
        {
            long prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption)
                .Sum(rf => rf.Population);
            return prey + Math.Max(0, region.CarryingCapacity);
        }

        private static double RegionDepletion(Region region)
        {
            double ceiling = Math.Max(1, region.MaximumCarryingCapacity);
            double capFraction = Math.Clamp(region.CarryingCapacity / ceiling, 0, 1);
            long prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption)
                .Sum(rf => rf.Population);
            double preyFraction = Math.Clamp(prey / ceiling, 0, 1);
            return 1.0 - 0.5 * (capFraction + preyFraction);
        }

        internal static void ResolveFeeding(Region region)
        {
            foreach (RegionFaction consumer in Consumers(region))
            {
                ResolveFeeding(consumer, consumer.GetDeployedStrength());
            }
        }

        internal static void ResolveHiddenFeeding(Region region)
        {
            foreach (RegionFaction consumer in Consumers(region).Where(rf => !rf.IsPublic))
            {
                ResolveFeeding(consumer, consumer.GetDeployedStrength());
            }
        }

        private static List<RegionFaction> Consumers(Region region)
        {
            return region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption)
                .ToList();
        }

        internal static void ResolveFeeding(RegionFaction consumer, double troops)
        {
            if (consumer == null || troops <= 0) return;
            Region region = consumer.Region;

            List<RegionFaction> prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption
                    && rf.Population > 0)
                .ToList();
            double preyRemaining = prey.Sum(rf => rf.Population);
            double biomassRemaining = Math.Max(0, region.CarryingCapacity);

            double predated = 0;
            double consumed = 0;
            double chunk = troops / BiomassAllocationSteps;
            double troopsRemaining = troops;
            for (int step = 0; step < BiomassAllocationSteps && troopsRemaining > 0; step++)
            {
                double thisChunk = Math.Min(chunk, troopsRemaining);
                troopsRemaining -= thisChunk;
                double predationYield = PredationMarginalYield(preyRemaining);
                double consumptionYield = ConsumptionMarginalYield(biomassRemaining);
                if (predationYield <= 0 && consumptionYield <= 0) break;
                if (predationYield >= consumptionYield)
                {
                    double kills = Math.Min(preyRemaining, thisChunk * predationYield);
                    preyRemaining -= kills;
                    predated += kills;
                }
                else
                {
                    double eaten = Math.Min(biomassRemaining, thisChunk * consumptionYield);
                    biomassRemaining -= eaten;
                    consumed += eaten;
                }
            }

            long killed = (long)predated;
            long stripped = (long)consumed;
            long consumerPopBefore = consumer.Population;
            long capacityBefore = region.CarryingCapacity;
            int preyFactionCount = prey.Count;
            long preyBefore = (long)(preyRemaining + predated);
            ApplyPredationKills(prey, killed, consumer.PlanetFaction.Faction);
            region.CarryingCapacity = Math.Max(0, region.CarryingCapacity - stripped);
            ScenarioMetricsCollector.RecordScenarioBlighting(
                region,
                stripped,
                consumer.PlanetFaction.Faction);
            long converted = (long)((killed + stripped) * BiomassFeedEfficiency);
            consumer.Population += converted;
            GameLog.Debug(() =>
                $"Biomass consume {DescribeRegionFaction(consumer)}: troops={troops:F0}, "
                + $"predated={killed} (prey {preyBefore} across {preyFactionCount} factions), "
                + $"consumed={stripped} (capacity {capacityBefore}->{region.CarryingCapacity}), "
                + $"converted={converted} (consumerPop {consumerPopBefore}->{consumer.Population})");
        }

        internal static void ResolvePredation(Region region, long organized, Faction attacker)
        {
            List<RegionFaction> prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.IsDefaultFaction && rf.Population > 0)
                .ToList();
            double preyRemaining = prey.Sum(rf => rf.Population);
            if (preyRemaining <= 0) return;

            long killed = (long)Math.Min(
                preyRemaining,
                organized * PredationMarginalYield(preyRemaining));
            ApplyPredationKills(prey, killed, attacker);
        }

        private static void ApplyPredationKills(
            List<RegionFaction> prey,
            long totalKilled,
            Faction attacker)
        {
            if (totalKilled <= 0) return;
            long preyTotal = prey.Sum(rf => rf.Population);
            if (preyTotal <= 0) return;
            long applied = 0;
            for (int i = 0; i < prey.Count; i++)
            {
                RegionFaction target = prey[i];
                long share = i == prey.Count - 1
                    ? totalKilled - applied
                    : (long)(totalKilled * (double)target.Population / preyTotal);
                share = Math.Clamp(share, 0, target.Population);
                target.Population -= share;
                ScenarioMetricsCollector.RecordScenarioCivilianKills(target, share, attacker);
                applied += share;
            }
        }

        internal static void RecoverCarryingCapacity(Region region)
        {
            if (region.CarryingCapacity >= region.MaximumCarryingCapacity) return;
            bool publicConsumerPresent = region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption
                && rf.Population > 0);
            if (publicConsumerPresent) return;
            long gap = region.MaximumCarryingCapacity - region.CarryingCapacity;
            long recovered = (long)(gap * CarryingCapacityRecoveryRate);
            if (recovered <= 0) recovered = 1;
            long capacityBefore = region.CarryingCapacity;
            region.CarryingCapacity = Math.Min(
                region.MaximumCarryingCapacity,
                region.CarryingCapacity + recovered);
            GameLog.Trace(() =>
                $"Capacity recovery {region.Planet.Name}/{region.Name}: "
                + $"{capacityBefore}->{region.CarryingCapacity} "
                + $"(+{region.CarryingCapacity - capacityBefore} toward ceiling "
                + $"{region.MaximumCarryingCapacity})");
        }

        private static string DescribeRegionFaction(RegionFaction regionFaction)
        {
            if (regionFaction == null) return "unknown";
            return $"{regionFaction.Region.Planet.Name}/{regionFaction.Region.Name}/"
                + $"{regionFaction.PlanetFaction.Faction.Name}";
        }
    }
}
