using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Builders
{
    class PlanetBuilder
    {
        private PlanetBuilder() 
        {
            _usedPlanetNameIndexes = [];
        }
        private static PlanetBuilder _instance;
        public static PlanetBuilder Instance
        {
            get
            {
                _instance ??= new PlanetBuilder();
                return _instance;
            }
        }

        private static HashSet<int> _usedPlanetNameIndexes;

        private static int _nextPlanetId = 0;
        private static int _nextLeaderId = 0;

        // Clears the per-sector generation state. Planet names are drawn without
        // replacement from a finite list and the id counters accumulate, so this must
        // be called at the start of each sector generation. Without it, generating more
        // than one sector in a single process eventually exhausts the name pool and the
        // name-selection loop in GenerateNewPlanet spins forever.
        public void Reset()
        {
            _usedPlanetNameIndexes.Clear();
            _nextPlanetId = 0;
            _nextLeaderId = 0;
        }

        public Planet GenerateNewPlanet(IReadOnlyDictionary<int, PlanetTemplate> planetTemplateMap, 
                                        Coordinate position, Faction controllingFaction, Faction infiltratingFaction)
        {
            PlanetTemplate template = DeterminePlanetTemplate(planetTemplateMap);
            Faction leaderFaction = controllingFaction;
            int nameIndex = RNG.GetIntBelowMax(0, TempPlanetList.PlanetNames.Length);
            while (_usedPlanetNameIndexes.Contains(nameIndex))
            {
                nameIndex = RNG.GetIntBelowMax(0, TempPlanetList.PlanetNames.Length);
            }
            _usedPlanetNameIndexes.Add(nameIndex);
            int importance = (int)(template.ImportanceRange.BaseValue)
                + (int)(RNG.NextRandomZValue() * template.ImportanceRange.StandardDeviation);
            int taxLevel =
                RNG.GetIntBelowMax(template.TaxRange.MinValue, template.TaxRange.MaxValue + 1);
            Planet planet = new Planet(_nextPlanetId, TempPlanetList.PlanetNames[nameIndex],
                                       position, 16, template, importance, taxLevel);
            _nextPlanetId++;

            PlanetFaction planetFaction = new PlanetFaction(controllingFaction);
            planetFaction.PlayerReputation = 0;
            planetFaction.IsPublic = true;
            planet.PlanetFactionMap[controllingFaction.Id] = planetFaction;
            

            PopulateRegions(template, planet, planetFaction);
            planet.SetCapitalRegion(planet.Regions
                .OrderByDescending(region => region.Population)
                .ThenBy(region => region.Id)
                .First().Id);
            if (infiltratingFaction != null)
            {
                HandleInfiltratingFaction(infiltratingFaction, planet);
            }

            if (controllingFaction.IsDefaultFaction)
            {
                planetFaction.Leader = CharacterBuilder.GenerateCharacter(_nextLeaderId, leaderFaction);
                if (!leaderFaction.IsDefaultFaction)
                {
                    // if the planetary leader is a member of a GC,
                    // they'll never reuest player aid 
                    planetFaction.Leader.OpinionOfPlayerForce = -1;
                }
                InitializeContentment(planet, planetFaction.Leader, planetTemplateMap);
                _nextLeaderId++;
            }
            return planet;
        }

        private static void InitializeContentment(
            Planet planet,
            Character governor,
            IReadOnlyDictionary<int, PlanetTemplate> templates)
        {
            int minimumTax = templates.Values.Min(item => item.TaxRange.MinValue);
            int maximumTax = templates.Values.Max(item => item.TaxRange.MaxValue);
            double normalizedTax = maximumTax <= minimumTax
                ? 0.0
                : Math.Clamp((planet.TaxLevel - minimumTax) / (double)(maximumTax - minimumTax), 0.0, 1.0);
            double structural = CivilUnrestRules.CalculateStructuralBaseline(
                normalizedTax, governor.Competence, governor.Severity);

            foreach (Region region in planet.Regions)
            {
                RegionFaction imperial = region.RegionFactionMap.Values.First(rf =>
                    rf.PlanetFaction.Faction.IsDefaultFaction);
                imperial.Contentment = (float)CivilUnrestRules.CalculateContentmentTarget(
                    structural,
                    imperial.Garrison,
                    imperial.Population,
                    region.NonConsumerPopulation,
                    region.CarryingCapacity);
            }
        }

        // Seeds a hidden infiltrating faction (a Genestealer Cult) across every region of a
        // world, carving population and PDF out of each region's public owner. Exposed to
        // ScenarioBuilder so the opening scenario can guarantee a cult on the promised world even
        // when planet generation didn't roll one there.
        internal static void HandleInfiltratingFaction(Faction infiltratingFaction, Planet planet)
        {
            PlanetFaction infiltration = new PlanetFaction(infiltratingFaction);
            // The cult claims a uniform 0-5% of each region's population. Kept small because a cult now
            // rises on its population strength vs the PDF (PRD §4.24), so a large infiltration would
            // revolt almost immediately; a cult must grow (conversion) before it can overrun a world.
            double infiltrationRate = RNG.GetLinearDouble() * 0.05;
            infiltration.PlayerReputation = 0;
            infiltration.IsPublic = false;
            planet.PlanetFactionMap[infiltratingFaction.Id] = infiltration;

            foreach (Region region in planet.Regions)
            {
                RegionFaction owningRegionFaction = region.RegionFactionMap.First().Value;
                long infiltrationPopulation = (long)(region.Population * infiltrationRate);
                long infiltrationPdf = infiltrationPopulation / 33;
                RegionFaction regionFaction = new RegionFaction(infiltration, region);
                regionFaction.Population = infiltrationPopulation;
                regionFaction.Garrison = infiltrationPdf;
                owningRegionFaction.Population -= infiltrationPopulation;
                owningRegionFaction.Garrison -= infiltrationPdf;
                region.RegionFactionMap[infiltration.Faction.Id] = regionFaction;
            }
        }

        private void PopulateRegions(PlanetTemplate template, Planet planet, PlanetFaction planetFaction)
        {
            for (int i = 0; i < 16; i++)
            {
                int regionId = planet.Id * 16 + i;
                planet.Regions[i] = new Region(regionId, planet, 0, GetRegionName(planet, i), RegionExtensions.GetCoordinatesFromRegionNumber(i), 0);
            }

            // Distribute carrying capacity (an absolute, per-type value) across regions using
            // a power law with wiggle. Population is then seeded as a fraction of each region's
            // capacity, so a world never starts above its carrying capacity. The fill fraction
            // emerges from the two type distributions: dense biomes (Hive/Forge) have capacity
            // close to their population and start nearly full, while sparse ones (Agri/Feral)
            // have ample headroom to grow.
            float alpha = 1.5f; // Experiment with this value (1.0 to 3.0 are common)
            float wiggleFactor = 0.2f; // Experiment with this value (0.0 to 0.5 are reasonable)

            long capacityToDistribute = (long)(template.CarryingCapacityRange.Floor)
                + (long)(Math.Pow(10, RNG.NextRandomZValue()) * template.CarryingCapacityRange.Scale);
            long popToDistribute = (long)(template.PopulationRange.Floor)
                + (long)(Math.Pow(10, RNG.NextRandomZValue()) * template.PopulationRange.Scale);

            List<long> regionCapacities = DistributePopulationPowerLaw(capacityToDistribute, 16, alpha, wiggleFactor);

            // Global fill: what fraction of the planet's capacity is currently inhabited.
            // Clamped to 1 so an unusually large population roll saturates the world rather
            // than overfilling it.
            double globalFill = capacityToDistribute <= 0
                ? 0
                : Math.Min(1.0, popToDistribute / (double)capacityToDistribute);

            foreach (Region region in planet.Regions)
            {
                int randomIndex = RNG.GetIntBelowMax(0, regionCapacities.Count - 1);
                long regionCapacity = regionCapacities[randomIndex];
                regionCapacities.RemoveAt(randomIndex);
                region.CarryingCapacity = regionCapacity;
                // The natural ceiling starts equal to the generated capacity; Tyranid Consumption
                // degrades CarryingCapacity below this later, recovering back toward it (PRD §4.24).
                region.MaximumCarryingCapacity = regionCapacity;

                // seed population as the global fill of this region's capacity, with wiggle,
                // never exceeding the region's capacity
                float regionWiggle = 1 + (float)RNG.GetLinearDouble() * wiggleFactor * (RNG.GetIntBelowMax(0, 2) == 0 ? -1 : 1);
                long regionPopulation = (long)(regionCapacity * globalFill * regionWiggle);
                regionPopulation = Math.Clamp(regionPopulation, 0, regionCapacity);

                RegionFaction regionFaction = new RegionFaction(planetFaction, region);
                regionFaction.Population = regionPopulation;
                regionFaction.Garrison = regionFaction.Population / 33;
                region.RegionFactionMap[planetFaction.Faction.Id] = regionFaction;
            }
        }

        private string GetRegionName(Planet planet, int i)
        {
            switch (i)
            {
                case 0:
                    return $"{planet.Name} Alpha";
                case 1:
                    return $"{planet.Name} Beta";
                case 2:
                    return $"{planet.Name} Gamma";
                case 3:
                    return $"{planet.Name} Delta";
                case 4:
                    return $"{planet.Name} Epsilon";
                case 5:
                    return $"{planet.Name} Zeta";
                case 6:
                    return $"{planet.Name} Eta";
                case 7:
                    return $"{planet.Name} Theta";
                case 8:
                    return $"{planet.Name} Iota";
                case 9:
                    return $"{planet.Name} Kappa";
                case 10:
                    return $"{planet.Name} Lambda";
                case 11:
                    return $"{planet.Name} Mu";
                case 12:
                    return $"{planet.Name} Nu";
                case 13:
                    return $"{planet.Name} Xi";
                case 14:
                    return $"{planet.Name} Omicron";
                case 15:
                    return $"{planet.Name} Pi";
                default:
                    return $"{planet.Name} Omega";
            }
        }

        private PlanetTemplate DeterminePlanetTemplate(IReadOnlyDictionary<int, PlanetTemplate> templates)
        {
            // we're using the "lottery ball" approach to randomness here, where each point 
            // of probability for each available body party 
            // defines the size of the random linear distribution
            int max = templates.Values.Sum(pt => pt.Probability);
            int roll = RNG.GetIntBelowMax(0, max);
            foreach (PlanetTemplate template in templates.Values)
            {
                if (roll < template.Probability)
                {
                    return template;
                }
                else
                {
                    // this is basically an easy iterative way to figure out which body part on the "chart" the roll matches
                    roll -= template.Probability;
                }
            }
            // this should never happen
            throw new InvalidOperationException("Could not determine a planet template");
        }

        private List<long> DistributePopulationPowerLaw(long totalPopulation, int numRegions, float alpha, float wiggleFactor)
        {
            // 1. Calculate the normalization constant (C)
            double C = 0;
            for (int i = 1; i <= numRegions; i++)
            {
                C += Math.Pow(i, -alpha);
            }
            C = totalPopulation / C;

            // 2. Generate base populations using the power law
            List<long> basePopulations = new List<long>();
            for (int i = 1; i <= numRegions; i++)
            {
                long basePop = (long)(C * Math.Pow(i, -alpha));
                basePopulations.Add(basePop);
            }

            // 3. Apply the "wiggle factor"
            List<long> finalPopulations = new List<long>();
            long currentTotal = 0; // Keep track of running total for adjustments
            for (int i = 0; i < numRegions; i++)
            {
                float randomFactor = 1 + (float)RNG.GetLinearDouble() * wiggleFactor * (RNG.GetIntBelowMax(0,2) == 0 ? -1 : 1); // Randomly add or subtract
                long population = (long)(basePopulations[i] * randomFactor);
                population = population < 0 ? 0 : population;
                currentTotal += population;
                finalPopulations.Add(population);
            }

            // 4. Adjust populations to match the total (if necessary)
            long difference = totalPopulation - currentTotal;
            if (difference != 0)
            {
                // Distribute the difference proportionally
                for (int i = 0; i < numRegions; i++)
                {
                    double proportion = (double)finalPopulations[i] / currentTotal;
                    long adjustment = (long)(difference * proportion);
                    finalPopulations[i] += adjustment;
                }

                // If there's still a minor difference, add it to a random region
                long remainingDifference = totalPopulation - finalPopulations.Sum();
                if (remainingDifference != 0)
                {
                    finalPopulations[RNG.GetIntBelowMax(0, numRegions)] += remainingDifference;
                }
            }

            return finalPopulations;
        }
    }
}
