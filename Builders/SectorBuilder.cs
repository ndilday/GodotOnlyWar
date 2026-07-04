using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;

namespace OnlyWar.Builders
{
    internal static class SectorBuilder
    {
        public static Sector GenerateSector(int seed, GameRulesData data, Date currentDate, string chapterName = null)
        {
            List<Planet> planetList = [];
            List<Character> characterList = [];
            List<TaskForce> forceList = [];

            RNG.Reset(seed);
            PlanetBuilder.Instance.Reset();

            for (ushort j = 0; j < data.SectorSize.Y; j++)
            {
                for (ushort i = 0; i < data.SectorSize.X; i++)
                {
                    double random = RNG.GetLinearDouble();
                    if (random <= data.PlanetChance)
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
                data.BaseSkillMap.Values, data.TrainingProfiles.Values, ratingCalculator);
            PlayerForce playerForce = NewChapterBuilder.CreateChapter(data, trainingService, trainingStartDate, currentDate, chapterName);

            // The scenario stamp resolves the sitting Sector Lord, so the sector and its derived
            // governance designation must exist first. The fleet starts empty here; the scenario
            // parks it in orbit via Sector.AddNewFleet (Design/OpeningScenario.md §1, §3).
            Sector sector = new Sector(playerForce, characterList, planetList, forceList);
            GenerateWarpNetwork(sector, data);
            sector.Scenario = ScenarioBuilder.StampPromisedWorld(
                sector, data, currentDate, playerForce, planetList, characterList);
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
            Godot.Vector2I gridDimensions = new(data.SectorSize.X, data.SectorSize.Y);
            List<Subsector> subsectors = SubsectorBuilder.BuildSubsectors(sector.Planets.Values, gridDimensions);
            List<WarpLane> warpLanes = WarpLaneBuilder.BuildWarpLanes(subsectors, data.MaxSubsectorCellDiameter * 2.5);
            sector.InitializeWarpNetwork(subsectors, warpLanes);
            AssignGovernance(sector);
        }

        /// <summary>
        /// Recomputes the governance designation (Design/OpeningScenario.md §2.3). For each
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
                    .Where(p => p.GetControllingFaction().IsDefaultFaction)
                    .OrderByDescending(p => p.Importance)
                    .ThenByDescending(p => p.Population)
                    .ThenBy(p => p.Id)
                    .FirstOrDefault();
                subsector.GovernanceSeat = seat;
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
            // Every generated world starts under Imperial (default-faction) control: no enemy
            // faction openly holds a planet at game start, so the newly founded Chapter isn't
            // dropped into a sector already speckled with Tyranid/cult holdings. The only overt
            // incursion is the Tyranid invasion the opening scenario stamps onto the promised
            // world (Design/OpeningScenario.md §3). Hidden Genestealer-cult infiltration is still
            // seeded on a minority of worlds — it's covert, not "in control," and gives the sector
            // latent threats to surface later.
            // TODO: reintroduce overt faction-owned worlds via game-start config + hot spots.
            double random = RNG.GetLinearDouble();
            Faction infiltratingFaction = random <= 0.1 ? data.SectorFactions.Infiltrator : null;
            return PlanetBuilder.Instance.GenerateNewPlanet(data.PlanetTemplateMap, position, data.DefaultFaction, infiltratingFaction);
        }

        // Reward path (Design/OpeningScenario.md §6.2): install the player as the planet-wide
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
