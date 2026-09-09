using OnlyWar.Generation.Abstractions;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Domain.Extensions;
using OnlyWar.Domain.Events;
using OnlyWar.Runtime.WorldGeometry;

namespace OnlyWar.Generation.World
{
    /// <summary>
    /// Builds a complete candidate sector. Generation constructs state and returns it; it never
    /// publishes a campaign, and every campaign capability it needs — seeding, narrative, fleet
    /// stationing, founding roles, training and the authored warm-up simulation — arrives through
    /// <see cref="GenerationSupport"/> rather than being reached for directly (SB-09).
    /// </summary>
    public static class SectorBuilder
    {
        public static Sector GenerateSector(int seed, GameRulesData data, Date currentDate,
                                            GenerationSupport support,
                                            string chapterName = null,
                                            ScenarioFactionSelection invaderSelection = null)
        {
            if (support == null) throw new ArgumentNullException(nameof(support));
            List<Planet> planetList = [];
            List<Character> characterList = [];
            List<TaskForce> forceList = [];

            RNG.Reset(seed);
            NameGenerator.Reset();
            PlanetBuilder planetBuilder = new(support.Identity);

            SectorGenerationProfile profile = data.SectorGenerationProfile;
            for (ushort j = 0; j < profile.SectorHeight; j++)
            {
                for (ushort i = 0; i < profile.SectorWidth; i++)
                {
                    double random = RNG.GetLinearDouble();
                    if (random <= profile.PlanetSpawnProbability)
                    {
                        Planet planet = GeneratePlanet(planetBuilder, new Coordinate(i, j), data);
                        planetList.Add(planet);

                        if (planet.PlanetFactionMap[planet.GetControllingFaction().Id].Leader != null)
                        {
                            Character leader =
                                planet.PlanetFactionMap[planet.GetControllingFaction().Id].Leader;
                            characterList.Add(leader);
                        }
                    }
                }
            }

            Date trainingStartDate = new Date(currentDate.Millenium, currentDate.Year - 4, 1);
            PlayerForce playerForce = NewChapterBuilder.CreateChapter(
                data, support, trainingStartDate, currentDate, chapterName);
            playerForce.CampaignIdentity = CampaignIdentity.CreateNew(seed);

            // The scenario stamp resolves the sitting Sector Lord, so the sector and its derived
            // governance designation must exist first. The fleet starts empty here; the scenario
            // parks it in orbit via Sector.AddNewFleet (Design/Reference/OpeningScenario.md).
            Sector sector = new Sector(playerForce, characterList, planetList, forceList);
            SectorTopologyBuilder.Rebuild(sector, data);
            // Ambient ghost populations are latent state, not planets. Seed them after ordinary world
            // generation so they can only occupy genuinely empty grid tiles and never change the
            // sector's visible planet roster.
            support.Seeding.SeedGhostPopulations(sector, data);
            // The opening-scenario stamp runs its planet-scoped simulations against the candidate
            // sector's own session, so nothing is published until the caller installs the finished
            // campaign (SB-09).
            sector.Scenario = ScenarioBuilder.StampPromisedWorld(
                sector, data, currentDate, support, playerForce, planetList, characterList,
                invaderSelection);
            support.Narrative.ReconcileChronicle(playerForce);
            return sector;
        }

        private static Planet GeneratePlanet(
            PlanetBuilder planetBuilder,
            Coordinate position,
            GameRulesData data)
        {
            // Every generated world starts under the configured default-faction control unless a
            // data-authored public presence rule takes it over. Hidden and public faction starts are
            // applied by the same declarative policy surface, so adding a new faction does not
            // require another faction-specific branch here.
            Planet planet = planetBuilder.GenerateNewPlanet(
                data.PlanetTemplateMap, position, data.DefaultFaction);
            foreach (FactionPlanetPresenceRule rule in data.FactionPlanetPresence
                         .GetApplicableRules(SectorGenerationProfileKeys.Standard, planet.Template.Id))
            {
                if (RNG.GetLinearDouble() < rule.SpawnChance)
                {
                    Faction faction = data.Factions.First(faction => faction.Id == rule.FactionId);
                    PlanetBuilder.ApplyFactionPresence(faction, planet, rule);
                    SeedInitialFeralBelief(planet, data, faction, rule);
                }
            }
            return planet;
        }

            // Dormant population presence and Imperial knowledge are separate rolls. A seeded population is
        // therefore sometimes already suspected by local authorities and sometimes genuinely
        // unknown; neither case changes the hidden/open state of the RegionFaction itself.
        private static void SeedInitialFeralBelief(
            Planet planet,
            GameRulesData data,
            Faction faction,
            FactionPlanetPresenceRule rule)
        {
            if (!FactionCapabilities.HasDormantPopulations(faction)
                || rule.PresenceMode != FactionPresenceMode.Hidden
                || data.FactionBehaviorRules.DormantInitialBeliefChance <= 0
                || RNG.GetLinearDouble() >= data.FactionBehaviorRules.DormantInitialBeliefChance)
            {
                return;
            }

            PlanetFaction observer = planet.PlanetFactionMap.Values
                .FirstOrDefault(presence => presence.Faction.IsDefaultFaction);
            if (observer == null) return;

            foreach (Region region in planet.Regions)
            {
                RegionFaction target = region.RegionFactionMap.GetValueOrDefault(faction.Id);
                if (target?.Population <= 0) continue;
                observer.AddRegionAwareness(region, 1.0f);
                observer.SeedTargetBelief(
                    region,
                    faction,
                    (float)data.FactionBehaviorRules.DormantInitialBeliefEvidence,
                    estimatedPopulation: null,
                    estimatedMilitaryStrength: null,
                    evidenceWeek: 0,
                    source: IntelObservationSource.Scenario);
            }
        }

    }
}
