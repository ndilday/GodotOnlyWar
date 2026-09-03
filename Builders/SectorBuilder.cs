using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Turns;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Events;

namespace OnlyWar.Builders
{
    internal static class SectorBuilder
    {
        public static Sector GenerateSector(int seed, GameRulesData data, Date currentDate, string chapterName = null,
                                            InvaderFactionSelection invaderSelection = InvaderFactionSelection.Tyranids)
        {
            List<Planet> planetList = [];
            List<Character> characterList = [];
            List<TaskForce> forceList = [];

            RNG.Reset(seed);
            NameGenerator.Reset();
            PlanetBuilder.Instance.Reset();

            SectorGenerationProfile profile = data.SectorGenerationProfile;
            for (ushort j = 0; j < profile.SectorHeight; j++)
            {
                for (ushort i = 0; i < profile.SectorWidth; i++)
                {
                    double random = RNG.GetLinearDouble();
                    if (random <= profile.PlanetSpawnProbability)
                    {
                        Planet planet = GeneratePlanet(new Coordinate(i, j), data);
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
            RatingCalculator ratingCalculator = new(data.RatingDefinitions, data.RatingAwardTiers,
                                                    data.BaseSkillMap, StaticRNG.Instance);
            ISoldierTrainingService trainingService = new SoldierTrainingCalculator(
                data.BaseSkillMap.Values,
                data.TrainingProfiles.Values,
                ratingCalculator,
                data.Skills,
                data.ScoutTrainingOptions.Options);
            PlayerForce playerForce = NewChapterBuilder.CreateChapter(data, trainingService, trainingStartDate, currentDate, chapterName);
            playerForce.CampaignIdentity = CampaignIdentity.CreateNew(seed);

            // The scenario stamp resolves the sitting Sector Lord, so the sector and its derived
            // governance designation must exist first. The fleet starts empty here; the scenario
            // parks it in orbit via Sector.AddNewFleet (Design/Reference/OpeningScenario.md).
            Sector sector = new Sector(playerForce, characterList, planetList, forceList);
            GenerateWarpNetwork(sector, data);
            // Ambient ghost populations are latent state, not planets. Seed them after ordinary world
            // generation so they can only occupy genuinely empty grid tiles and never change the
            // sector's visible planet roster.
            GhostPlanetSeeder.Seed(sector, data, StaticRNG.Instance);
            // Register the in-progress sector so the opening-scenario stamp can run its planet-scoped
            // simulations (which read GameDataSingleton.Instance.Sector) before generation returns.
            GameDataSingleton.Instance.SetSectorDuringGeneration(sector);
            sector.Scenario = ScenarioBuilder.StampPromisedWorld(
                sector, data, currentDate, playerForce, planetList, characterList, invaderSelection);
            ChapterChronicleProjector.Reconcile(
                playerForce.CampaignEventLedger,
                playerForce.ChapterChronicle,
                playerForce.CampaignIdentity);
            return sector;
        }

        /// <summary>
        /// Builds the subsector layout and warp-lane network for a sector. Both are
        /// deterministic functions of the planet positions, so this is run for freshly
        /// generated sectors and for sectors restored from a save alike, rather than
        /// being persisted.
        /// </summary>
        public static void GenerateWarpNetwork(Sector sector, GameRulesData data)
        {
            SectorGenerationProfile profile = data.SectorGenerationProfile;
            Godot.Vector2I gridDimensions = new(profile.SectorWidth, profile.SectorHeight);
            List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors(
                sector.Planets.Values,
                gridDimensions,
                profile.MaxSubsectorDiameter);
            List<WarpLane> warpLanes = WarpLaneBuilder.BuildWarpLanes(
                subsectors,
                profile.MaxSubsectorDiameter * 2.5);
            sector.InitializeWarpNetwork(subsectors, warpLanes);
            AssignGovernance(sector);
        }

        /// <summary>
        /// Recomputes the governance designation (Design/Reference/OpeningScenario.md). For each
        /// subsector, the highest-Importance Imperial-controlled world becomes the governance
        /// seat (tagged SubsectorCapital); the top seat sector-wide is promoted to SectorCapital.
        /// Like the warp network, this is derived from persisted planet data rather than stored,
        /// so it is rebuilt on both new-game and load and is idempotent if rerun.
        /// </summary>
        private static void AssignGovernance(Sector sector)
        {
            // Clear any stale designation so reruns (load, end-of-turn refresh) re-derive cleanly.
            foreach (Planet planet in sector.Planets.Values)
            {
                planet.GovernanceTier = GovernanceTier.Planetary;
            }

            Planet sectorSeat = null;
            foreach (Subsector subsector in sector.Subsectors)
            {
                Planet seat = subsector.Planets
                    .Where(p => p.GetControllingFaction()?.IsDefaultFaction == true)
                    .OrderByDescending(p => p.Importance)
                    .ThenByDescending(p => p.Population)
                    .ThenBy(p => p.Id)
                    .FirstOrDefault();
                subsector.SetGovernanceSeat(seat);
                if (seat == null) continue;

                seat.GovernanceTier = GovernanceTier.SubsectorCapital;
                if (sectorSeat == null || OutranksSeat(seat, sectorSeat))
                {
                    sectorSeat = seat;
                }
            }

            // Promote the strongest subsector seat to the single sector capital.
            if (sectorSeat != null)
            {
                sectorSeat.GovernanceTier = GovernanceTier.SectorCapital;
            }
        }

        // Ranks two candidate governance seats by the same order used to pick a subsector seat:
        // Importance, then Population, then Id (so selection is deterministic for a seed).
        private static bool OutranksSeat(Planet candidate, Planet incumbent)
        {
            if (candidate.Importance != incumbent.Importance)
                return candidate.Importance > incumbent.Importance;
            if (candidate.Population != incumbent.Population)
                return candidate.Population > incumbent.Population;
            return candidate.Id < incumbent.Id;
        }

        private static Planet GeneratePlanet(Coordinate position, GameRulesData data)
        {
            // Every generated world starts under the configured default-faction control unless a
            // data-authored public presence rule takes it over. Hidden and public faction starts are
            // applied by the same declarative policy surface, so adding a new faction does not
            // require another faction-specific branch here.
            Planet planet = PlanetBuilder.Instance.GenerateNewPlanet(
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

        // Reward path (Design/Reference/OpeningScenario.md): install the player as the planet-wide
        // controlling faction, inheriting the displaced Imperial population/garrison region by
        // region. Invoked by TurnController when the opening scenario is won.
        //
        // The Imperial (default) faction is resolved from the planet's faction map rather than via
        // GetControllingFaction: on a freshly-liberated world a cleared former-Tyranid region can
        // momentarily have no public faction (the displaced civilian remnant is non-public), which
        // would make GetControllingFaction's per-region resolution throw. Each region inherits the
        // Imperial garrison/population if that faction is present there, otherwise it is granted to
        // the player at zero strength.
        internal static void ReplaceChapterPlanetFaction(Planet chapterPlanet, Faction playerFaction)
        {
            PlanetFaction existingPlanetFaction = chapterPlanet.PlanetFactionMap.Values
                .FirstOrDefault(pf => pf.Faction.IsDefaultFaction);
            int? existingFactionId = existingPlanetFaction?.Faction.Id;

            PlanetFaction homePlanetFaction = new PlanetFaction(playerFaction)
            {
                IsPublic = true,
                Leader = null,
                PlayerReputation = 1
            };
            foreach (Region region in chapterPlanet.Regions)
            {
                RegionFaction homePlanetRegionFaction = new RegionFaction(homePlanetFaction, region)
                {
                    IsPublic = true
                };
                if (existingFactionId.HasValue
                    && region.RegionFactionMap.TryGetValue(existingFactionId.Value, out RegionFaction existingRegionFaction))
                {
                    homePlanetRegionFaction.Garrison = existingRegionFaction.Garrison;
                    homePlanetRegionFaction.Population = existingRegionFaction.Population;
                    region.RegionFactionMap.Remove(existingFactionId.Value);
                }
                region.RegionFactionMap[playerFaction.Id] = homePlanetRegionFaction;
            }
            if (existingFactionId.HasValue)
            {
                chapterPlanet.PlanetFactionMap.Remove(existingFactionId.Value);
            }
            chapterPlanet.PlanetFactionMap[homePlanetFaction.Faction.Id] = homePlanetFaction;
        }
    }
}
