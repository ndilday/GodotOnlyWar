using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
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

                        if (planet.PlanetFactionMap[planet.ControllingFaction.Id].Leader != null)
                        {
                            Character leader =
                                planet.PlanetFactionMap[planet.ControllingFaction.Id].Leader;
                            characterList.Add(leader);
                        }
                    }
                }
            }

            PlayerForce playerForce = CreateChapter(data, currentDate, planetList);
            PlaceStartingForces(planetList, playerForce, forceList);

            return new Sector(playerForce, characterList, planetList, forceList);
        }

        private static Planet GeneratePlanet(Tuple<ushort, ushort> position, GameRulesData data)
        {
            // TODO: There should be game start config settings for planet ownership by specific factions
            // TODO: Once genericized, move into planet factory
            double random = RNG.GetLinearDouble();
            Faction controllingFaction, infiltratingFaction;
            if (random <= 0.05)
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

        private static PlayerForce CreateChapter(GameRulesData data, Date currentDate, List<Planet> planetList)
        {
            Date basicTrainingEndDate = new Date(currentDate.Millenium, currentDate.Year - 3, 52);
            Date trainingStartDate = new Date(currentDate.Millenium, currentDate.Year - 4, 1);
            var soldierTemplate = data.PlayerFaction.SoldierTemplates[0];
            var soldiers =
                SoldierFactory.Instance.GenerateNewSoldiers(1000, soldierTemplate.Species, data.SkillTemplateList)
                .Select(s => new PlayerSoldier(s, $"{TempNameGenerator.GetName()} {TempNameGenerator.GetName()}"))
                .ToList();

            string foo = "";
            SoldierTrainingCalculator trainingHelper =
                new SoldierTrainingCalculator(data.BaseSkillMap.Values);
            foreach (PlayerSoldier soldier in soldiers)
            {
                soldier.AddEntryToHistory(trainingStartDate + ": accepted into training");
                if (soldier.PsychicPower > 0)
                {
                    soldier.AddEntryToHistory(trainingStartDate + ": psychic ability detected, acolyte training initiated");
                    // add psychic specific training here
                }
                trainingHelper.EvaluateSoldier(soldier, basicTrainingEndDate);
                soldier.ProgenoidImplantDate = new Date(currentDate.Millenium, currentDate.Year - 2, RNG.GetIntBelowMax(1, 53));

                foo += $"{(int)soldier.MeleeRating}, {(int)soldier.RangedRating}, {(int)soldier.LeadershipRating}, {(int)soldier.AncientRating}, {(int)soldier.MedicalRating}, {(int)soldier.TechRating}, {(int)soldier.PietyRating}\n";
            }


            //System.IO.File.WriteAllText($"{Application.streamingAssetsPath}/ratings.csv", foo);

            List<string> foundingHistoryEntries = new List<string>
            {
                $"{data.PlayerFaction.UnitTemplates.First().Value.Name} officially forms with its first 1,000 battle brothers."
            };
            // post-MOS evaluations
            foreach (PlayerSoldier soldier in soldiers)
            {
                trainingHelper.EvaluateSoldier(soldier, currentDate);
            }
            //GameDataSingleton.Instance.Sector.PlayerForce.Faction.Units.Add(GameDataSingleton.Instance.Sector.PlayerForce.OrderOfBattle);
            FoundChapterPlanet(planetList, data.PlayerFaction);

            var chapter = NewChapterBuilder.CreateChapter(data.PlayerFaction, trainingStartDate, data, currentDate);
            chapter.AddToBattleHistory(currentDate, "Chapter Founding",foundingHistoryEntries);
            return chapter;
        }

        private static void FoundChapterPlanet(List<Planet> planetList, Faction playerFaction)
        {
            // TODO: replace this with a random assignment of starting planet
            // and then have the galaxy map screen default to zooming in
            // on the Marine starting planet
            var emptyPlanets = planetList.Where(p => p.ControllingFaction.IsDefaultFaction);
            int max = emptyPlanets.Count();
            int chapterPlanetIndex = RNG.GetIntBelowMax(0, max);
            Planet chapterPlanet = emptyPlanets.ElementAt(chapterPlanetIndex);
            ReplaceChapterPlanetFaction(chapterPlanet, playerFaction);
        }

        private static void ReplaceChapterPlanetFaction(Planet chapterPlanet, Faction playerFaction)
        {
            Faction defaultFaction = chapterPlanet.ControllingFaction;
            chapterPlanet.ControllingFaction = playerFaction;
            
            PlanetFaction existingPlanetFaction = chapterPlanet.PlanetFactionMap[defaultFaction.Id];
            PlanetFaction homePlanetFaction = new PlanetFaction(playerFaction);
            homePlanetFaction.IsPublic = true;
            homePlanetFaction.Leader = null;
            foreach (Region region in chapterPlanet.Regions)
            {
                RegionFaction existingPlanetRegionFaction = region.RegionFactionMap[existingPlanetFaction.Faction.Id];
                RegionFaction homePlanetRegionFaction = new RegionFaction(homePlanetFaction, region);
                homePlanetRegionFaction.PDFMembers = existingPlanetRegionFaction.PDFMembers;
                homePlanetRegionFaction.Population = existingPlanetRegionFaction.Population;
                region.RegionFactionMap.Remove(existingPlanetFaction.Faction.Id);
                region.RegionFactionMap[playerFaction.Id] = homePlanetRegionFaction;
            }
            homePlanetFaction.PlayerReputation = 1;
            chapterPlanet.PlanetFactionMap.Remove(existingPlanetFaction.Faction.Id);
            chapterPlanet.PlanetFactionMap[homePlanetFaction.Faction.Id] = homePlanetFaction;
        }

        private static void PlaceStartingForces(IEnumerable<Planet> planets, PlayerForce playerForce, List<TaskForce> forceList)
        {
            foreach (Planet planet in planets)
            {
                // For now, put the chapter on their home planet
                if (planet.ControllingFaction == playerForce.Faction)
                {
                    planet.Regions[0].RegionFactionMap[planet.ControllingFaction.Id].LandedSquads.AddRange(
                            playerForce.Army.SquadMap.Values);
                    foreach (Squad squad in playerForce.Army.SquadMap.Values)
                    {
                        if (squad.Members.Count > 0)
                        {
                            squad.CurrentRegion = planet.Regions[0];
                        }
                    }
                    foreach (TaskForce taskForce in playerForce.Fleet.TaskForces)
                    {
                        taskForce.Planet = planet;
                        taskForce.Position = planet.Position;
                        forceList.Add(taskForce);
                    }
                }
                /*else if (planet.ControllingFaction.UnitTemplates != null)
                {
                    int potentialArmies = planet.ControllingFaction
                                                .UnitTemplates
                                                .Values
                                                .Where(ut => ut.IsTopLevelUnit)
                                                .Count();
                    // TODO: generalize this
                    Unit newArmy = TempArmyBuilder.GenerateArmy(
                        RNG.GetIntBelowMax(0, potentialArmies),
                        planet.ControllingFaction);
                    planet.ControllingFaction.Units.Add(newArmy);
                    planet.FactionSquadListMap[planet.ControllingFaction.Id] = newArmy.Squads.ToList();
                    foreach (Squad squad in newArmy.Squads)
                    {
                        squad.Location = planet;
                    }
                }*/
            }
        }
    }
}
