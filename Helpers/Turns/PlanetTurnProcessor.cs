using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Orchestrates the planet-scoped portion of a campaign turn. The ordering in
    /// <see cref="UpdatePlanet"/> is deliberate: every phase observes the state produced by the
    /// preceding phase.
    /// </summary>
    internal sealed class PlanetTurnProcessor
    {
        private readonly PlanetDemographicsProcessor _demographicsProcessor;
        private readonly RegionControlTurnProcessor _regionControlProcessor;
        private readonly ConversionTurnProcessor _conversionProcessor;
        private readonly PlanetIntelligenceProcessor _intelligenceProcessor;
        private readonly GovernorTurnProcessor _governorTurnProcessor;
        private readonly CivilUnrestTurnProcessor _civilUnrestTurnProcessor;

        internal PlanetTurnProcessor(
            GameSession session,
            PlanetIntelligenceProcessor intelligenceProcessor = null,
            OrganicPopulationGrowthLedger growthLedger = null,
            ICollection<FortificationTransferReport> fortificationTransfers = null,
            ICollection<GovernorRequestReport> governorRequestReports = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            growthLedger ??= new OrganicPopulationGrowthLedger();
            _intelligenceProcessor = intelligenceProcessor
                ?? new PlanetIntelligenceProcessor(session, new List<Mission>());
            _demographicsProcessor = new PlanetDemographicsProcessor(session, growthLedger);
            _regionControlProcessor = new RegionControlTurnProcessor(fortificationTransfers);
            _conversionProcessor = new ConversionTurnProcessor(session);
            _governorTurnProcessor = new GovernorTurnProcessor(session, governorRequestReports);
            _civilUnrestTurnProcessor = new CivilUnrestTurnProcessor(session);
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
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values.ToList())
            {
                long population = planet.Regions.Sum(region =>
                    region.RegionFactionMap.TryGetValue(
                        planetFaction.Faction.Id,
                        out RegionFaction regionFaction)
                        ? regionFaction.Population
                        : 0);
                if (population > 0 && planetFaction.Leader != null)
                {
                    _governorTurnProcessor.ProcessGovernor(planet, planetFaction);
                }
            }
        }
    }
}
