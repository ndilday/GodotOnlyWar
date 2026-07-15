using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Resolves the planet-scoped portion of a campaign turn. The ordering in <see cref="UpdatePlanet"/>
    /// intentionally mirrors the former TurnController implementation because population growth,
    /// biomass consumption, remnant state changes, revolts, governors, and intelligence all observe
    /// the state produced by the preceding phase.
    /// </summary>
    internal sealed class PlanetTurnProcessor
    {
        private const float LogisticGrowthRate = 0.0006f;
        private const float BaselineGrowthRate = 0.0004f;
        private const float GarrisonAttritionRate = 0.001f;
        private const float GarrisonDraftRate = 0.025f;
        private const float EmergencyGarrisonDraftRate = 0.05f;
        private const float ActiveAssaultGarrisonDraftRate = 0.15f;
        private const float OverrunRemnantGarrisonArmingRate = 1.0f;
        private const double OccupiedDefenseDecayPerTurn = 0.25;
        private const double BiomassReferenceAvailability = 1_000_000.0;
        private const double BiomassAppetitePerTroop = 0.5;
        private const double ConsumptionDiminishingExponent = 0.5;
        private const double BiomassFeedEfficiency = 0.5;
        private const int BiomassAllocationSteps = 128;
        private const float CarryingCapacityRecoveryRate = 0.01f;
        private const double ImperialEmigrationRate = 0.05;
        private const double TyranidExpansionShare = 0.5;
        private const double CultRelocationRate = 0.25;

        private const float IntelDecayRate = 0.75f;
        private const float IntelPerListeningPostLevel = 0.2f;
        private const float IntelPatrolBaseGain = 1.0f;

        private readonly GameSession _session;
        private readonly List<Mission> _specialMissions;
        private readonly TurnIntelLedger _intelLedger;
        private readonly GovernorTurnProcessor _governorTurnProcessor;

        internal PlanetTurnProcessor(
            GameSession session,
            List<Mission> specialMissions,
            TurnIntelLedger intelLedger = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _specialMissions = specialMissions ?? throw new ArgumentNullException(nameof(specialMissions));
            _intelLedger = intelLedger ?? new TurnIntelLedger();
            _governorTurnProcessor = new GovernorTurnProcessor(_session);
        }

        internal void ClearTurnIntelGains()
        {
            _intelLedger.Clear();
        }

        internal void RecordIntelGain(PlanetFaction planetFaction, Region region, float gain)
        {
            _intelLedger.RecordGain(planetFaction, region, gain);
        }

        internal void UpdatePlanets(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                UpdatePlanet(planet);
            }
        }

        internal void UpdatePlanet(Planet planet)
        {
            // Tyranid troop AI step 2: spreading precedes consumption so departing force is not
            // counted twice at home.
            ResolveTyranidExpansion(planet);

            foreach (Region region in planet.Regions)
            {
                float pdfRatio = region.PlanetaryDefenseForces / (float)region.Population;
                foreach (RegionFaction regionFaction in region.RegionFactionMap.Values.ToList())
                {
                    if (CanRemoveRegionFaction(regionFaction))
                    {
                        region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
                    }
                    else
                    {
                        EndOfTurnRegionFactionsUpdate(regionFaction, pdfRatio);
                    }
                }

                ResolveBiomassConsumption(region);
                RecoverCarryingCapacity(region);
                DecayUnmannedDefenses(region);
                RemoveEmptyRegionFactions(region);
            }

            // Two passes are deliberate: emigration must see the finalized hide/unhide state for
            // every neighboring Imperial remnant.
            foreach (Region region in planet.Regions)
            {
                UpdateImperialRemnantState(region);
            }
            foreach (Region region in planet.Regions)
            {
                ProcessImperialEmigration(region);
            }

            foreach (Region region in planet.Regions)
            {
                ResolveCultManeuvers(region);
            }

            CheckForPlanetaryRevolt(planet);
            CheckForRevoltSuppression(planet);

            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values.ToList())
            {
                long planetFactionPopulation = planet.Regions.Sum(
                    r => r.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf)
                        ? rf.Population
                        : 0);
                if (planetFactionPopulation <= 0)
                {
                    planet.PlanetFactionMap.Remove(planetFaction.Faction.Id);
                }
                else if (planetFaction.Leader != null)
                {
                    _governorTurnProcessor.ProcessGovernor(planet, planetFaction);
                }
            }

            UpdateRegionIntel(planet);
        }

        internal void EndOfTurnRegionFactionsUpdate(RegionFaction regionFaction, float pdfRatio)
        {
            Planet planet = regionFaction.Region.Planet;
            Faction controllingFaction = planet.GetControllingFaction();
            float newPop = 0;
            bool isOverrunRemnant = regionFaction.PlanetFaction.Faction.IsDefaultFaction && !regionFaction.IsPublic;

            switch (regionFaction.PlanetFaction.Faction.GrowthType)
            {
                case GrowthType.Logistic:
                    newPop = ApplyCarryingCapacity(
                        regionFaction.Population * LogisticGrowthRate * regionFaction.GrowthMultiplier,
                        regionFaction.Region);
                    break;
                case GrowthType.Conversion:
                    if (!regionFaction.IsPublic)
                    {
                        newPop = ConvertPopulation(regionFaction.Region, regionFaction, newPop);
                        if (regionFaction.PlanetFaction.Faction.Id != controllingFaction.Id &&
                            planet.PlanetFactionMap[controllingFaction.Id].Leader != null)
                        {
                            // Governor detection of converted population remains future work.
                        }
                    }
                    break;
                case GrowthType.Consumption:
                    newPop = 0;
                    break;
                default:
                    newPop = ApplyCarryingCapacity(
                        regionFaction.Population * BaselineGrowthRate * regionFaction.GrowthMultiplier,
                        regionFaction.Region);
                    break;
            }

            float whole = (float)Math.Truncate(newPop);
            float fraction = newPop - whole;
            if (_session.Random.GetLinearDouble() < Math.Abs(fraction))
            {
                whole += Math.Sign(fraction);
            }
            long populationBeforeGrowth = regionFaction.Population;
            regionFaction.Population += (long)whole;
            if (regionFaction.Population < 0)
            {
                regionFaction.Population = 0;
            }
            long grown = regionFaction.Population - populationBeforeGrowth;
            RecordScenarioNaturalPopulationChange(regionFaction, grown);
            if (isOverrunRemnant && grown > 0)
            {
                long garrisonBefore = regionFaction.Garrison;
                regionFaction.Garrison += (long)(grown * OverrunRemnantGarrisonArmingRate);
                RecordScenarioPdfDrafted(regionFaction, regionFaction.Garrison - garrisonBefore);
            }
            UpdateRegionFactionForces(regionFaction, pdfRatio, newPop);
        }

        private static float ApplyCarryingCapacity(float baseGrowth, Region region)
        {
            long capacity = region.CarryingCapacity;
            if (capacity <= 0)
            {
                return baseGrowth;
            }
            float crowding = 1f - (region.NonConsumerPopulation / (float)capacity);
            return baseGrowth * crowding;
        }

        private void UpdateRegionFactionForces(RegionFaction regionFaction, float pdfRatio, float newPop)
        {
            bool isDefaultFaction = regionFaction.PlanetFaction.Faction.IsDefaultFaction;
            bool isPlayerFaction = regionFaction.PlanetFaction.Faction.IsPlayerFaction;
            bool isOverrunRemnant = isDefaultFaction && !regionFaction.IsPublic;

            if ((isDefaultFaction || isPlayerFaction || !regionFaction.IsPublic) && !isOverrunRemnant)
            {
                regionFaction.Garrison -= (long)(regionFaction.Garrison * GarrisonAttritionRate);

                float draftRate = GarrisonDraftRate;
                if (isDefaultFaction && regionFaction.IsPublic && HasPublicNpcFactionInRegion(regionFaction))
                {
                    draftRate = ActiveAssaultGarrisonDraftRate;
                }
                else if (pdfRatio < 0.03f || !regionFaction.IsPublic)
                {
                    draftRate = EmergencyGarrisonDraftRate;
                }

                long garrisonBeforeDraft = regionFaction.Garrison;
                regionFaction.Garrison += (long)(newPop * draftRate);
                RecordScenarioPdfDrafted(
                    regionFaction,
                    Math.Max(0, regionFaction.Garrison - garrisonBeforeDraft));
            }
        }

        private static bool HasPublicNpcFactionInRegion(RegionFaction regionFaction)
        {
            return regionFaction.Region.RegionFactionMap.Values.Any(other =>
                other != regionFaction
                && other.IsPublic
                && !other.PlanetFaction.Faction.IsPlayerFaction
                && !other.PlanetFaction.Faction.IsDefaultFaction);
        }

        private float ConvertPopulation(Region region, RegionFaction regionFaction, float newPop)
        {
            RegionFaction defaultFaction = region.RegionFactionMap.Values.First(pf => pf.PlanetFaction.Faction.IsDefaultFaction);
            if (defaultFaction?.Population > 0)
            {
                defaultFaction.Population--;
                regionFaction.Population++;
                float pdfChance = (float)defaultFaction.Garrison / defaultFaction.Population;
                if (_session.Random.GetLinearDouble() < pdfChance)
                {
                    defaultFaction.Garrison--;
                    regionFaction.Garrison++;
                }
                if (regionFaction.Population > 100)
                {
                    newPop = regionFaction.Population * 0.002f;
                }
            }

            return newPop;
        }

        private static double PredationMarginalYield(double preyRemaining)
        {
            if (preyRemaining <= 0) return 0;
            return BiomassAppetitePerTroop * Math.Log(1 + preyRemaining) / Math.Log(1 + BiomassReferenceAvailability);
        }

        private static double ConsumptionMarginalYield(double biomassRemaining)
        {
            if (biomassRemaining <= 0) return 0;
            return BiomassAppetitePerTroop * Math.Pow(
                biomassRemaining / BiomassReferenceAvailability,
                ConsumptionDiminishingExponent);
        }

        internal static void ResolveTyranidExpansion(Planet planet)
        {
            var moves = new List<(RegionFaction Source, Region Destination, long Amount)>();
            foreach (Region region in planet.Regions)
            {
                foreach (RegionFaction swarm in region.RegionFactionMap.Values
                    .Where(rf => rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption && rf.Population > 0))
                {
                    long organized = (long)(swarm.Population * (Math.Max(swarm.Organization, 0) / 100.0));
                    if (organized <= 0) continue;

                    double homeBiomass = RegionBiomass(region);
                    Region richest = region.GetAdjacentRegions().OrderByDescending(RegionBiomass).FirstOrDefault();
                    if (richest == null || RegionBiomass(richest) <= homeBiomass) continue;

                    long movers = Math.Min(swarm.Population,
                        (long)(organized * RegionDepletion(region) * TyranidExpansionShare));
                    if (movers > 0)
                    {
                        moves.Add((swarm, richest, movers));
                    }
                }
            }

            foreach ((RegionFaction source, Region destination, long amount) in moves)
            {
                long sourceBefore = source.Population;
                source.Population -= amount;
                EstablishInvaderPresence(source.PlanetFaction.Faction, destination, amount);
                GameLog.Trace(() =>
                    $"Tyranid expansion {source.PlanetFaction.Faction.Name} "
                    + $"{source.Region.Planet.Name}/{source.Region.Name}->{destination.Name}: "
                    + $"moved={amount} (sourcePop {sourceBefore}->{source.Population}), "
                    + $"depletion={RegionDepletion(source.Region):F2}, "
                    + $"destBiomass={RegionBiomass(destination):F0}");
            }
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

        internal static void ResolveBiomassConsumption(Region region)
        {
            foreach (RegionFaction consumer in region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption).ToList())
            {
                double troops = consumer.Population * (consumer.Organization / 100.0);
                if (troops <= 0) continue;

                List<RegionFaction> prey = region.RegionFactionMap.Values
                    .Where(rf => rf.PlanetFaction.Faction.GrowthType != GrowthType.Consumption && rf.Population > 0)
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
                long swarmPopBefore = consumer.Population;
                long capacityBefore = region.CarryingCapacity;
                int preyFactionCount = prey.Count;
                long preyBefore = (long)(preyRemaining + predated);
                ApplyPredationKills(prey, killed, consumer.PlanetFaction.Faction);
                region.CarryingCapacity = Math.Max(0, region.CarryingCapacity - stripped);
                RecordScenarioBlighting(region, stripped, consumer.PlanetFaction.Faction);
                long converted = (long)((killed + stripped) * BiomassFeedEfficiency);
                consumer.Population += converted;
                GameLog.Debug(() =>
                    $"Biomass consume {DescribeRegionFaction(consumer)}: troops={troops:F0}, "
                    + $"predated={killed} (prey {preyBefore} across {preyFactionCount} factions), "
                    + $"consumed={stripped} (capacity {capacityBefore}->{region.CarryingCapacity}), "
                    + $"converted={converted} (swarmPop {swarmPopBefore}->{consumer.Population})");
            }
        }

        private static void ApplyPredationKills(List<RegionFaction> prey, long totalKilled, Faction attacker)
        {
            if (totalKilled <= 0) return;
            long preyTotal = prey.Sum(rf => rf.Population);
            if (preyTotal <= 0) return;
            long applied = 0;
            for (int i = 0; i < prey.Count; i++)
            {
                RegionFaction rf = prey[i];
                long share = i == prey.Count - 1
                    ? totalKilled - applied
                    : (long)(totalKilled * (double)rf.Population / preyTotal);
                share = Math.Clamp(share, 0, rf.Population);
                rf.Population -= share;
                RecordScenarioCivilianKills(rf, share, attacker);
                applied += share;
            }
        }

        internal static void RecoverCarryingCapacity(Region region)
        {
            if (region.CarryingCapacity >= region.MaximumCarryingCapacity) return;
            bool publicSwarmPresent = region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && rf.PlanetFaction.Faction.GrowthType == GrowthType.Consumption
                && rf.Population > 0);
            if (publicSwarmPresent) return;
            long gap = region.MaximumCarryingCapacity - region.CarryingCapacity;
            long recovered = (long)(gap * CarryingCapacityRecoveryRate);
            if (recovered <= 0) recovered = 1;
            long capacityBefore = region.CarryingCapacity;
            region.CarryingCapacity = Math.Min(region.MaximumCarryingCapacity, region.CarryingCapacity + recovered);
            GameLog.Trace(() =>
                $"Capacity recovery {region.Planet.Name}/{region.Name}: "
                + $"{capacityBefore}->{region.CarryingCapacity} (+{region.CarryingCapacity - capacityBefore} "
                + $"toward ceiling {region.MaximumCarryingCapacity})");
        }

        internal static void EstablishInvaderPresence(Faction attacker, Region region, long survivors)
        {
            InvaderPresenceService.Establish(attacker, region, survivors);
        }

        internal static void ResolveCultManeuvers(Region region)
        {
            RegionFaction cult = region.RegionFactionMap.Values.FirstOrDefault(
                rf => rf.IsPublic && rf.PlanetFaction.Faction.GrowthType == GrowthType.Conversion);
            if (cult == null || cult.Population <= 0) return;

            int organization = Math.Max(cult.Organization, 0);
            long organized = (long)(cult.Population * (organization / 100.0));
            if (organized <= 0) return;

            if (HasActiveImperialEnemyNearby(region)) return;

            Region frontward = region.GetAdjacentRegions().FirstOrDefault(HasActiveImperialEnemyNearby);
            if (frontward != null)
            {
                RelocateCultForce(cult, frontward, organized);
                return;
            }

            SacrificialCultPredation(region, organized, cult.PlanetFaction.Faction);
        }

        private static bool HasActiveImperialEnemyNearby(Region region)
        {
            return region.GetSelfAndAdjacentRegions().Any(r => r.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && (rf.PlanetFaction.Faction.IsDefaultFaction || rf.PlanetFaction.Faction.IsPlayerFaction)
                && (rf.Garrison > 0 || rf.LandedSquads.Count > 0)));
        }

        private static void RelocateCultForce(RegionFaction cult, Region destination, long organized)
        {
            long movers = Math.Min(cult.Population, (long)(organized * CultRelocationRate));
            if (movers <= 0) return;
            cult.Population -= movers;

            Faction cultFaction = cult.PlanetFaction.Faction;
            if (!destination.RegionFactionMap.TryGetValue(cultFaction.Id, out RegionFaction destCult))
            {
                if (!destination.Planet.PlanetFactionMap.TryGetValue(cultFaction.Id, out PlanetFaction destPlanetFaction))
                {
                    destPlanetFaction = new PlanetFaction(cultFaction) { IsPublic = true };
                    destination.Planet.PlanetFactionMap[cultFaction.Id] = destPlanetFaction;
                }
                destCult = new RegionFaction(destPlanetFaction, destination)
                {
                    IsPublic = true,
                    Organization = Math.Max(cult.Organization, 0)
                };
                destination.RegionFactionMap[cultFaction.Id] = destCult;
            }
            destCult.IsPublic = true;
            destCult.Population += movers;
        }

        private static void SacrificialCultPredation(Region region, long organized, Faction attacker)
        {
            List<RegionFaction> prey = region.RegionFactionMap.Values
                .Where(rf => rf.PlanetFaction.Faction.IsDefaultFaction && rf.Population > 0)
                .ToList();
            double preyRemaining = prey.Sum(rf => rf.Population);
            if (preyRemaining <= 0) return;

            long killed = (long)Math.Min(preyRemaining, organized * PredationMarginalYield(preyRemaining));
            ApplyPredationKills(prey, killed, attacker);
        }

        internal static void UpdateImperialRemnantState(Region region)
        {
            RegionFaction defaultFaction = region.RegionFactionMap.Values
                .FirstOrDefault(rf => rf.PlanetFaction.Faction.IsDefaultFaction);
            if (defaultFaction == null) return;

            bool publicEnemy = HasPublicEnemy(region);
            if (defaultFaction.IsPublic)
            {
                if (defaultFaction.Garrison <= 0 && publicEnemy)
                {
                    defaultFaction.IsPublic = false;
                    defaultFaction.HalveDefensesOnGoingToGround();
                }
            }
            else if (!publicEnemy || LoyalStrengthOutweighsEnemy(region))
            {
                defaultFaction.IsPublic = true;
            }
        }

        private static bool LoyalStrengthOutweighsEnemy(Region region)
        {
            long loyal = 0;
            long enemy = 0;
            foreach (RegionFaction rf in region.RegionFactionMap.Values)
            {
                Faction faction = rf.PlanetFaction.Faction;
                if (faction.IsDefaultFaction || faction.IsPlayerFaction)
                {
                    loyal += rf.MilitaryStrength;
                    loyal += SquadBattleValue(rf.LandedSquads);
                }
                else
                {
                    enemy += rf.MilitaryStrength;
                }
            }
            return loyal > 0 && loyal > enemy;
        }

        internal static void DecayUnmannedDefenses(Region region)
        {
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
            {
                if (regionFaction.IsPublic) continue;
                if (regionFaction.Entrenchment <= 0
                    && regionFaction.ListeningPost <= 0
                    && regionFaction.AntiAir <= 0)
                {
                    continue;
                }

                bool hiddenIsImperial = regionFaction.PlanetFaction.Faction.IsPlayerFaction
                    || regionFaction.PlanetFaction.Faction.IsDefaultFaction;
                bool occupierPresent = region.RegionFactionMap.Values.Any(other =>
                    other.IsPublic
                    && other.MilitaryStrength > 0
                    && (other.PlanetFaction.Faction.IsPlayerFaction
                        || other.PlanetFaction.Faction.IsDefaultFaction) != hiddenIsImperial);
                if (!occupierPresent) continue;

                regionFaction.Entrenchment = Math.Max(0.0, regionFaction.Entrenchment - OccupiedDefenseDecayPerTurn);
                regionFaction.ListeningPost = Math.Max(0.0, regionFaction.ListeningPost - OccupiedDefenseDecayPerTurn);
                regionFaction.AntiAir = Math.Max(0.0, regionFaction.AntiAir - OccupiedDefenseDecayPerTurn);
                GameLog.Trace(() =>
                    $"Occupation decay {DescribeRegionFaction(regionFaction)}: "
                    + $"ent={regionFaction.Entrenchment:F2}, lp={regionFaction.ListeningPost:F2}, "
                    + $"aa={regionFaction.AntiAir:F2}");
            }
        }

        private static void RemoveEmptyRegionFactions(Region region)
        {
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values.ToList())
            {
                if (CanRemoveRegionFaction(regionFaction))
                {
                    region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
                }
            }
        }

        private static bool CanRemoveRegionFaction(RegionFaction regionFaction)
        {
            return regionFaction.Population <= 0
                && regionFaction.Garrison <= 0
                && regionFaction.LandedSquads.Count == 0
                && regionFaction.Entrenchment <= 0
                && regionFaction.ListeningPost <= 0
                && regionFaction.AntiAir <= 0;
        }

        private static bool HasPublicEnemy(Region region)
        {
            return region.RegionFactionMap.Values.Any(rf =>
                rf.IsPublic
                && !rf.PlanetFaction.Faction.IsPlayerFaction
                && !rf.PlanetFaction.Faction.IsDefaultFaction
                && (rf.Population > 0 || rf.Garrison > 0));
        }

        internal static void ProcessImperialEmigration(Region region)
        {
            RegionFaction remnant = region.RegionFactionMap.Values.FirstOrDefault(
                rf => rf.PlanetFaction.Faction.IsDefaultFaction && !rf.IsPublic);
            if (remnant == null || remnant.Population <= 0) return;

            List<RegionFaction> refuges = region.GetAdjacentRegions()
                .Select(r => r.RegionFactionMap.Values.FirstOrDefault(
                    rf => rf.PlanetFaction.Faction.IsDefaultFaction && rf.IsPublic))
                .Where(rf => rf != null)
                .ToList();
            if (refuges.Count == 0) return;

            long available = remnant.Population - remnant.Garrison;
            long emigrants = (long)(available * ImperialEmigrationRate);
            if (emigrants <= 0) return;
            remnant.Population -= emigrants;
            RecordScenarioImmigration(region, -emigrants);

            long refugeTotal = refuges.Sum(rf => rf.Population);
            long distributed = 0;
            for (int i = 0; i < refuges.Count; i++)
            {
                long share = i == refuges.Count - 1
                    ? emigrants - distributed
                    : refugeTotal > 0
                        ? (long)(emigrants * (double)refuges[i].Population / refugeTotal)
                        : emigrants / refuges.Count;
                refuges[i].Population += share;
                RecordScenarioImmigration(refuges[i].Region, share);
                distributed += share;
            }
        }

        private void CheckForPlanetaryRevolt(Planet planet)
        {
            Faction controllingFaction = planet.GetControllingFaction();
            PlanetFaction controllingPlanetFaction = planet.PlanetFactionMap[controllingFaction.Id];
            Faction hiddenFactionType = null;
            PlanetFaction hiddenPlanetFaction = null;

            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                if (!planetFaction.IsPublic
                    && !planetFaction.Faction.IsDefaultFaction
                    && !planetFaction.Faction.IsPlayerFaction)
                {
                    hiddenFactionType = planetFaction.Faction;
                    hiddenPlanetFaction = planetFaction;
                    break;
                }
            }

            if (hiddenPlanetFaction != null)
            {
                long hiddenFactionStrength = 0;
                long controllingFactionStrength = 0;

                foreach (Region region in planet.Regions)
                {
                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                    {
                        if (regionFaction.PlanetFaction == controllingPlanetFaction)
                        {
                            controllingFactionStrength += regionFaction.MilitaryStrength;
                        }
                        else if (regionFaction.PlanetFaction == hiddenPlanetFaction)
                        {
                            hiddenFactionStrength += regionFaction.MilitaryStrength;
                        }
                    }
                }

                if (hiddenFactionStrength > controllingFactionStrength)
                {
                    foreach (Region region in planet.Regions)
                    {
                        if (region.RegionFactionMap.ContainsKey(hiddenFactionType.Id))
                        {
                            RegionFaction revoltingRegionFaction = region.RegionFactionMap[hiddenFactionType.Id];
                            revoltingRegionFaction.IsPublic = true;
                            revoltingRegionFaction.Population += revoltingRegionFaction.Garrison;
                            revoltingRegionFaction.Garrison = 0;
                            if (region.RegionFactionMap.ContainsKey(controllingFaction.Id))
                            {
                                RegionFaction controllingRegionFaction = region.RegionFactionMap[controllingFaction.Id];
                                if (controllingRegionFaction.ListeningPost > 0)
                                {
                                    double revoltShare = Math.Clamp(
                                        controllingRegionFaction.ListeningPost / 2.0 + _session.Random.NextRandomZValue(),
                                        0.0, controllingRegionFaction.ListeningPost);
                                    controllingRegionFaction.ListeningPost -= revoltShare;
                                    revoltingRegionFaction.ListeningPost += revoltShare;
                                }
                                if (controllingRegionFaction.AntiAir > 0)
                                {
                                    double revoltShare = Math.Clamp(
                                        controllingRegionFaction.AntiAir / 2.0 + _session.Random.NextRandomZValue(),
                                        0.0, controllingRegionFaction.AntiAir);
                                    controllingRegionFaction.AntiAir -= revoltShare;
                                    revoltingRegionFaction.AntiAir += revoltShare;
                                }
                                if (controllingRegionFaction.Entrenchment > 0)
                                {
                                    double revoltShare = Math.Clamp(
                                        controllingRegionFaction.Entrenchment / 2.0 + _session.Random.NextRandomZValue(),
                                        0.0, controllingRegionFaction.Entrenchment);
                                    controllingRegionFaction.Entrenchment -= revoltShare;
                                    revoltingRegionFaction.Entrenchment += revoltShare;
                                }
                                controllingRegionFaction.Organization = (int)(_session.Random.GetLinearDouble() * 100);
                            }
                        }
                    }
                    hiddenPlanetFaction.IsPublic = true;
                }
            }
        }

        private void CheckForRevoltSuppression(Planet planet)
        {
            Faction controllingFaction = planet.GetControllingFaction();
            if (controllingFaction.IsPlayerFaction) return;
            PlanetFaction controllingPlanetFaction = planet.PlanetFactionMap[controllingFaction.Id];

            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                if (!planetFaction.IsPublic
                    || planetFaction == controllingPlanetFaction
                    || planetFaction.Faction.IsDefaultFaction
                    || planetFaction.Faction.IsPlayerFaction
                    || planetFaction.Faction.GrowthType != GrowthType.Conversion)
                {
                    continue;
                }

                long hostileStrength = SumMilitaryStrength(planet, planetFaction);
                long controllingStrength = SumMilitaryStrength(planet, controllingPlanetFaction);
                if (hostileStrength < 0.7f * controllingStrength)
                {
                    planetFaction.IsPublic = false;
                    foreach (Region region in planet.Regions)
                    {
                        if (region.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf))
                        {
                            rf.IsPublic = false;
                            rf.HalveDefensesOnGoingToGround();
                        }
                    }
                }
            }
        }

        private static long SumMilitaryStrength(Planet planet, PlanetFaction planetFaction)
        {
            long strength = 0;
            foreach (Region region in planet.Regions)
            {
                if (region.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf))
                {
                    strength += rf.MilitaryStrength;
                }
            }
            return strength;
        }

        private void UpdateRegionIntel(Planet planet)
        {
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                foreach (Region region in planetFaction.RegionIntel.Keys.ToList())
                {
                    planetFaction.SetRegionIntel(region, planetFaction.GetRegionIntel(region) * IntelDecayRate);
                }
            }

            foreach (Region region in planet.Regions)
            {
                foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                {
                    float gain = (float)(regionFaction.ListeningPost * IntelPerListeningPostLevel);
                    int patrolStrength = regionFaction.LandedSquads
                        .Where(s => s.CurrentOrders?.Mission.MissionType == MissionType.Patrol)
                        .Sum(s => s.Members.Count);
                    if (patrolStrength > 0)
                    {
                        gain += IntelPatrolBaseGain + (float)Math.Log10(patrolStrength);
                    }
                    RecordIntelGain(regionFaction.PlanetFaction, region, gain);
                }
            }

            _intelLedger.Apply(planet);
        }

        internal void UpdateIntelligence(IEnumerable<Planet> planets)
        {
            foreach (Planet planet in planets)
            {
                UpdateIntelligence(planet);
            }
        }

        internal void UpdateIntelligence(Planet planet)
        {
            foreach (Region region in planet.Regions)
            {
                float visibleIntel = region.GetPlayerVisibleIntel();
                if (visibleIntel <= 0)
                {
                    region.SpecialMissions.Clear();
                    continue;
                }

                foreach (Mission mission in region.SpecialMissions.ToList())
                {
                    if (_session.Random.GetIntBelowMax(0, 4) == 0)
                    {
                        region.SpecialMissions.Remove(mission);
                    }
                }
                if (visibleIntel > 0)
                {
                    float regionSpecMissionBudget = (float)Math.Log(visibleIntel, 2) + 1;
                    List<RegionFaction> publicEnemyFactions = region.RegionFactionMap.Values
                        .Where(rf => !rf.PlanetFaction.Faction.IsPlayerFaction
                                     && !rf.PlanetFaction.Faction.IsDefaultFaction
                                     && rf.IsPublic)
                        .ToList();
                    long totalDeployedStrength = publicEnemyFactions.Sum(rf => rf.GetDeployedStrength());

                    foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                    {
                        if (regionFaction.PlanetFaction.Faction.IsPlayerFaction
                            || regionFaction.PlanetFaction.Faction.IsDefaultFaction)
                        {
                            continue;
                        }
                        if (regionFaction.IsPublic)
                        {
                            float share = totalDeployedStrength > 0
                                ? (float)regionFaction.GetDeployedStrength() / totalDeployedStrength
                                : 1.0f / publicEnemyFactions.Count;
                            HandlePublicFactionIntelligence(regionFaction, regionSpecMissionBudget * share);
                        }
                        else
                        {
                            HandleHiddenFactionIntelligence(regionFaction);
                        }
                    }
                }
            }
        }

        internal void HandlePublicFactionIntelligence(RegionFaction enemyRegionFaction, float specMissionBudget)
        {
            float specMissionChance = specMissionBudget;
            specMissionChance -= enemyRegionFaction.Region.SpecialMissions
                .Count(m => m.RegionFaction == enemyRegionFaction);
            for (int i = 0; i < specMissionChance; i++)
            {
                double chance = _session.Random.NextRandomZValue();
                if (chance >= 2)
                {
                    GenerateAssassinationMission(enemyRegionFaction);
                }
                else if (chance >= 1)
                {
                    double defenseTotal = enemyRegionFaction.Entrenchment
                        + enemyRegionFaction.ListeningPost
                        + enemyRegionFaction.AntiAir;
                    if (defenseTotal <= 0)
                    {
                        GenerateAmbushMission(enemyRegionFaction);
                    }
                    else
                    {
                        GenerateSabotageMission(enemyRegionFaction, defenseTotal);
                    }
                }
                else if (chance >= 0)
                {
                    GenerateAmbushMission(enemyRegionFaction);
                }
            }
        }

        internal void HandleHiddenFactionIntelligence(RegionFaction enemyRegionFaction)
        {
            long regionPopulation = Math.Max(1, enemyRegionFaction.Region.Population);
            float popRatio = Math.Clamp(
                (float)enemyRegionFaction.Population / regionPopulation, 0.0001f, 0.9999f);
            float zScore = GaussianCalculator.ApproximateInverseNormalCDF(popRatio);
            zScore += enemyRegionFaction.Region.GetPlayerVisibleIntel() / 10.0f;
            double chance = _session.Random.NextRandomZValue();
            if (chance < zScore)
            {
                int size = Math.Max((int)(zScore - chance), 1);
                enemyRegionFaction.Region.SpecialMissions.Add(
                    new Mission(MissionType.Extermination, enemyRegionFaction, size));
            }
        }

        private void GenerateAmbushMission(RegionFaction enemyRegionFaction)
        {
            double maxSize = Math.Log10(enemyRegionFaction.Garrison);
            int size = Math.Min(Math.Max((int)_session.Random.NextRandomZValue() + 1, 1), (int)maxSize);
            Mission ambush = new Mission(MissionType.Ambush, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(ambush);
            _specialMissions.Add(ambush);
        }

        private void GenerateSabotageMission(RegionFaction enemyRegionFaction, double defenseTotal)
        {
            double roll = _session.Random.GetLinearDouble() * defenseTotal;
            if (roll <= enemyRegionFaction.Entrenchment)
            {
                AddSabotageMission(
                    enemyRegionFaction,
                    DefenseType.Entrenchment,
                    enemyRegionFaction.Entrenchment);
            }
            else if (roll - enemyRegionFaction.Entrenchment <= enemyRegionFaction.ListeningPost)
            {
                AddSabotageMission(
                    enemyRegionFaction,
                    DefenseType.ListeningPost,
                    enemyRegionFaction.ListeningPost);
            }
            else
            {
                AddSabotageMission(enemyRegionFaction, DefenseType.AntiAir, enemyRegionFaction.AntiAir);
            }
        }

        private void AddSabotageMission(
            RegionFaction enemyRegionFaction,
            DefenseType defenseType,
            double defenseLevel)
        {
            int size = Math.Min(
                Math.Max((int)_session.Random.NextRandomZValue() + 1, 1),
                (int)Math.Ceiling(defenseLevel));
            SabotageMission sabotage = new SabotageMission(defenseType, size, enemyRegionFaction);
            enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
            _specialMissions.Add(sabotage);
        }

        private void GenerateAssassinationMission(RegionFaction enemyRegionFaction)
        {
            int max = (int)Math.Log10(enemyRegionFaction.Population);
            int size = Math.Min(Math.Max((int)_session.Random.NextRandomZValue() + 1, 1), max);
            Mission assassination = new Mission(MissionType.Assassination, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(assassination);
            _specialMissions.Add(assassination);
        }

        private static string DescribeRegionFaction(RegionFaction regionFaction)
        {
            if (regionFaction == null) return "unknown";
            return $"{regionFaction.Region.Planet.Name}/{regionFaction.Region.Name}/"
                + $"{regionFaction.PlanetFaction.Faction.Name}";
        }

        private static long SquadBattleValue(IEnumerable<Squad> squads)
        {
            return squads
                .SelectMany(squad => squad.Members)
                .Sum(member => (long)member.Template.BattleValue);
        }

        private static void RecordScenarioNaturalPopulationChange(RegionFaction regionFaction, long delta)
        {
            ScenarioMetricsCollector.RecordScenarioNaturalPopulationChange(regionFaction, delta);
        }

        private static void RecordScenarioImmigration(Region region, long delta)
        {
            ScenarioMetricsCollector.RecordScenarioImmigration(region, delta);
        }

        private static void RecordScenarioCivilianKills(
            RegionFaction regionFaction,
            long killed,
            Faction attacker)
        {
            ScenarioMetricsCollector.RecordScenarioCivilianKills(regionFaction, killed, attacker);
        }

        private static void RecordScenarioPdfDrafted(RegionFaction regionFaction, long drafted)
        {
            ScenarioMetricsCollector.RecordScenarioPdfDrafted(regionFaction, drafted);
        }

        private static void RecordScenarioBlighting(Region region, long stripped, Faction consumer)
        {
            ScenarioMetricsCollector.RecordScenarioBlighting(region, stripped, consumer);
        }
    }
}
