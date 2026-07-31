using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Fortifications;
using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Missions.Ambush;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Supply;
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

        private readonly GameSession _session;
        private readonly List<Mission> _specialMissions;
        private readonly TurnIntelLedger _intelLedger;
        private readonly GovernorTurnProcessor _governorTurnProcessor;
        private readonly CivilUnrestTurnProcessor _civilUnrestTurnProcessor;
        private readonly Dictionary<(int PlanetId, int FactionId), long> _organicPopulationGrowth = [];
        private readonly ICollection<FortificationTransferReport> _fortificationTransfers;

        internal PlanetTurnProcessor(
            GameSession session,
            List<Mission> specialMissions,
            TurnIntelLedger intelLedger = null,
            ICollection<FortificationTransferReport> fortificationTransfers = null,
            ICollection<GovernorRequestReport> governorRequestReports = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _specialMissions = specialMissions ?? throw new ArgumentNullException(nameof(specialMissions));
            _fortificationTransfers = fortificationTransfers;
            _intelLedger = intelLedger ?? new TurnIntelLedger();
            _governorTurnProcessor = new GovernorTurnProcessor(_session, governorRequestReports);
            _civilUnrestTurnProcessor = new CivilUnrestTurnProcessor(_session);
        }

        internal void ClearTurnIntelGains()
        {
            _intelLedger.Clear();
        }

        // Recruitment must use demographic growth rather than the world's raw before/after
        // population delta (which also contains battle deaths, migration, conversion, and
        // consumption). The turn controller clears this ledger once per campaign week and the
        // recruitment processor reads the Home World's player-faction entry after planet updates.
        internal void ClearOrganicPopulationGrowth()
        {
            _organicPopulationGrowth.Clear();
        }

        internal long GetOrganicPopulationGrowth(int planetId, int factionId)
        {
            return _organicPopulationGrowth.TryGetValue((planetId, factionId), out long growth)
                ? growth
                : 0;
        }

        internal void RecordIntelGain(PlanetFaction planetFaction, Region region, float gain)
        {
            _intelLedger.RecordGain(planetFaction, region, gain);
        }

        internal void RecordReconEvidence(PlanetFaction planetFaction, Region region, float evidence)
        {
            _intelLedger.RecordReconEvidence(planetFaction, region, evidence);
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
                // Before anything is decayed or swept away: a faction that has left the region but
                // whose works still stand hands them to an ally still holding the ground. This is
                // what stops a squad marching out of a region it fortified from stranding those
                // works on a presence-less RegionFaction that nothing can then use or clear.
                TransferAbandonedWorksToAllies(region);
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
            _civilUnrestTurnProcessor.ProcessPlanet(planet);

            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values.ToList())
            {
                long planetFactionPopulation = planet.Regions.Sum(
                    r => r.RegionFactionMap.TryGetValue(planetFaction.Faction.Id, out RegionFaction rf)
                        ? rf.Population
                        : 0);
                bool hasRegionalPresence = planet.Regions.Any(region =>
                    region.RegionFactionMap.ContainsKey(planetFaction.Faction.Id));
                bool hasIntel = planetFaction.RegionIntel.Count > 0
                    || _intelLedger.HasPendingEntries(planetFaction, planet);
                if (!hasRegionalPresence && !hasIntel)
                {
                    planet.PlanetFactionMap.Remove(planetFaction.Faction.Id);
                }
                else if (planetFactionPopulation > 0 && planetFaction.Leader != null)
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
                        if (controllingFaction != null
                            && regionFaction.PlanetFaction.Faction.Id != controllingFaction.Id &&
                            planet.PlanetFactionMap[controllingFaction.Id].Leader != null)
                        {
                            // Governor detection of converted population remains future work.
                        }
                    }
                    break;
                case GrowthType.Consumption:
                    newPop = 0;
                    break;
                case GrowthType.Unrest:
                    // Secular allegiance, embedded-PDF recruitment, and civilian arming are
                    // processed together by CivilUnrestTurnProcessor after ordinary growth.
                    return;
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
            (int PlanetId, int FactionId) growthKey =
                (regionFaction.Region.Planet.Id, regionFaction.PlanetFaction.Faction.Id);
            _organicPopulationGrowth[growthKey] =
                GetOrganicPopulationGrowth(growthKey.PlanetId, growthKey.FactionId) + grown;
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
                && !FactionDispositionService.IsImperial(other.PlanetFaction.Faction));
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
                && FactionDispositionService.IsImperial(rf.PlanetFaction.Faction)
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
                    // If the Chapter (or another ally) still stands in the region, the works pass
                    // to it intact rather than being abandoned - a garrison dying under a bastion
                    // its allies still man does not level the bastion. Only when nobody is left to
                    // hold them do the works fall to the occupier (RegionDefenses.TransferToAlly).
                    if (RegionDefenses.TransferToAlly(defaultFaction) == null)
                    {
                        defaultFaction.HalveDefensesOnGoingToGround();
                    }
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
                if (FactionDispositionService.IsImperial(faction))
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

                bool occupierPresent = region.RegionFactionMap.Values.Any(other =>
                    other.IsPublic
                    && other.MilitaryStrength > 0
                    && !FactionDispositionService.AreAllied(
                        other.PlanetFaction.Faction,
                        regionFaction.PlanetFaction.Faction));
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

        /// <summary>
        /// Hands over the works of any faction that still holds fortifications in this region but
        /// no longer has anyone there to man them.
        /// </summary>
        /// <remarks>
        /// Without this, works outlive their builder's presence indefinitely: CanRemoveRegionFaction
        /// refuses to clear a RegionFaction while its defenses are above zero, so a squad leaving a
        /// region it fortified used to strand those works on a presence-less faction where neither
        /// side could use them and nothing would ever tidy them away. Transferring in point space
        /// leaves the side's shared position untouched (RegionDefenses.TransferToAlly), so this is
        /// a change of custody rather than a change of strength.
        /// </remarks>
        private void TransferAbandonedWorksToAllies(Region region)
        {
            foreach (RegionFaction regionFaction in region.RegionFactionMap.Values.ToList())
            {
                if (regionFaction.MilitaryStrength > 0) continue;
                if (regionFaction.LandedSquads.Count > 0) continue;
                if (!RegionDefenses.HasAnyWorks(regionFaction)) continue;

                RegionFaction inheritor = RegionDefenses.TransferToAlly(regionFaction);
                if (inheritor == null) continue;

                _fortificationTransfers?.Add(new FortificationTransferReport(
                    region,
                    regionFaction.PlanetFaction.Faction,
                    inheritor.PlanetFaction.Faction,
                    RegionDefenses.GetShared(inheritor, DefenseType.Entrenchment)));
                GameLog.Debug(() =>
                    $"Fortifications transferred {regionFaction.PlanetFaction.Faction.Name} -> "
                    + $"{inheritor.PlanetFaction.Faction.Name} in {region.Planet.Name}/{region.Name}");
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
                && !FactionDispositionService.IsImperial(rf.PlanetFaction.Faction)
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
            foreach (Region region in planet.Regions.Where(region => region != null))
            {
                RegionFaction loyalist = region.RegionFactionMap.Values.FirstOrDefault(rf =>
                    rf.IsPublic
                    && (rf.PlanetFaction.Faction.IsDefaultFaction
                        || rf.PlanetFaction.Faction.IsPlayerFaction));
                if (loyalist == null) continue;

                bool externalEnemyPresent = region.RegionFactionMap.Values.Any(rf =>
                    rf.IsPublic
                    && FactionDispositionService.IsExternalEnemy(rf.PlanetFaction.Faction));
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
                        planetFaction.Faction.Id, out RegionFaction rf) && rf.IsPublic);
            }
        }

        private void TransferRevoltDefenses(RegionFaction loyalist, RegionFaction revolting)
        {
            // A revolt takes the works over to the other side, so this stays a level-space seizure:
            // what the rebels gain, the loyalists lose outright. The stat order is load-bearing -
            // each draw pulls from the seeded RNG, so reordering it would silently change every
            // seeded revolt outcome.
            foreach (DefenseType defenseType in RevoltSeizureOrder)
            {
                double share = DrawRevoltDefenseShare(loyalist.GetDefense(defenseType));
                loyalist.AddDefense(defenseType, -share);
                revolting.AddDefense(defenseType, share);
            }
            loyalist.Organization = (int)(_session.Random.GetLinearDouble() * 100);
        }

        private static readonly DefenseType[] RevoltSeizureOrder =
        [
            DefenseType.ListeningPost,
            DefenseType.AntiAir,
            DefenseType.Entrenchment
        ];

        private double DrawRevoltDefenseShare(double defense) => defense <= 0
            ? 0
            : Math.Clamp(defense / 2.0 + _session.Random.NextRandomZValue(), 0.0, defense);

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
                    // A listening post is a STRUCTURE, so allied installations pool exactly the way
                    // every other kind of works does - on the logarithmic curve in RegionDefenses -
                    // and the result is applied to each ally directly. It deliberately bypasses the
                    // intel ledger: the ledger sums allied gains, which is right for activities but
                    // would count the same shared sensor net once per ally.
                    float sensorGain = (float)(
                        RegionDefenses.GetShared(regionFaction, DefenseType.ListeningPost)
                        * IntelPerListeningPostLevel);
                    if (sensorGain > 0f)
                    {
                        regionFaction.PlanetFaction.AddRegionIntel(region, sensorGain);
                    }

                    // Patrol deliberately grants NO intelligence (OnlyWar_TDD.md §6.4
                    // §5). It used to add a flat 1.0 + log10(headcount) here, which made it a strictly
                    // better intel source than Recon: this path routes through RecordIntelGain, which
                    // bypasses the diminishing-returns curve TurnIntelLedger applies to recon evidence,
                    // and it cost no roll, no risk, and no operating days. Patrolling your own region
                    // beat reconnoitring it.
                    //
                    // Patrol's product is DETECTION, not knowledge: it contributes search effort
                    // (RegionFaction.GetPatrolStrength -> MissionStealthDifficulty), it decides who
                    // spots an intruder (Region.SelectSpotter, which gates the player-side contact
                    // channel in NpcMissionReportBuilder), and it intercepts. Recon is the only source
                    // of regional intel.
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
                // Show of Force is not an intelligence find - it is a standing petition from the
                // world's own governor, delivered by astropath. Losing eyes on the ground does not
                // withdraw it, and it does not go stale, so it survives both the intel wipe and
                // the weekly expiry roll that clear ordinary opportunities. Only
                // GovernorTurnProcessor retires it, when the request resolves.
                if (visibleIntel <= 0)
                {
                    region.SpecialMissions.RemoveAll(
                        mission => mission.MissionType != MissionType.ShowOfForce);
                    continue;
                }

                foreach (Mission mission in region.SpecialMissions.ToList())
                {
                    if (mission.MissionType == MissionType.ShowOfForce) continue;
                    if (_session.Random.GetIntBelowMax(0, 4) == 0)
                    {
                        region.SpecialMissions.Remove(mission);
                    }
                }
                if (visibleIntel > 0)
                {
                    float regionSpecMissionBudget = (float)Math.Log(visibleIntel, 2) + 1;
                    List<RegionFaction> publicEnemyFactions = region.RegionFactionMap.Values
                        .Where(rf => !FactionDispositionService.IsImperial(rf.PlanetFaction.Faction)
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
                    // Weighted against the position as it actually stands - what a raider would see
                    // and pick a target from - which for an allied-held region is the pooled works.
                    double defenseTotal = RegionDefenses.GetShared(enemyRegionFaction, DefenseType.Entrenchment)
                        + RegionDefenses.GetShared(enemyRegionFaction, DefenseType.ListeningPost)
                        + RegionDefenses.GetShared(enemyRegionFaction, DefenseType.AntiAir);
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
            // Mission size is an order-of-magnitude band of the force being ambushed. Reading raw
            // Garrison made this Log10(0) = -infinity for a PopulationIsMilitary horde (Tyranids,
            // cults) whose army is its Population; the cast saturates to int.MinValue, so the
            // ambush was created with a wildly negative size that PositionAmbushMissionStep then
            // raised 10 to - producing a target battle value of 0, an ambush with no ambushers.
            int maxSize = (int)MissionStealthDifficulty.TroopMagnitude(
                enemyRegionFaction.GetDeployedStrength());
            int size = ClampMissionSize((int)_session.Random.NextRandomZValue() + 1, maxSize);
            long targetBattleValue =
                AmbushMissionSizing.RollTargetBattleValue(size, _session.Random);
            Mission ambush = new Mission(
                MissionType.Ambush,
                enemyRegionFaction,
                size,
                targetBattleValue);
            enemyRegionFaction.Region.SpecialMissions.Add(ambush);
            _specialMissions.Add(ambush);
        }

        private void GenerateSabotageMission(RegionFaction enemyRegionFaction, double defenseTotal)
        {
            double entrenchment = RegionDefenses.GetShared(enemyRegionFaction, DefenseType.Entrenchment);
            double listeningPost = RegionDefenses.GetShared(enemyRegionFaction, DefenseType.ListeningPost);
            double roll = _session.Random.GetLinearDouble() * defenseTotal;
            if (roll <= entrenchment)
            {
                AddSabotageMission(enemyRegionFaction, DefenseType.Entrenchment, entrenchment);
            }
            else if (roll - entrenchment <= listeningPost)
            {
                AddSabotageMission(enemyRegionFaction, DefenseType.ListeningPost, listeningPost);
            }
            else
            {
                AddSabotageMission(
                    enemyRegionFaction,
                    DefenseType.AntiAir,
                    RegionDefenses.GetShared(enemyRegionFaction, DefenseType.AntiAir));
            }
        }

        private void AddSabotageMission(
            RegionFaction enemyRegionFaction,
            DefenseType defenseType,
            double defenseLevel)
        {
            int size = ClampMissionSize(
                (int)_session.Random.NextRandomZValue() + 1,
                (int)Math.Ceiling(defenseLevel));
            SabotageMission sabotage = new SabotageMission(defenseType, size, enemyRegionFaction);
            enemyRegionFaction.Region.SpecialMissions.Add(sabotage);
            _specialMissions.Add(sabotage);
        }

        private void GenerateAssassinationMission(RegionFaction enemyRegionFaction)
        {
            // Size selects the target's rank from the faction's HQ ladder (1: Prime, 2: Broodlord,
            // 3: Hive Tyrant), so it scales with the faction's whole presence here rather than the
            // portion it has fielded - Population is the right pool. It still needs the same guard:
            // a region faction can be reduced to zero population and still be walked here by
            // HandlePublicFactionIntelligence, and Log10(0) casts to int.MinValue.
            int max = (int)MissionStealthDifficulty.TroopMagnitude(enemyRegionFaction.Population);
            int size = ClampMissionSize((int)_session.Random.NextRandomZValue() + 1, max);
            Mission assassination = new Mission(MissionType.Assassination, enemyRegionFaction, size);
            enemyRegionFaction.Region.SpecialMissions.Add(assassination);
            _specialMissions.Add(assassination);
        }

        // A generated mission's size, capped by how much there is to act against but never below 1.
        // The cap is derived from world state that can legitimately be zero (a spent garrison, an
        // undamaged-but-unbuilt defense), and a size of 0 or less is not a small mission - it is a
        // broken one: MissionAftermathProcessor clamps sabotage impact to the size, so nothing the
        // force achieves can ever count, and PositionAmbushMissionStep raises 10 to it to pick the
        // opposing force's battle value.
        private static int ClampMissionSize(int rolled, int maximum) =>
            Math.Clamp(rolled, 1, Math.Max(1, maximum));

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
