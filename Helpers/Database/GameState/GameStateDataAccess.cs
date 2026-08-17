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
                            IReadOnlyDictionary<int, EquipmentKitTemplate> equipmentKits = null)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("The selected save file does not exist.", fullPath);
            }

            string connection = BuildConnectionString(fullPath, SqliteOpenMode.ReadOnly);
            using IDbConnection dbCon = new SqliteConnection(connection);
            dbCon.Open();
            int saveVersion = _globalDataAccess.EnsureCompatibleSaveVersion(dbCon);
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
            var missionMap = _planetDataAccess.PopulateRegionMissions(dbCon, regions, factionMap);
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
            var recruitment = _recruitmentDataAccess.GetData(dbCon);
            int highestIdentity = soldiers.Keys
                .Concat(recruitment.Aspirants.Select(aspirant => aspirant.Id))
                .DefaultIfEmpty(0)
                .Max();
            SoldierFactory.Instance.SetCurrentHighestSoldierId(highestIdentity);
            var playerSoldiers = _playerSoldierDataAccess.GetData(dbCon, soldiers);
            // Must run here, not inside GetSquadsByUnitId where the orders are built: the
            // PlayerSoldier constructor swaps the wrapper into the squad in place of the base
            // Soldier, so attachments have to be resolved against the wrappers that the call
            // above just produced (Design/Reference/SpecialistAttachment.md §6.3).
            _unitDataAccess.PopulateOrderAttachments(dbCon, squadMap, playerSoldiers);
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
                             EquipmentLoadoutDoctrine equipmentLoadoutDoctrine = null)
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
                              relationshipLedger, equipmentLoadoutDoctrine);
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
                                   EquipmentLoadoutDoctrine equipmentLoadoutDoctrine)
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
                    // Under the >=1-assigned-squad invariant the squad walk alone is
                    // sufficient, but an order is now also reachable through an attached
                    // specialist. Concatenating that side makes the coverage explicit rather
                    // than accidental (Design/Reference/SpecialistAttachment.md §6.2).
                    var orders = squads.Select(s => s.CurrentOrders)
                                       .Concat(playerSoldiers.Select(s => s.AttachedOrder))
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
                    _recruitmentDataAccess.SaveData(transaction, recruitment);
                    _globalDataAccess.SaveGlobalData(transaction, currentDate, requisition,
                                                     geneseedStockpile, geneseedPurity, scenario,
                                                     homeWorldPlanetId, campaignIdentity);
                    _campaignEventDataAccess.SaveLedger(transaction, campaignEventLedger);
                    _chapterChronicleDataAccess.SaveLedger(transaction, chapterChronicle);
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
