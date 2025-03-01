using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models.Squads;

namespace OnlyWar.Builders
{
    internal static class SectorBuilder
    {
        public static Sector GenerateSector(int seed, GameRulesData data, Date currentDate)
        {
            List<Planet> planetList = [];
            List<Character> characterList = [];
            List<TaskForce> forceList = [];

            RNG.Reset(seed);

            for (ushort j = 0; j < data.SectorSize.Item2; j++)
            {
                for (ushort i = 0; i < data.SectorSize.Item1; i++)
                {
                    double random = RNG.GetLinearDouble();
                    if (random <= data.PlanetChance)
                    {
                        Planet planet = GeneratePlanet(new Tuple<ushort, ushort>(i, j), data);
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
            ISoldierTrainingService trainingService = new SoldierTrainingCalculator(data.BaseSkillMap.Values);
            PlayerForce playerForce = NewChapterBuilder.CreateChapter(data, trainingService, trainingStartDate, currentDate);
            FoundTakebackPlanet(playerForce, planetList, forceList);
            //Planet chapterPlanet = FoundChapterPlanet(planetList, data.PlayerFaction);
            //PlaceStartingForces(chapterPlanet, playerForce, forceList);

            return new Sector(playerForce, characterList, planetList, forceList);
        }

        private static Planet GeneratePlanet(Tuple<ushort, ushort> position, GameRulesData data)
        {
            // TODO: There should be game start config settings for planet ownership by specific factions
            // TODO: Once genericized, move into planet factory
            double random = RNG.GetLinearDouble();
            Faction controllingFaction, infiltratingFaction;
            if (random <= 0.05f)
            {
                controllingFaction = data.Factions.First(f => f.Name == "Genestealer Cult");
                infiltratingFaction = null;
            }
            else if (random <= 0.25f)
            {
                controllingFaction = data.Factions.First(f => f.Name == "Tyranids");
                infiltratingFaction = null;
            }
            else
            {
                controllingFaction = data.DefaultFaction;
                random = RNG.GetLinearDouble();
                infiltratingFaction = random <= 0.1 ? data.Factions.First(f => f.Name == "Genestealer Cult") : null;
            }

            return PlanetBuilder.Instance.GenerateNewPlanet(data.PlanetTemplateMap, position, controllingFaction, infiltratingFaction);
        }

        private static Planet FoundTakebackPlanet(PlayerForce playerForce, List<Planet> planetList, List<TaskForce> forceList)
        {
            var enemyPlanets = planetList.Where(p => !p.GetControllingFaction().IsDefaultFaction && p.Population >= 16000).OrderBy(p => p.Population);
            Planet planetToInvade = enemyPlanets.First();
            // find the region with the lowest population, and set it to the player faction
            Region regionToInvade = planetToInvade.Regions.OrderBy(r => r.RegionFactionMap[planetToInvade.GetControllingFaction().Id].Population).First();
            regionToInvade.RegionFactionMap.Clear();
            RegionFaction playerRegionFaction = new RegionFaction(new PlanetFaction(playerForce.Faction), regionToInvade);
            regionToInvade.RegionFactionMap[playerForce.Faction.Id] = playerRegionFaction;

            playerRegionFaction.LandedSquads.AddRange(playerForce.Army.SquadMap.Values);
            foreach (Squad squad in playerForce.Army.SquadMap.Values)
            {
                if (squad.Members.Count > 0)
                {
                    squad.CurrentRegion = regionToInvade;
                }
            }
            foreach (TaskForce taskForce in playerForce.Fleet.TaskForces)
            {
                taskForce.Planet = planetToInvade;
                taskForce.Position = planetToInvade.Position;
                forceList.Add(taskForce);
            }

            return planetToInvade;
        }

        private static Planet FoundChapterPlanet(List<Planet> planetList, Faction playerFaction)
        {
            var emptyPlanets = planetList.Where(p => p.GetControllingFaction().IsDefaultFaction);
            int max = emptyPlanets.Count();
            int chapterPlanetIndex = RNG.GetIntBelowMax(0, max);
            Planet chapterPlanet = emptyPlanets.ElementAt(chapterPlanetIndex);
            ReplaceChapterPlanetFaction(chapterPlanet, playerFaction);
            return chapterPlanet;
        }

        private static void ReplaceChapterPlanetFaction(Planet chapterPlanet, Faction playerFaction)
        {
            Faction defaultFaction = chapterPlanet.GetControllingFaction();
            
            PlanetFaction existingPlanetFaction = chapterPlanet.PlanetFactionMap[defaultFaction.Id];
            PlanetFaction homePlanetFaction = new PlanetFaction(playerFaction);
            homePlanetFaction.IsPublic = true;
            homePlanetFaction.Leader = null;
            foreach (Region region in chapterPlanet.Regions)
            {
                RegionFaction existingPlanetRegionFaction = region.RegionFactionMap[existingPlanetFaction.Faction.Id];
                RegionFaction homePlanetRegionFaction = new RegionFaction(homePlanetFaction, region);
                homePlanetRegionFaction.Garrison = existingPlanetRegionFaction.Garrison;
                homePlanetRegionFaction.Population = existingPlanetRegionFaction.Population;
                region.RegionFactionMap.Remove(existingPlanetFaction.Faction.Id);
                region.RegionFactionMap[playerFaction.Id] = homePlanetRegionFaction;
            }
            homePlanetFaction.PlayerReputation = 1;
            chapterPlanet.PlanetFactionMap.Remove(existingPlanetFaction.Faction.Id);
            chapterPlanet.PlanetFactionMap[homePlanetFaction.Faction.Id] = homePlanetFaction;
        }

        private static void PlaceStartingForces(Planet startingPlanet, PlayerForce playerForce, List<TaskForce> forceList)
        {
            startingPlanet.Regions[0].RegionFactionMap[startingPlanet.GetControllingFaction().Id].LandedSquads.AddRange(
                    playerForce.Army.SquadMap.Values);
            foreach (Squad squad in playerForce.Army.SquadMap.Values)
            {
                if (squad.Members.Count > 0)
                {
                    squad.CurrentRegion = startingPlanet.Regions[0];
                }
            }
            foreach (TaskForce taskForce in playerForce.Fleet.TaskForces)
            {
                taskForce.Planet = startingPlanet;
                taskForce.Position = startingPlanet.Position;
                forceList.Add(taskForce);
            }
        }
    }
}
