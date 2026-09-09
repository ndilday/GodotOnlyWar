using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Supply;
using OnlyWar.Domain.FactionBehaviors;
using OnlyWar.Runtime.Naming;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// Orchestrates the planet-scoped portion of a campaign turn. The ordering in
    /// <see cref="UpdatePlanet"/> is deliberate: every phase observes the state produced by the
    /// preceding phase.
    /// </summary>
    internal sealed class PlanetTurnProcessor
    {
        private PlanetDemographicsProcessor _demographicsProcessor;
        private RegionControlTurnProcessor _regionControlProcessor;
        private ConversionTurnProcessor _conversionProcessor;
        private PlanetIntelligenceProcessor _intelligenceProcessor;
        private GovernorTurnProcessor _governorTurnProcessor;
        private CivilUnrestTurnProcessor _civilUnrestTurnProcessor;

        internal PlanetTurnProcessor(
            ICampaignSimulationSession session,
            PlanetIntelligenceProcessor intelligenceProcessor = null,
            OrganicPopulationGrowthLedger growthLedger = null,
            ICollection<FortificationTransferReport> fortificationTransfers = null,
            ICollection<GovernorRequestReport> governorRequestReports = null,
            NameGenerator nameGenerator = null)
        {
            Initialize(
                CampaignTurnContext.From(session, nameGenerator),
                intelligenceProcessor,
                growthLedger,
                fortificationTransfers,
                governorRequestReports,
                nameGenerator);
        }

        internal PlanetTurnProcessor(
            CampaignTurnContext turn,
            PlanetIntelligenceProcessor intelligenceProcessor = null,
            OrganicPopulationGrowthLedger growthLedger = null,
            ICollection<FortificationTransferReport> fortificationTransfers = null,
            ICollection<GovernorRequestReport> governorRequestReports = null,
            NameGenerator nameGenerator = null)
        {
            Initialize(
                turn,
                intelligenceProcessor,
                growthLedger,
                fortificationTransfers,
                governorRequestReports,
                nameGenerator);
        }

        private void Initialize(
            CampaignTurnContext turn,
            PlanetIntelligenceProcessor intelligenceProcessor,
            OrganicPopulationGrowthLedger growthLedger,
            ICollection<FortificationTransferReport> fortificationTransfers,
            ICollection<GovernorRequestReport> governorRequestReports,
            NameGenerator nameGenerator)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            growthLedger ??= new OrganicPopulationGrowthLedger();
            _intelligenceProcessor = intelligenceProcessor
                ?? new PlanetIntelligenceProcessor(turn, new List<Mission>());
            _demographicsProcessor = new PlanetDemographicsProcessor(turn, growthLedger);
            _regionControlProcessor = new RegionControlTurnProcessor(fortificationTransfers);
            _conversionProcessor = new ConversionTurnProcessor(turn);
            _governorTurnProcessor = new GovernorTurnProcessor(
                turn, governorRequestReports, nameGenerator);
            _civilUnrestTurnProcessor = new CivilUnrestTurnProcessor(turn);
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
            // Public consumption factions expand and feed through planned mission taskings. Hidden
            // consumers have no planner, so their whole deployed strength remains available here.
            ConsumptionTurnProcessor.ResolveHiddenExpansion(planet);

            foreach (Region region in planet.Regions)
            {
                float pdfRatio = region.PlanetaryDefenseForces / (float)region.Population;
                foreach (RegionFaction regionFaction in region.RegionFactionMap.Values.ToList())
                {
                    if (RegionControlTurnProcessor.CanRemoveRegionFaction(regionFaction))
                    {
                        region.RegionFactionMap.Remove(regionFaction.PlanetFaction.Faction.Id);
                    }
                    else
                    {
                        _demographicsProcessor.ProcessRegionFaction(regionFaction, pdfRatio);
                    }
                }

                ConsumptionTurnProcessor.ResolveHiddenFeeding(region);
                ConsumptionTurnProcessor.RecoverCarryingCapacity(region);
                _regionControlProcessor.SettleRegion(region);
            }

            RegionControlTurnProcessor.ProcessImperialRemnants(planet);
            _conversionProcessor.ProcessPlanet(planet);
            _civilUnrestTurnProcessor.ProcessPlanet(planet);

            RemoveEmptyPlanetFactions(planet);
            _intelligenceProcessor.ApplyAwareness(planet);
            ProcessGovernors(planet);
        }

        private void RemoveEmptyPlanetFactions(Planet planet)
        {
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values.ToList())
            {
                bool hasRegionalPresence = planet.Regions.Any(region =>
                    region.RegionFactionMap.ContainsKey(planetFaction.Faction.Id));
                bool hasIntel = planetFaction.HasIntelligenceFootprint
                    || _intelligenceProcessor.HasPendingEntries(planetFaction, planet);
                if (!hasRegionalPresence && !hasIntel)
                {
                    planet.PlanetFactionMap.Remove(planetFaction.Faction.Id);
                }
            }
        }

        private void ProcessGovernors(Planet planet)
        {
            Faction controllingFaction = planet.GetControllingFaction();
            bool invasionControlled = FactionCapabilities.GeneratesInvasions(controllingFaction);
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values.ToList())
            {
                long population = planet.Regions.Sum(region =>
                    region.RegionFactionMap.TryGetValue(
                        planetFaction.Faction.Id,
                        out RegionFaction regionFaction)
                        ? regionFaction.Population
                        : 0);
                if (population > 0
                    && planetFaction.Leader != null
                    && !(invasionControlled && planetFaction.Faction.IsDefaultFaction))
                {
                    _governorTurnProcessor.ProcessGovernor(planet, planetFaction);
                }
            }
        }
    }
}

