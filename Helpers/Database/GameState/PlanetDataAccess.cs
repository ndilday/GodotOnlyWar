using OnlyWar.Builders;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
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
                    int importance = reader.GetInt32(5);
                    int taxLevel = reader.GetInt32(6);
                    var template = planetTemplateMap[planetTemplateId];
                    // for now, we're hard coding all planets to be size 16
                    Planet planet = new Planet(id, name, new Coordinate((ushort)x, (ushort)y), 16, template, importance, taxLevel);

                    // set up region adjacency
                    foreach (PlanetFaction planetFaction in planetFactions[id])
                    {
                        planet.PlanetFactionMap.Add(planetFaction.Faction.Id, planetFaction);
                    }
                    planetList.Add(planet);
                }
            }

            // Region factions and missions are populated separately, after GetRegions
            // has loaded the Region rows (see GetData). They cannot be populated here
            // because the regions do not exist yet at this point.
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
                    long carryingCapacity = reader.GetInt64(7);
                    // MaximumCarryingCapacity was appended after CarryingCapacity; legacy rows that
                    // predate it default to the current capacity (an undegraded region). See PRD §4.24.
                    long maximumCarryingCapacity = reader.FieldCount > 8 ? reader.GetInt64(8) : carryingCapacity;

                    Planet planet = planets.First(p => p.Id == planetId);
                    Region region = new Region(id, planet, regionType, regionName, RegionExtensions.GetCoordinatesFromRegionNumber(regionNumber), intelligenceLevel, carryingCapacity, maximumCarryingCapacity);
                    regionMap[id] = region;
                    planet.Regions[regionNumber] = region;
                }
            }
            return regionMap;
        }

        // Loads every persisted mission and returns them keyed by id. Region "special"
        // missions (intelligence-discovered opportunities) are added back to their region's
        // SpecialMissions list; order-attached missions (IsRegionMission = 0) are not, but are
        // still returned in the map so order loading can resolve them by id.
        public Dictionary<int, Mission> PopulateRegionMissions(IDbConnection connection,
                                           IReadOnlyDictionary<int, Region> regionMap)
        {
            Dictionary<int, Mission> missionMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Mission";
                var reader = command.ExecuteReader();
                int maxId = 0;

                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    MissionType missionType = (MissionType)reader.GetInt32(1);
                    int regionId = reader.GetInt32(2);
                    int factionId = reader.GetInt32(3);
                    int missionSize = reader.GetInt32(4);
                    DefenseType? defenseType = null;
                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        defenseType = (DefenseType)reader.GetInt32(5);
                    }
                    bool isRegionMission = reader.GetBoolean(6);

                    Region region = regionMap[regionId];
                    RegionFaction regionFaction = region.RegionFactionMap[factionId];
                    Mission mission;
                    // Both SabotageMission and ConstructionMission carry a DefenseType; distinguish
                    // them by MissionType so a saved construction mission does not load back as a
                    // sabotage mission.
                    if (defenseType == null)
                    {
                        mission = new Mission(id, missionType, regionFaction, missionSize);
                    }
                    else if (missionType == MissionType.Construction)
                    {
                        mission = new ConstructionMission(id, (DefenseType)defenseType, missionSize, regionFaction);
                    }
                    else
                    {
                        mission = new SabotageMission(id, (DefenseType)defenseType, missionSize, regionFaction);
                    }
                    missionMap[id] = mission;
                    if (isRegionMission)
                    {
                        region.SpecialMissions.Add(mission);
                    }
                    if(id > maxId)
                    {
                        maxId = id;
                    }
                }
                IdGenerator.SetNextMissionId(maxId + 1);
            }
            return missionMap;
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
                    float playerReputation = Convert.ToSingle(reader[4]);
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

        public static void PopulateRegionFactions(IDbConnection connection,
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
                    long population = reader.GetInt64(3);
                    long garrison = reader.GetInt64(4);
                    int organization = Math.Max(1, reader.GetInt32(5));
                    int entrenchment = reader.GetInt32(6);
                    int detection = reader.GetInt32(7);
                    int antiAir = reader.GetInt32(8);
                    // GrowthMultiplier was appended after AntiAir; legacy rows that predate it
                    // default to 1.0 (no throttle). See Design/OpeningScenario.md §2.2 / §7.
                    float growthMultiplier = reader.FieldCount > 9 ? (float)reader.GetDouble(9) : 1.0f;

                    Region region = regionMap[regionId];
                    if (!region.Planet.PlanetFactionMap.TryGetValue(factionId, out PlanetFaction planetFaction))
                    {
                        // Defensive: a saved RegionFaction referenced a faction that has no
                        // PlanetFaction on this planet. Sector generation can currently leave
                        // such an orphan region faction (a known data inconsistency), and the
                        // RegionFaction model requires a backing PlanetFaction. Reconstruct a
                        // minimal one rather than failing the entire load.
                        planetFaction = new PlanetFaction(factionMap[factionId]) { IsPublic = isPublic };
                        region.Planet.PlanetFactionMap[factionId] = planetFaction;
                    }
                    RegionFaction regionFaction =
                        new(planetFaction, region)
                        {
                            IsPublic = isPublic,
                            Population = population,
                            Garrison = garrison,
                            Organization = organization,
                            Entrenchment = entrenchment,
                            Detection = detection,
                            AntiAir = antiAir,
                            GrowthMultiplier = growthMultiplier
                        };
                    region.RegionFactionMap[regionFaction.PlanetFaction.Faction.Id] = regionFaction;
                }
            }

            PopulateObserverIntel(connection, regionMap);
        }

        // Loads per-observer intelligence beliefs onto the already-built region factions. Tolerant
        // of the table being absent (saves that predate the recon-intel feature) — those simply
        // load with no prior intel, and the AI re-recons.
        private static void PopulateObserverIntel(IDbConnection connection,
                                                  IReadOnlyDictionary<int, Region> regionMap)
        {
            using (var tableCheck = connection.CreateCommand())
            {
                tableCheck.CommandText =
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='RegionFactionObserverIntel'";
                if (tableCheck.ExecuteScalar() == null)
                {
                    return;
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT RegionId, FactionId, ObserverFactionId, IntelLevel FROM RegionFactionObserverIntel";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int regionId = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    int observerFactionId = reader.GetInt32(2);
                    float intelLevel = (float)reader.GetDouble(3);

                    if (regionMap.TryGetValue(regionId, out Region region)
                        && region.RegionFactionMap.TryGetValue(factionId, out RegionFaction regionFaction))
                    {
                        regionFaction.ObserverIntel[observerFactionId] = intelLevel;
                    }
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
                    string name = reader.GetString(1);
                    int age = reader.GetInt32(2);
                    float investigation = Convert.ToSingle(reader[3]);
                    float paranoia = Convert.ToSingle(reader[4]);
                    float neediness = Convert.ToSingle(reader[5]);
                    float patience = Convert.ToSingle(reader[6]);
                    float appreciation = Convert.ToSingle(reader[7]);
                    float influence = Convert.ToSingle(reader[8]);
                    int factionId = reader.GetInt32(9);
                    float opinionOfPlayer = Convert.ToSingle(reader[10]);

                    Character character = new Character()
                    {
                        Id = id,
                        Name = name,
                        Age = age,
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

            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Planet
                    (Id, PlanetTemplateId, Name, x, y, Importance, TaxLevel) VALUES
                    (@id, @templateId, @name, @x, @y, @importance, @taxLevel);";
                command.AddParam("@id", planet.Id);
                command.AddParam("@templateId", planet.Template.Id);
                command.AddParam("@name", planet.Name);
                command.AddParam("@x", planet.Position.X);
                command.AddParam("@y", planet.Position.Y);
                command.AddParam("@importance", planet.Importance);
                command.AddParam("@taxLevel", planet.TaxLevel);
                command.ExecuteNonQuery();
            }
            SavePlanetFactions(transaction, planet.Id, planet.PlanetFactionMap);
            SavePlanetRegions(transaction, planet.Id, planet.Regions);
            SaveRegionFactions(transaction, planet.Regions);
            SaveMissions(transaction, planet.Regions);
        }

        public void SaveCharacter(IDbTransaction transaction, Character character)
        {
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Character
                    (Id, Name, Age, Investigation, Paranoia, Neediness, Patience, Appreciation,
                     Influence, LoyalFactionId, OpinionOfPlayer) VALUES
                    (@id, @name, @age, @investigation, @paranoia, @neediness, @patience,
                     @appreciation, @influence, @loyalFactionId, @opinionOfPlayer);";
                command.AddParam("@id", character.Id);
                command.AddParam("@name", character.Name);
                command.AddParam("@age", character.Age);
                command.AddParam("@investigation", character.Investigation);
                command.AddParam("@paranoia", character.Paranoia);
                command.AddParam("@neediness", character.Neediness);
                command.AddParam("@patience", character.Patience);
                command.AddParam("@appreciation", character.Appreciation);
                command.AddParam("@influence", character.Influence);
                command.AddParam("@loyalFactionId", character.Loyalty.Id);
                command.AddParam("@opinionOfPlayer", character.OpinionOfPlayerForce);
                command.ExecuteNonQuery();
            }
        }

        private static void SavePlanetFactions(IDbTransaction transaction, int planetId, Dictionary<int, PlanetFaction> planetFactions)
        {
            foreach(KeyValuePair<int, PlanetFaction> planetFaction in planetFactions)
            {
                object leaderId = planetFaction.Value.Leader != null ?
                    (object)planetFaction.Value.Leader.Id :
                    null;
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO PlanetFaction
                        (PlanetId, FactionId, IsPublic, PlanetaryControl, PlayerReputation, LeaderId) VALUES
                        (@planetId, @factionId, @isPublic, @planetaryControl, @playerReputation, @leaderId);";
                    command.AddParam("@planetId", planetId);
                    command.AddParam("@factionId", planetFaction.Key);
                    command.AddParam("@isPublic", planetFaction.Value.IsPublic ? 1 : 0);
                    command.AddParam("@planetaryControl", planetFaction.Value.PlanetaryControl);
                    command.AddParam("@playerReputation", planetFaction.Value.PlayerReputation);
                    command.AddParam("@leaderId", leaderId);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void SavePlanetRegions(IDbTransaction transaction, int planetId, Region[] regions)
        {
            for(int i = 0; i < regions.Length; i++)
            {
                using (var command = transaction.Connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO Region
                        (Id, PlanetId, RegionNumber, RegionName, RegionType, IsUnderAssault, IntelligenceLevel, CarryingCapacity, MaximumCarryingCapacity) VALUES
                        (@id, @planetId, @regionNumber, @regionName, 0, 0, @intelligenceLevel, @carryingCapacity, @maximumCarryingCapacity);";
                    command.AddParam("@id", regions[i].Id);
                    command.AddParam("@planetId", planetId);
                    command.AddParam("@regionNumber", i);
                    command.AddParam("@regionName", regions[i].Name);
                    command.AddParam("@intelligenceLevel", regions[i].IntelligenceLevel);
                    command.AddParam("@carryingCapacity", regions[i].CarryingCapacity);
                    command.AddParam("@maximumCarryingCapacity", regions[i].MaximumCarryingCapacity);
                    command.ExecuteNonQuery();
                }
                // Special missions are persisted by SaveMissions, called separately
                // from SavePlanet. Do not insert them here as well (see TDD 8.1).
            }
        }

        private static void SaveRegionFactions(IDbTransaction transaction, Region[] regions)
        {
            foreach (var region in regions)
            {
                foreach (RegionFaction regionFaction in region.RegionFactionMap.Values)
                {
                    using (var command = transaction.Connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"INSERT INTO RegionFaction
                            (RegionId, FactionId, IsPublic, Population, Garrison, Organization, Entrenchment, Detection, AntiAir, GrowthMultiplier) VALUES
                            (@regionId, @factionId, @isPublic, @population, @garrison, @organization, @entrenchment, @detection, @antiAir, @growthMultiplier);";
                        command.AddParam("@regionId", region.Id);
                        command.AddParam("@factionId", regionFaction.PlanetFaction.Faction.Id);
                        command.AddParam("@isPublic", regionFaction.IsPublic ? 1 : 0);
                        command.AddParam("@population", regionFaction.Population);
                        command.AddParam("@garrison", regionFaction.Garrison);
                        command.AddParam("@organization", regionFaction.Organization);
                        command.AddParam("@entrenchment", regionFaction.Entrenchment);
                        command.AddParam("@detection", regionFaction.Detection);
                        command.AddParam("@antiAir", regionFaction.AntiAir);
                        command.AddParam("@growthMultiplier", regionFaction.GrowthMultiplier);
                        command.ExecuteNonQuery();
                    }
                    foreach (KeyValuePair<int, float> intel in regionFaction.ObserverIntel)
                    {
                        using var intelCommand = transaction.Connection.CreateCommand();
                        intelCommand.Transaction = transaction;
                        intelCommand.CommandText = @"INSERT INTO RegionFactionObserverIntel
                            (RegionId, FactionId, ObserverFactionId, IntelLevel) VALUES
                            (@regionId, @factionId, @observerFactionId, @intelLevel);";
                        intelCommand.AddParam("@regionId", region.Id);
                        intelCommand.AddParam("@factionId", regionFaction.PlanetFaction.Faction.Id);
                        intelCommand.AddParam("@observerFactionId", intel.Key);
                        intelCommand.AddParam("@intelLevel", intel.Value);
                        intelCommand.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void SaveMissions(IDbTransaction transaction, Region[] regions)
        {
            foreach (var region in regions)
            {
                foreach (Mission mission in region.SpecialMissions)
                {
                    SaveMission(transaction, mission, isRegionMission: true);
                }
            }
        }

        // Persists a single mission row. Region special missions pass isRegionMission: true;
        // missions that exist only because an order references them pass false (see
        // GameStateDataAccess.SaveData), so the loader can keep them out of the region's
        // SpecialMissions list while still resolving them for orders.
        public static void SaveMission(IDbTransaction transaction, Mission mission, bool isRegionMission)
        {
            object defenseType = mission switch
            {
                SabotageMission sabotage => (int)sabotage.DefenseType,
                ConstructionMission construction => (int)construction.ConstructionType,
                _ => null
            };
            using (var command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Mission
                    (Id, MissionType, RegionId, FactionId, MissionSize, DefenseTypeId, IsRegionMission) VALUES
                    (@id, @missionType, @regionId, @factionId, @missionSize, @defenseType, @isRegionMission);";
                command.AddParam("@id", mission.Id);
                command.AddParam("@missionType", (int)mission.MissionType);
                command.AddParam("@regionId", mission.RegionFaction.Region.Id);
                command.AddParam("@factionId", mission.RegionFaction.PlanetFaction.Faction.Id);
                command.AddParam("@missionSize", mission.MissionSize);
                command.AddParam("@defenseType", defenseType);
                command.AddParam("@isRegionMission", isRegionMission ? 1 : 0);
                command.ExecuteNonQuery();
            }
        }
    }
}
