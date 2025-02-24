using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    public class PlanetDataAccess
    {
        public List<Planet> GetPlanets(IDbConnection connection,
                                       IReadOnlyDictionary<int, Faction> factionMap,
                                       IReadOnlyDictionary<int, Character> characterMap,
                                       IReadOnlyDictionary<int, PlanetTemplate> planetTemplateMap)
        {
            Dictionary<int, List<PlanetFaction>> planetFactions =
                GetPlanetFactions(connection, factionMap, characterMap);
            
            List<Planet> planetList = [];
            Dictionary<int, Region> regionMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Planet";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int planetTemplateId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    int x = reader.GetInt32(3);
                    int y = reader.GetInt32(4);
                    int importance = reader.GetInt32(6);
                    int taxLevel = reader.GetInt32(7);
                    bool isUnderAssault = reader.GetBoolean(8);
                    var template = planetTemplateMap[planetTemplateId];
                    Faction controllingFaction;
                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        controllingFaction = factionMap[reader.GetInt32(5)];
                    }
                    else
                    {
                        controllingFaction = null;
                    }
                    // for now, we're hard coding all planets to be size 16
                    Planet planet =
                        new Planet(id, name, new Tuple<ushort, ushort>((ushort)x, (ushort)y), 16, template, importance, taxLevel)
                        {
                            ControllingFaction = controllingFaction,
                            IsUnderAssault = isUnderAssault
                        };

                    // set up region adjacency
                    foreach (PlanetFaction planetFaction in planetFactions[id])
                    {
                        planet.PlanetFactionMap.Add(planetFaction.Faction.Id, planetFaction);
                    }
                    planetList.Add(planet);
                }
            }

            // Fetch data from the RegionFaction table
            PopulateRegionFactions(connection, factionMap, regionMap);

            return planetList;
        }

        public Dictionary<int, Region> GetRegions(IDbConnection connection,
                                                   IReadOnlyDictionary<int, Faction> factionMap,
                                                   IReadOnlyCollection<Planet> planets)
        {
            Dictionary<int, Region> regionMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Region";
                var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int planetId = reader.GetInt32(1);
                    int regionNumber = reader.GetInt32(2);
                    string regionName = reader[3].ToString();
                    int regionType = reader.GetInt32(4);
                    bool isUnderAssault = reader.GetBoolean(5);
                    float intelligenceLevel = reader.GetFloat(6);

                    Planet planet = planets.First(p => p.Id == planetId);
                    Region region = new Region(id, planet, regionType, regionName, RegionExtensions.GetCoordinatesFromRegionNumber(regionNumber), intelligenceLevel);
                    regionMap[id] = region;
                    planet.Regions[regionNumber] = region;
                }
            }
            return regionMap;
        }

        private Dictionary<int, List<PlanetFaction>> GetPlanetFactions(IDbConnection connection,
                                                                       IReadOnlyDictionary<int, Faction> factionMap,
                                                                       IReadOnlyDictionary<int, Character> characterMap)
        {
            Dictionary<int, List<PlanetFaction>> planetPlanetFactionMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM PlanetFaction";
                var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    int? leaderId = null;
                    int planetId = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    bool isPublic = reader.GetBoolean(2);
                    int planetaryControl = reader.GetInt32(3);
                    float playerReputation = (float)reader[4];
                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        leaderId = reader.GetInt32(5);
                    }
                    PlanetFaction planetFaction =
                        new PlanetFaction(factionMap[factionId])
                        {
                            IsPublic = isPublic,
                            PlanetaryControl = planetaryControl,
                            PlayerReputation = playerReputation,
                            Leader = leaderId == null ? null : characterMap[(int)leaderId]
                        };

                    if (!planetPlanetFactionMap.ContainsKey(planetId))
                    {
                        planetPlanetFactionMap[planetId] = [];
                    }
                    planetPlanetFactionMap[planetId].Add(planetFaction);
                }
            }
            return planetPlanetFactionMap;
        }

        private static void PopulateRegionFactions(IDbConnection connection,
                                                                       IReadOnlyDictionary<int, Faction> factionMap,
                                                                       IReadOnlyDictionary<int, Region> regionMap)
        {
            Dictionary<int, List<RegionFaction>> regionRegionFactionMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM RegionFaction";
                var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    int regionId = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    bool isPublic = reader.GetBoolean(2);
                    int population = reader.GetInt32(3);
                    int pdfMembers = reader.GetInt32(4);

                    Region region = regionMap[regionId];
                    PlanetFaction planetFaction = region.Planet.PlanetFactionMap[factionId];
                    RegionFaction regionFaction =
                        new(planetFaction, region)
                        {
                            IsPublic = isPublic,
                            Population = population,
                            PDFMembers = pdfMembers
                        };
                    region.RegionFactionMap[regionFaction.PlanetFaction.Faction.Id] = regionFaction;
                }
            }
        }

        public Dictionary<int, Character> GetCharacterMap(IDbConnection connection, 
                                                           IReadOnlyDictionary<int, Faction> factionMap)
        {
            Dictionary<int, Character> characterMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Character";
                var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    float investigation = (float)reader[1];
                    float paranoia = (float)reader[2];
                    float neediness = (float)reader[3];
                    float patience = (float)reader[4];
                    float appreciation = (float)reader[5];
                    float influence = (float)reader[6];
                    int factionId = reader.GetInt32(7);
                    float opinionOfPlayer = (float)reader[8];

                    Character character = new Character()
                    {
                        Id = id,
                        Appreciation = appreciation,
                        Influence = influence,
                        Investigation = investigation,
                        Loyalty = factionMap[factionId],
                        Neediness = neediness,
                        OpinionOfPlayerForce = opinionOfPlayer,
                        Paranoia = paranoia,
                        Patience = patience
                    };

                    characterMap[id] = character;
                }
            }
            return characterMap;
        }

        public void SavePlanet(IDbTransaction transaction, Planet planet)
        {
            string controllingFactionId = planet.ControllingFaction == null ?
                "null" : planet.ControllingFaction.Id.ToString();

            string insert = $@"INSERT INTO Planet 
                (Id, PlanetTemplateId, Name, x, y, FactionId, 
                Importance, TaxLevel, IsUnderAssault) VALUES 
                ({planet.Id}, {planet.Template.Id}, '{planet.Name.Replace("\'", "\'\'")}', 
                {planet.Position.Item1}, {planet.Position.Item2}, {controllingFactionId},
                {planet.Importance}, {planet.TaxLevel}, {planet.IsUnderAssault});";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }
            SavePlanetFactions(transaction, planet.Id, planet.PlanetFactionMap);
            SavePlanetRegions(transaction, planet.Id, planet.Regions);
            SaveRegionFactions(transaction, planet.Regions);
        }

        public void SaveCharacter(IDbTransaction transaction, Character character)
        {
            string insert = $@"INSERT INTO Character 
                (Id, Investigation, Paranoia, Neediness, Patience, Appreciation, 
                Influence, LoyalFactionId, OpinionOfPlayer) VALUES 
                ({character.Id}, {character.Investigation}, '{character.Paranoia}', 
                {character.Neediness}, {character.Patience}, {character.Appreciation},
                {character.Influence}, {character.Loyalty.Id}, {character.OpinionOfPlayerForce});";
            using (var command = transaction.Connection.CreateCommand())
            {
                command.CommandText = insert;
                command.ExecuteNonQuery();
            }
        }

        private static void SavePlanetFactions(IDbTransaction transaction, int planetId, Dictionary<int, PlanetFaction> planetFactions)
        {
            foreach(KeyValuePair<int, PlanetFaction> planetFaction in planetFactions)
            {
                object leaderId = planetFaction.Value.Leader != null ?
                    (object)planetFaction.Value.Leader.Id : 
                    "null";
                string insert = $@"INSERT INTO PlanetFaction 
                    (PlanetId, FactionId, IsPublic, Population, 
                    PDFMembers, PlanetaryControl, PlayerReputation, LeaderId) VALUES 
                    ({planetId}, {planetFaction.Key}, {planetFaction.Value.IsPublic}, 
                    {planetFaction.Value.Population}, {planetFaction.Value.PDFMembers}, 
                    {planetFaction.Value.PlanetaryControl}, 
                    {planetFaction.Value.PlayerReputation}, {leaderId});";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void SavePlanetRegions(IDbTransaction transaction, int planetId, Region[] regions)
        {
            for(int i = 0; i < regions.Length; i++)
            {
                string insert = $@"INSERT INTO Region 
                    (Id, PlanetId, RegionNumber, RegionName, RegionType, IsUnderAssault, IntelligenceLevel) VALUES 
                    ({regions[i].Id}, {planetId}, {i}, {regions[i].Name}, 0, 0, {regions[i].IntelligenceLevel});";
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandText = insert;
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void SaveRegionFactions(IDbTransaction transaction, Region[] regions)
        {
            foreach (var region in regions)
            {
                foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                {
                    string insert = $@"INSERT INTO RegionFaction
                    (RegionId, FactionId, IsPublic, Population, PDFMembers) VALUES 
                    ({region.Id}, {regionFaction.PlanetFaction.Faction.Id}, {regionFaction.IsPublic}, 
                     {regionFaction.Population}, {regionFaction.PDFMembers});";
                    using (var command = transaction.Connection.CreateCommand())
                    {
                        command.CommandText = insert;
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
