using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Helpers;

namespace OnlyWar.Builders
{
    internal static class SectorBuilder
    {
        public static Sector GenerateSector(int seed, GameRulesData data)
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

            return new Sector(characterList, planetList, forceList);
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
    }
}
