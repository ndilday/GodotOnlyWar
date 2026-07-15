using Microsoft.Data.Sqlite;
using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using OnlyWar.Models.Orders;
using OnlyWar.Helpers.Storage;
using OnlyWar.Models.Supply;

namespace OnlyWar.Helpers.Database.GameState
{
    public class GameStateDataBlob
    {
        public List<Character> Characters { get; set; }
        public List<Planet> Planets { get; set; }
        public List<IRequest> Requests { get; set; }
        public List<Pledge> Pledges { get; set; }
        public List<TaskForce> Fleets { get; set; }
        public List<Unit> Units { get; set; }
        public Date CurrentDate { get; set; }
        // The chapter's Requisition pool (PRD 4.23), restored onto the loaded Army.
        public int Requisition { get; set; }
        // The chapter's gene-seed stockpile count and aggregate purity (PRD 4.8), restored
        // onto the loaded PlayerForce.
        public int GeneseedStockpile { get; set; }
        public float GeneseedPurity { get; set; }
        // Medical procedures in progress (PRD 4.8 / 5.3), restored onto the loaded Army.
        public List<MedicalProcedure> MedicalProcedures { get; set; }
        public Dictionary<Date, List<EventHistory>> History { get; set; }
        // Squad-less fallen brothers, retained for their dossiers (PRD 4.12).
        public List<PlayerSoldier> FallenBrothers { get; set; }
        // The Opening Scenario state (Design/OpeningScenario.md §7), or null for legacy/sandbox
        // saves; reattached to Sector.Scenario by the load path.
        public CampaignScenario Scenario { get; set; }
    }

    public class GameStateDataAccess
    {
        private readonly PlanetDataAccess _planetDataAccess;
        private readonly RequestDataAccess _requestDataAccess;
        private readonly FleetDataAccess _fleetDataAccess;
        private readonly UnitDataAccess _unitDataAccess;
        private readonly SoldierDataAccess _soldierDataAccess;
        private readonly PlayerSoldierDataAccess _playerSoldierDataAccess;
        private readonly GlobalDataAccess _globalDataAccess;
        private readonly PlayerFactionEventDataAccess _playerFactionEventDataAccess;
        private readonly MedicalProcedureDataAccess _medicalProcedureDataAccess;
        private readonly PledgeDataAccess _pledgeDataAccess;
        private static GameStateDataAccess _instance;
        public static GameStateDataAccess Instance
        {
            get
            {
                if(_instance == null)
                {
                    _instance = new GameStateDataAccess();
                }
                return _instance;
            }
        }
        
        private GameStateDataAccess()
        {
            _planetDataAccess = new PlanetDataAccess();
            _requestDataAccess = new RequestDataAccess();
            _fleetDataAccess = new FleetDataAccess();
            _unitDataAccess = new UnitDataAccess();
            _soldierDataAccess = new SoldierDataAccess();
            _playerSoldierDataAccess = new PlayerSoldierDataAccess();
            _globalDataAccess = new GlobalDataAccess();
            _playerFactionEventDataAccess = new PlayerFactionEventDataAccess();
            _medicalProcedureDataAccess = new MedicalProcedureDataAccess();
            _pledgeDataAccess = new PledgeDataAccess();
        }

        public GameStateDataBlob GetData(string filePath,
                            Dictionary<int, Faction> factionMap,
                            IReadOnlyDictionary<int, PlanetTemplate> planetTemplateMap,
                            IReadOnlyDictionary<int, ShipTemplate> shipTemplateMap,
                            IReadOnlyDictionary<int, UnitTemplate> unitTemplateMap,
                            IReadOnlyDictionary<int, SquadTemplate> squadTemplates,
                            IReadOnlyDictionary<int, WeaponSet> weaponSets,
                            IReadOnlyDictionary<int, HitLocationTemplate> hitLocationTemplates,
                            IReadOnlyDictionary<int, BaseSkill> baseSkillMap, 
                            IReadOnlyDictionary<int, SoldierTemplate> soldierTemplateMap)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("The selected save file does not exist.", fullPath);
            }

            string connection = BuildConnectionString(fullPath, SqliteOpenMode.ReadOnly);
            using IDbConnection dbCon = new SqliteConnection(connection);
            dbCon.Open();
            _globalDataAccess.EnsureCompatibleSaveVersion(dbCon);
            var characterMap = _planetDataAccess.GetCharacterMap(dbCon, factionMap);
            //var regionData = _planetDataAccess.Get
            var planets = _planetDataAccess.GetPlanets(dbCon, factionMap, characterMap,
                                                       planetTemplateMap);
            var regions = _planetDataAccess.GetRegions(dbCon, factionMap, planets);
            PlanetDataAccess.PopulateRegionFactions(dbCon, factionMap, regions);
            var missionMap = _planetDataAccess.PopulateRegionMissions(dbCon, regions);
            var requests = _requestDataAccess.GetRequests(dbCon, characterMap, factionMap, planets);
            var pledges = _pledgeDataAccess.GetPledges(dbCon);
            var ships = _fleetDataAccess.GetShipsByFleetId(dbCon, shipTemplateMap);
            var shipMap = ships.Values.SelectMany(s => s).ToDictionary(ship => ship.Id);
            var fleets = _fleetDataAccess.GetFleetsByFactionId(dbCon, ships, factionMap, planets);
            var loadouts = _unitDataAccess.GetSquadWeaponSets(dbCon, weaponSets);
            var squads = _unitDataAccess.GetSquadsByUnitId(dbCon, squadTemplates, loadouts,
                                                           shipMap, regions, missionMap);
            var units = _unitDataAccess.GetUnits(dbCon, unitTemplateMap, squads);
            var squadMap = squads.Values.SelectMany(s => s).ToDictionary(s => s.Id);
            var soldiers = _soldierDataAccess.GetData(dbCon, hitLocationTemplates, baseSkillMap,
                                                      soldierTemplateMap, squadMap);
            SoldierFactory.Instance.SetCurrentHighestSoldierId(soldiers.Keys.Max());
            var playerSoldiers = _playerSoldierDataAccess.GetData(dbCon, soldiers);
            var global = _globalDataAccess.GetGlobalData(dbCon);
            var medicalProcedures = _medicalProcedureDataAccess.GetProcedures(dbCon);
            var history = _playerFactionEventDataAccess.GetHistory(dbCon);
            // Decorated soldiers with no squad are fallen brothers; the living are reached
            // through the loaded units, so only the fallen need to ride along in the blob.
            var fallenBrothers = playerSoldiers.Values
                .Where(s => s.AssignedSquad == null)
                .ToList();
            return new GameStateDataBlob
            {
                Characters = characterMap.Values.ToList(),
                Planets = planets,
                Requests = requests,
                Pledges = pledges,
                Fleets = fleets,
                Units = units,
                CurrentDate = global?.Date,
                Requisition = global?.Requisition ?? 0,
                GeneseedStockpile = global?.GeneseedStockpile ?? 0,
                GeneseedPurity = global?.GeneseedPurity ?? 1.0f,
                MedicalProcedures = medicalProcedures,
                History = history,
                FallenBrothers = fallenBrothers,
                Scenario = global?.Scenario
            };
        }

        public void SaveData(string filePath,
                             Date currentDate,
                             int requisition,
                             int geneseedStockpile,
                             float geneseedPurity,
                             CampaignScenario scenario,
                             IEnumerable<MedicalProcedure> medicalProcedures,
                             IEnumerable<Character> characters,
                             IEnumerable<IRequest> requests,
                             IEnumerable<Pledge> pledges,
                             IEnumerable<Planet> planets,
                             IEnumerable<TaskForce> fleets,
                             IEnumerable<Unit> units,
                             IEnumerable<PlayerSoldier> playerSoldiers,
                             IEnumerable<PlayerSoldier> fallenBrothers,
                             IReadOnlyDictionary<Date, List<EventHistory>> history,
                             string schemaFilePath = null)
        {

            // Write the whole save to a sibling temp file first and only swap it over the
            // real file once everything has committed. The previous save is left untouched
            // until the final move, so a failure anywhere below can never destroy it.
            string fullPath = Path.GetFullPath(filePath);
            string directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var squads = units.SelectMany(u => u.GetAllSquads());
            var ships = fleets.SelectMany(f => f.Ships);
            try
            {
                GenerateTables(tempPath, schemaFilePath ?? DefaultSchemaFilePath());
                WriteSaveData(tempPath, currentDate, requisition, geneseedStockpile,
                              geneseedPurity, scenario, medicalProcedures, characters, requests,
                              pledges, planets, fleets, playerSoldiers, fallenBrothers, history, squads,
                              ships, units);
                // Release the pooled SQLite handles so the temp file can be moved over the
                // target on Windows (an open handle would block the move).
                SqliteConnection.ClearAllPools();
                File.Move(tempPath, fullPath, overwrite: true);
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the temp file; ignore if still locked.
                }
                throw;
            }
        }

        private void WriteSaveData(string filePath,
                                   Date currentDate,
                                   int requisition,
                                   int geneseedStockpile,
                                   float geneseedPurity,
                                   CampaignScenario scenario,
                                   IEnumerable<MedicalProcedure> medicalProcedures,
                                   IEnumerable<Character> characters,
                                   IEnumerable<IRequest> requests,
                                   IEnumerable<Pledge> pledges,
                                   IEnumerable<Planet> planets,
                                   IEnumerable<TaskForce> fleets,
                                   IEnumerable<PlayerSoldier> playerSoldiers,
                                   IEnumerable<PlayerSoldier> fallenBrothers,
                                   IReadOnlyDictionary<Date, List<EventHistory>> history,
                                   IEnumerable<Squad> squads,
                                   IEnumerable<Ship> ships,
                                   IEnumerable<Unit> units)
        {
            string connection = BuildConnectionString(filePath, SqliteOpenMode.ReadWriteCreate);
            using IDbConnection dbCon = new SqliteConnection(connection);
            dbCon.Open();
            using (var transaction = dbCon.BeginTransaction())
            {
                try
                {
                    foreach(Character character in characters)
                    {
                        _planetDataAccess.SaveCharacter(transaction, character);
                    }
                    
                    foreach (Planet planet in planets)
                    {
                        _planetDataAccess.SavePlanet(transaction, planet);
                    }

                    foreach(IRequest request in requests)
                    {
                        _requestDataAccess.SaveRequest(transaction, request);
                    }

                    foreach (Pledge pledge in pledges ?? [])
                    {
                        _pledgeDataAccess.SavePledge(transaction, pledge);
                    }

                    foreach(TaskForce fleet in fleets)
                    {
                        _fleetDataAccess.SaveFleet(transaction, fleet);
                    }

                    foreach(Ship ship in ships)
                    {
                        _fleetDataAccess.SaveShip(transaction, ship);
                    }

                    foreach(Unit unit in units)
                    {
                        _unitDataAccess.SaveUnit(transaction, unit);
                        foreach(Unit childUnit in unit?.ChildUnits)
                        {
                            _unitDataAccess.SaveUnit(transaction, childUnit);
                        }
                    }

                    foreach(Squad squad in squads)
                    {
                        _unitDataAccess.SaveSquad(transaction, squad);
                        foreach (ISoldier soldier in squad.Members)
                        {
                            _soldierDataAccess.SaveSoldier(transaction, soldier);
                        }
                    }

                    // Fallen brothers belong to no squad, so they are not covered by the
                    // loop above; persist their base soldier rows (with a null SquadId) here.
                    List<PlayerSoldier> fallen = fallenBrothers?.ToList() ?? [];
                    foreach (PlayerSoldier fallenBrother in fallen)
                    {
                        _soldierDataAccess.SaveSoldier(transaction, fallenBrother);
                    }
                    // missions already written as region special missions, so order missions
                    // that reuse one are not inserted twice (primary-key conflict)
                    HashSet<int> savedMissionIds = planets
                        .SelectMany(p => p.Regions)
                        .SelectMany(r => r.SpecialMissions)
                        .Select(m => m.Id)
                        .ToHashSet();
                    var orders = squads.Select(s => s.CurrentOrders)
                                       .Where(o => o != null && o.Mission != null)
                                       .Distinct();
                    foreach(Order order in orders)
                    {
                        // an order's mission may not be a region special mission (e.g. a
                        // player Recon/Advance/Fortify order); persist it so the order can be
                        // restored on load
                        if (savedMissionIds.Add(order.Mission.Id))
                        {
                            PlanetDataAccess.SaveMission(transaction, order.Mission, isRegionMission: false);
                        }
                        _unitDataAccess.SaveOrder(transaction, order);
                    }

                    foreach(PlayerSoldier playerSoldier in playerSoldiers.Concat(fallen))
                    {
                        _playerSoldierDataAccess.SavePlayerSoldier(transaction, playerSoldier);
                    }
                    foreach (MedicalProcedure procedure in medicalProcedures ?? [])
                    {
                        _medicalProcedureDataAccess.SaveProcedure(transaction, procedure);
                    }
                    _globalDataAccess.SaveGlobalData(transaction, currentDate, requisition,
                                                     geneseedStockpile, geneseedPurity, scenario);
                    _playerFactionEventDataAccess.SaveData(transaction, history);
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
                transaction.Commit();
                dbCon.Close();
            }
        }

        private static string DefaultSchemaFilePath()
        {
            return GameStorage.SaveSchemaPath;
        }

        private static string BuildConnectionString(string filePath, SqliteOpenMode mode)
        {
            // Foreign key enforcement is enabled. The save schema is FK-valid (every
            // reference resolves to a table in the save file) and the save routines
            // insert parent rows before the rows that reference them.
            return new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = mode,
                ForeignKeys = true,
                Pooling = false
            }.ToString();
        }

        private void GenerateTables(string filePath, string schemaFilePath)
        {
            string cmdText = File.ReadAllText(schemaFilePath);
            string connection = BuildConnectionString(filePath, SqliteOpenMode.ReadWriteCreate);
            using IDbConnection dbCon = new SqliteConnection(connection);
            dbCon.Open();
            using (var command = dbCon.CreateCommand())
            {
                command.CommandText = cmdText;
                command.ExecuteNonQuery();
            }
        }
    }
}
