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
using OnlyWar.Models.Reports;
using OnlyWar.Models.Events;
using OnlyWar.Models.FactionBehaviors;
using OnlyWar.Models.Orks;

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
        // The first-class Chapter Home World and the recruitment aggregate's primitive
        // persistence representation. Domain reconstruction happens in SavedGameLoader.
        public int? HomeWorldPlanetId { get; set; }
        public RecruitmentSaveData Recruitment { get; set; }
        public List<Order> Orders { get; set; }
        // Medical procedures in progress (PRD 4.8 / 5.3), restored onto the loaded Army.
        public List<MedicalProcedure> MedicalProcedures { get; set; }
        public Dictionary<Date, List<EventHistory>> History { get; set; }
        // Squad-less fallen brothers, retained for their dossiers (PRD 4.12).
        public List<PlayerSoldier> FallenBrothers { get; set; }
        // The Opening Scenario state (Design/Reference/OpeningScenario.md), or null for sandbox
        // saves; reattached to Sector.Scenario by the load path.
        public CampaignScenario Scenario { get; set; }
        public LoadoutDoctrine ChapterLoadoutDoctrine { get; set; }
        public CharacterLoadoutDoctrine CharacterLoadoutDoctrine { get; set; }
        public EquipmentLoadoutDoctrine EquipmentLoadoutDoctrine { get; set; }
        public LastTurnReportSnapshot LastTurnReportSnapshot { get; set; }
        public CampaignEventLedger CampaignEventLedger { get; set; }
        public ChapterChronicleLedger ChapterChronicle { get; set; }
        public CampaignIdentity CampaignIdentity { get; set; }
        public FactionRelationshipLedger RelationshipLedger { get; set; }
        public IReadOnlyList<WorldControlEpisodeState> WorldControlEpisodes { get; set; }
        public List<GhostPopulationSource> GhostPopulationSources { get; set; }
        public List<StrategicInvasionForceSaveData> StrategicInvasionForces { get; set; }
        // Legacy projections are populated while old save consumers migrate. They intentionally
        // do not drive new production behavior.
        [Obsolete("Use GhostPopulationSources.")]
        public List<OrkGhostSource> OrkGhostSources { get; set; }
        [Obsolete("Use StrategicInvasionForces.")]
        public List<OrkWaaaghSaveData> OrkWaaaghs { get; set; }
        public bool UpgradePending { get; set; }
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
        private readonly MedicalProcedureDataAccess _medicalProcedureDataAccess;
        private readonly PledgeDataAccess _pledgeDataAccess;
        private readonly RecruitmentDataAccess _recruitmentDataAccess;
        private readonly LoadoutDoctrineDataAccess _loadoutDoctrineDataAccess;
        private readonly LastTurnReportDataAccess _lastTurnReportDataAccess;
        private readonly CampaignEventDataAccess _campaignEventDataAccess;
        private readonly ChapterChronicleDataAccess _chapterChronicleDataAccess;
        private readonly WorldControlEpisodeDataAccess _worldControlEpisodeDataAccess;
        private readonly IndividualPostingDataAccess _individualPostingDataAccess;
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
            _medicalProcedureDataAccess = new MedicalProcedureDataAccess();
            _pledgeDataAccess = new PledgeDataAccess();
            _recruitmentDataAccess = new RecruitmentDataAccess();
            _loadoutDoctrineDataAccess = new LoadoutDoctrineDataAccess();
            _lastTurnReportDataAccess = new LastTurnReportDataAccess();
            _campaignEventDataAccess = new CampaignEventDataAccess();
            _chapterChronicleDataAccess = new ChapterChronicleDataAccess();
            _worldControlEpisodeDataAccess = new WorldControlEpisodeDataAccess();
            _individualPostingDataAccess = new IndividualPostingDataAccess();
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
                            IReadOnlyDictionary<int, SoldierTemplate> soldierTemplateMap,
                            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates = null,
                            IReadOnlyDictionary<int, EquipmentKitTemplate> equipmentKits = null,
                            ScoutTrainingOptionCatalog scoutTrainingOptions = null)
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
            FactionRelationshipLedger relationshipLedger = LoadFactionRelationships(
                dbCon,
                factionMap);
            var characterMap = _planetDataAccess.GetCharacterMap(dbCon, factionMap);
            //var regionData = _planetDataAccess.Get
            var planets = _planetDataAccess.GetPlanets(dbCon, factionMap, characterMap,
                                                       planetTemplateMap);
            var planetMap = planets.ToDictionary(planet => planet.Id);
            _loadoutDoctrineDataAccess.PopulatePlanetDoctrines(dbCon, planetMap, weaponSets);
            var chapterLoadoutDoctrine = _loadoutDoctrineDataAccess.GetChapterDoctrine(dbCon, weaponSets);
            var characterLoadoutDoctrine = _loadoutDoctrineDataAccess.GetCharacterDoctrine(dbCon, weaponSets);
            var equipmentLoadoutDoctrine = equipmentTemplates != null && equipmentKits != null
                ? _loadoutDoctrineDataAccess.GetEquipmentDoctrine(dbCon, equipmentTemplates, equipmentKits)
                : new EquipmentLoadoutDoctrine();
            var regions = _planetDataAccess.GetRegions(dbCon, factionMap, planets);
            PlanetDataAccess.PopulateRegionFactions(dbCon, factionMap, regions);
            List<GhostPopulationSource> ghostPopulationSources = LoadGhostPopulationSources(
                dbCon, factionMap, planetTemplateMap);
            var missionMap = _planetDataAccess.PopulateRegionMissions(dbCon, regions, factionMap);
            var requests = _requestDataAccess.GetRequests(dbCon, characterMap, factionMap, planets);
            var pledges = _pledgeDataAccess.GetPledges(dbCon);
            var ships = _fleetDataAccess.GetShipsByFleetId(dbCon, shipTemplateMap);
            var shipMap = ships.Values.SelectMany(s => s).ToDictionary(ship => ship.Id);
            var fleets = _fleetDataAccess.GetFleetsByFactionId(dbCon, ships, factionMap, planets);
            FlagshipService flagships = new();
            List<Ship> playerShips = fleets
                .Where(fleet => fleet.Faction == factionMap.Values.FirstOrDefault(faction => faction.IsPlayerFaction))
                .SelectMany(fleet => fleet.Ships)
                .ToList();
            Faction playerFaction = factionMap.Values.FirstOrDefault(
                faction => faction.IsPlayerFaction);
            flagships.ValidateSinglePlayerFlagship(playerFaction, playerShips);
            var loadouts = _unitDataAccess.GetSquadWeaponSets(dbCon, weaponSets);
            var squads = _unitDataAccess.GetSquadsByUnitId(dbCon, squadTemplates, loadouts,
                                                           shipMap, regions, missionMap, factionMap,
                                                           scoutTrainingOptions);
            List<StrategicInvasionForceSaveData> strategicInvasionForces = LoadStrategicInvasionForces(dbCon);
            var units = _unitDataAccess.GetUnits(dbCon, unitTemplateMap, squads);
            var squadMap = squads.Values.SelectMany(s => s).ToDictionary(s => s.Id);
            var soldiers = _soldierDataAccess.GetData(dbCon, hitLocationTemplates, baseSkillMap,
                                                      soldierTemplateMap, squadMap);
            var recruitment = _recruitmentDataAccess.GetData(dbCon);
            int highestIdentity = soldiers.Keys
                .Concat(recruitment.Aspirants.Select(aspirant => aspirant.Id))
                .DefaultIfEmpty(0)
                .Max();
            SoldierFactory.Instance.SetCurrentHighestSoldierId(highestIdentity);
            var playerSoldiers = _playerSoldierDataAccess.GetData(dbCon, soldiers);
            _unitDataAccess.PopulateOrderCharacters(dbCon, playerSoldiers);
            // Postings hydrate only after soldiers, squads, ships, regions, and orders exist.
            // The service rebuilds both order and individual-ship projections from these rows.
            _individualPostingDataAccess.Populate(
                dbCon, squadMap, playerSoldiers, shipMap, regions);
            var global = _globalDataAccess.GetGlobalData(dbCon);
            var medicalProcedures = _medicalProcedureDataAccess.GetProcedures(dbCon);
            var lastTurnReportSnapshot = _lastTurnReportDataAccess.GetSnapshot(dbCon);
            // Decorated soldiers with no squad are fallen brothers; the living are reached
            // through the loaded units, so only the fallen need to ride along in the blob.
            var fallenBrothers = playerSoldiers.Values
                .Where(s => s.AssignedSquad == null)
                .ToList();
            CampaignIdentity campaignIdentity = global?.CampaignIdentity
                ?? throw new InvalidDataException(
                    "The current-format save contains no campaign identity.");
            CampaignEventLedger campaignEvents = _campaignEventDataAccess.GetLedger(
                dbCon,
                id => playerSoldiers.GetValueOrDefault(id) ?? fallenBrothers.FirstOrDefault(soldier => soldier.Id == id));
            ChapterChronicleLedger chronicle = _chapterChronicleDataAccess.GetLedger(dbCon, campaignEvents);
            IReadOnlyList<WorldControlEpisodeState> worldControlEpisodes =
                _worldControlEpisodeDataAccess.GetStates(dbCon);
            CampaignEventProjectionBuilder.PopulateSoldierServiceRecords(
                campaignEvents,
                playerSoldiers.Values.Concat(fallenBrothers),
                campaignIdentity);
            ChapterChronicleProjector.Reconcile(campaignEvents, chronicle, campaignIdentity);
            Dictionary<Date, List<EventHistory>> history =
                CampaignEventProjectionBuilder.BuildBattleHistoryView(chronicle, campaignEvents);
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
                HomeWorldPlanetId = global?.HomeWorldPlanetId,
                Recruitment = recruitment,
                Orders = _unitDataAccess.LoadedOrders.Values.ToList(),
                MedicalProcedures = medicalProcedures,
                History = history,
                FallenBrothers = fallenBrothers,
                Scenario = global?.Scenario,
                ChapterLoadoutDoctrine = chapterLoadoutDoctrine,
                CharacterLoadoutDoctrine = characterLoadoutDoctrine,
                EquipmentLoadoutDoctrine = equipmentLoadoutDoctrine,
                LastTurnReportSnapshot = lastTurnReportSnapshot,
                CampaignEventLedger = campaignEvents,
                ChapterChronicle = chronicle,
                CampaignIdentity = campaignIdentity,
                RelationshipLedger = relationshipLedger,
                WorldControlEpisodes = worldControlEpisodes,
                GhostPopulationSources = ghostPopulationSources,
                StrategicInvasionForces = strategicInvasionForces,
                OrkGhostSources = ghostPopulationSources.OfType<OrkGhostSource>().ToList(),
                OrkWaaaghs = strategicInvasionForces.OfType<OrkWaaaghSaveData>().ToList(),
                UpgradePending = false
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
                             LoadoutDoctrine chapterLoadoutDoctrine,
                             CharacterLoadoutDoctrine characterLoadoutDoctrine,
                             string schemaFilePath = null,
                             int? homeWorldPlanetId = null,
                             RecruitmentSaveData recruitment = null,
                             LastTurnReportSnapshot lastTurnReportSnapshot = null,
                             CampaignEventLedger campaignEventLedger = null,
                             ChapterChronicleLedger chapterChronicle = null,
                             CampaignIdentity campaignIdentity = null,
                             FactionRelationshipLedger relationshipLedger = null,
                             EquipmentLoadoutDoctrine equipmentLoadoutDoctrine = null,
                             IEnumerable<WorldControlEpisodeState> worldControlEpisodes = null,
                             IEnumerable<Order> additionalOrders = null,
                             IEnumerable<GhostPopulationSource> orkGhostSources = null,
                             IEnumerable<StrategicInvasionForce> orkWaaaghs = null)
        {
            ArgumentNullException.ThrowIfNull(campaignEventLedger);
            ArgumentNullException.ThrowIfNull(chapterChronicle);
            ArgumentNullException.ThrowIfNull(campaignIdentity);
            ArgumentNullException.ThrowIfNull(relationshipLedger);
            ArgumentNullException.ThrowIfNull(equipmentLoadoutDoctrine);

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
                              pledges, planets, fleets, playerSoldiers, fallenBrothers, squads,
                              ships, units, chapterLoadoutDoctrine, characterLoadoutDoctrine,
                              homeWorldPlanetId, recruitment, lastTurnReportSnapshot,
                              campaignEventLedger, chapterChronicle, campaignIdentity,
                              relationshipLedger, equipmentLoadoutDoctrine, worldControlEpisodes,
                              additionalOrders, orkGhostSources, orkWaaaghs);
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
                                   IEnumerable<Squad> squads,
                                   IEnumerable<Ship> ships,
                                   IEnumerable<Unit> units,
                                   LoadoutDoctrine chapterLoadoutDoctrine,
                                   CharacterLoadoutDoctrine characterLoadoutDoctrine,
                                   int? homeWorldPlanetId,
                                   RecruitmentSaveData recruitment,
                                   LastTurnReportSnapshot lastTurnReportSnapshot,
                                   CampaignEventLedger campaignEventLedger,
                                   ChapterChronicleLedger chapterChronicle,
                                   CampaignIdentity campaignIdentity,
                                   FactionRelationshipLedger relationshipLedger,
                                   EquipmentLoadoutDoctrine equipmentLoadoutDoctrine,
                                   IEnumerable<WorldControlEpisodeState> worldControlEpisodes,
                                   IEnumerable<Order> additionalOrders,
                                   IEnumerable<GhostPopulationSource> ghostPopulationSources,
                                   IEnumerable<StrategicInvasionForce> strategicInvasionForces)
        {
            string connection = BuildConnectionString(filePath, SqliteOpenMode.ReadWriteCreate);
            using IDbConnection dbCon = new SqliteConnection(connection);
            dbCon.Open();
            using (var transaction = dbCon.BeginTransaction())
            {
                try
                {
                    // Saving is passive: reconciliation and narration happen at
                    // load/new-game/turn boundaries before this transaction begins.
                    foreach(Character character in characters)
                    {
                        _planetDataAccess.SaveCharacter(transaction, character);
                    }
                    
                    foreach (Planet planet in planets)
                    {
                        _planetDataAccess.SavePlanet(transaction, planet);
                        _loadoutDoctrineDataAccess.SavePlanetDoctrine(transaction, planet);
                    }

                    SaveFactionRelationships(transaction, relationshipLedger);

                    _loadoutDoctrineDataAccess.SaveChapterDoctrine(transaction, chapterLoadoutDoctrine);

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

                    SaveGhostPopulationSources(transaction, ghostPopulationSources);
                    SaveStrategicInvasionForces(transaction, strategicInvasionForces);

                    // Fallen brothers belong to no squad, so they are not covered by the
                    // loop above; persist their base soldier rows (with a null SquadId) here.
                    List<PlayerSoldier> fallen = fallenBrothers?.ToList() ?? [];
                    foreach (PlayerSoldier fallenBrother in fallen)
                    {
                        _soldierDataAccess.SaveSoldier(transaction, fallenBrother);
                    }

                    // After the soldier rows exist: personal loadouts carry a foreign key to
                    // Soldier, and the insert drops entries for anyone no longer on the roster.
                    _loadoutDoctrineDataAccess.SaveCharacterDoctrine(transaction, characterLoadoutDoctrine);
                    _loadoutDoctrineDataAccess.SaveEquipmentDoctrine(transaction, equipmentLoadoutDoctrine);
                    // missions already written as region special missions, so order missions
                    // that reuse one are not inserted twice (primary-key conflict)
                    HashSet<int> savedMissionIds = planets
                        .SelectMany(p => p.Regions)
                        .SelectMany(r => r.SpecialMissions)
                        .Select(m => m.Id)
                        .ToHashSet();
                    // Orders are reachable through either participant collection. Character-only
                    // orders must be persisted even when no squad points back to them.
                    var orders = squads.Select(s => s.CurrentOrders)
                                       .Concat(playerSoldiers.Select(s => s.CurrentOrder))
                                       .Concat(additionalOrders ?? Enumerable.Empty<Order>())
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
                    foreach (PlayerSoldier playerSoldier in playerSoldiers)
                    {
                        _individualPostingDataAccess.Save(transaction, playerSoldier);
                    }
                    foreach (MedicalProcedure procedure in medicalProcedures ?? [])
                    {
                        _medicalProcedureDataAccess.SaveProcedure(transaction, procedure);
                    }
                    _recruitmentDataAccess.SaveData(transaction, recruitment);
                    _globalDataAccess.SaveGlobalData(transaction, currentDate, requisition,
                                                     geneseedStockpile, geneseedPurity, scenario,
                                                     homeWorldPlanetId, campaignIdentity);
                    _campaignEventDataAccess.SaveLedger(transaction, campaignEventLedger);
                    _chapterChronicleDataAccess.SaveLedger(transaction, chapterChronicle);
                    _worldControlEpisodeDataAccess.SaveStates(transaction, worldControlEpisodes);
                    _lastTurnReportDataAccess.SaveSnapshot(transaction, lastTurnReportSnapshot);
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

        private static List<GhostPopulationSource> LoadGhostPopulationSources(
            IDbConnection connection,
            IReadOnlyDictionary<int, Faction> factionMap,
            IReadOnlyDictionary<int, PlanetTemplate> planetTemplateMap)
        {
            bool genericTable = HasTable(connection, "GhostPopulationSource");
            List<GhostPopulationSource> sources = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = genericTable
                ? "SELECT Id, FactionId, x, y, PlanetTemplateId, Population, PopulationCapacity, Consolidation FROM GhostPopulationSource"
                : "SELECT Id, x, y, PlanetTemplateId, Population, PopulationCapacity, Consolidation FROM OrkGhostSource";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                int factionOrdinal = genericTable ? 1 : -1;
                int xOrdinal = genericTable ? 2 : 1;
                int yOrdinal = genericTable ? 3 : 2;
                int templateOrdinal = genericTable ? 4 : 3;
                int populationOrdinal = genericTable ? 5 : 4;
                int capacityOrdinal = genericTable ? 6 : 5;
                int consolidationOrdinal = genericTable ? 7 : 6;
                int templateId = reader.GetInt32(templateOrdinal);
                if (!planetTemplateMap.TryGetValue(templateId, out PlanetTemplate template))
                {
                    throw new InvalidDataException(
                        $"Ghost population source {id} references missing planet template {templateId}.");
                }
                Faction faction = genericTable && !reader.IsDBNull(factionOrdinal)
                    ? factionMap.GetValueOrDefault(reader.GetInt32(factionOrdinal))
                    : null;
                sources.Add(new OrkGhostSource(
                    id,
                    new Coordinate((ushort)reader.GetInt32(xOrdinal), (ushort)reader.GetInt32(yOrdinal)),
                    template,
                    reader.GetInt64(populationOrdinal),
                    reader.GetInt64(capacityOrdinal),
                    reader.GetDouble(consolidationOrdinal),
                    faction));
            }
            return sources;
        }

        private static List<StrategicInvasionForceSaveData> LoadStrategicInvasionForces(IDbConnection connection)
        {
            string table = HasTable(connection, "StrategicInvasionForce")
                ? "StrategicInvasionForce"
                : "OrkWaaagh";
            List<StrategicInvasionForceSaveData> forces = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = $@"SELECT Id, FactionId, CommandSquadId, CurrentRegionId,
                                           OriginPlanetId, DestinationPlanetId,
                                           TravelWeeksRemaining, TransitBattleValue,
                                           IsActive
                                    FROM {table}";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                forces.Add(new OrkWaaaghSaveData
                {
                    Id = reader.GetInt64(0),
                    FactionId = reader.GetInt32(1),
                    CommandSquadId = reader.GetInt32(2),
                    CurrentRegionId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    OriginPlanetId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    DestinationPlanetId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    TravelWeeksRemaining = reader.GetInt32(6),
                    TransitBattleValue = reader.GetInt64(7),
                    IsActive = reader.GetBoolean(8)
                });
            }
            return forces;
        }

        private static void SaveGhostPopulationSources(
            IDbTransaction transaction,
            IEnumerable<GhostPopulationSource> sources)
        {
            foreach (GhostPopulationSource source in sources ?? Enumerable.Empty<GhostPopulationSource>())
            {
                using IDbCommand command = transaction.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO GhostPopulationSource
                    (Id, FactionId, x, y, PlanetTemplateId, Population, PopulationCapacity, Consolidation)
                    VALUES (@id, @factionId, @x, @y, @planetTemplateId, @population, @populationCapacity, @consolidation);";
                command.AddParam("@id", source.Id);
                command.AddParam("@factionId", source.FactionId);
                command.AddParam("@x", source.Position.X);
                command.AddParam("@y", source.Position.Y);
                command.AddParam("@planetTemplateId", source.WorldType.Id);
                command.AddParam("@population", source.Population);
                command.AddParam("@populationCapacity", source.PopulationCapacity);
                command.AddParam("@consolidation", source.Consolidation);
                command.ExecuteNonQuery();
            }
        }

        private static void SaveStrategicInvasionForces(
            IDbTransaction transaction,
            IEnumerable<StrategicInvasionForce> forces)
        {
            foreach (StrategicInvasionForce force in forces ?? Enumerable.Empty<StrategicInvasionForce>())
            {
                if (force?.CommandSquad?.ParentUnit == null)
                {
                    throw new InvalidDataException(
                        $"Strategic invasion force {force?.Id.ToString() ?? "<null>"} has no persistent command unit.");
                }
                using IDbCommand command = transaction.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO StrategicInvasionForce
                    (Id, FactionId, CommandSquadId, CurrentRegionId, OriginPlanetId,
                     DestinationPlanetId, TravelWeeksRemaining, TransitBattleValue,
                     IsActive)
                    VALUES (@id, @factionId, @commandSquadId, @currentRegionId, @originPlanetId,
                            @destinationPlanetId, @travelWeeksRemaining, @transitBattleValue,
                            @isActive);";
                command.AddParam("@id", force.Id);
                command.AddParam("@factionId", force.Faction.Id);
                command.AddParam("@commandSquadId", force.CommandSquad.Id);
                command.AddParam("@currentRegionId", force.CurrentRegion?.Id);
                command.AddParam("@originPlanetId", force.OriginPlanet?.Id);
                command.AddParam("@destinationPlanetId", force.DestinationPlanet?.Id);
                command.AddParam("@travelWeeksRemaining", force.TravelWeeksRemaining);
                command.AddParam("@transitBattleValue", force.TransitBattleValue);
                command.AddParam("@isActive", force.IsActive ? 1 : 0);
                command.ExecuteNonQuery();
            }
        }

        private static bool HasTable(IDbConnection connection, string tableName)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1";
            command.AddParam("@name", tableName);
            return command.ExecuteScalar() != null;
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

        private static FactionRelationshipLedger LoadFactionRelationships(
            IDbConnection connection,
            IReadOnlyDictionary<int, Faction> factionMap)
        {
            FactionRelationshipLedger ledger = new();
            foreach (Faction faction in factionMap.Values)
            {
                ledger.RegisterFaction(faction);
            }

            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT LowerFactionId, HigherFactionId, Stance
                                    FROM FactionRelationship";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int lowerId = reader.GetInt32(0);
                int higherId = reader.GetInt32(1);
                if (lowerId >= higherId
                    || !factionMap.ContainsKey(lowerId)
                    || !factionMap.ContainsKey(higherId))
                {
                    throw new InvalidDataException(
                        $"Save contains an invalid faction relationship pair ({lowerId}, {higherId}).");
                }

                int stanceValue = reader.GetInt32(2);
                if (stanceValue is < (int)FactionStance.Neutral or > (int)FactionStance.Allied)
                {
                    throw new InvalidDataException(
                        $"Save contains an invalid faction relationship stance {stanceValue}.");
                }

                Faction lowerFaction = factionMap[lowerId];
                Faction higherFaction = factionMap[higherId];
                if ((lowerFaction.HasBehavior(FactionBehavior.UniversallyHostile)
                    || higherFaction.HasBehavior(FactionBehavior.UniversallyHostile))
                    && stanceValue != (int)FactionStance.Hostile)
                {
                    throw new InvalidDataException(
                        $"Save contains a non-hostile relationship for universally hostile factions ({lowerId}, {higherId}).");
                }

                try
                {
                    ledger.LoadEntry(
                        lowerId,
                        higherId,
                        (FactionStance)stanceValue);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or InvalidOperationException)
                {
                    throw new InvalidDataException(
                        $"Save contains an illegal faction relationship ({lowerId}, {higherId}).",
                        exception);
                }
            }

            return ledger;
        }

        private static void SaveFactionRelationships(
            IDbTransaction transaction,
            FactionRelationshipLedger relationshipLedger)
        {
            if (relationshipLedger == null) return;

            foreach (KeyValuePair<FactionPair, FactionStance> entry in relationshipLedger.Entries)
            {
                if (entry.Value is not FactionStance.Neutral and not FactionStance.Allied)
                {
                    throw new InvalidDataException(
                        $"Cannot persist faction relationship {entry.Key}: invalid stance.");
                }

                using IDbCommand command = transaction.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO FactionRelationship
                    (LowerFactionId, HigherFactionId, Stance)
                    VALUES (@lowerFactionId, @higherFactionId, @stance);";
                command.AddParam("@lowerFactionId", entry.Key.LowerFactionId);
                command.AddParam("@higherFactionId", entry.Key.HigherFactionId);
                command.AddParam("@stance", (int)entry.Value);
                command.ExecuteNonQuery();
            }
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
