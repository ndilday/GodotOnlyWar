using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using OnlyWar.Models.FactionBehaviors;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace OnlyWar.Helpers.Database.GameRules
{
    public class GameRulesBlob
    {
        public IReadOnlyList<Faction> Factions { get; set; }
        public IReadOnlyDictionary<int, BaseSkill> BaseSkills { get; set; }
        public IReadOnlyList<SkillTemplate> SkillTemplates { get; set; }
        public IReadOnlyDictionary<int, List<HitLocationTemplate>> BodyTemplates { get; set; }
        public IReadOnlyDictionary<int, PlanetTemplate> PlanetTemplates { get; set; }
        public IReadOnlyDictionary<int, RangedWeaponTemplate> RangedWeaponTemplates { get; set; }
        public IReadOnlyDictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplates { get; set; }
        public IReadOnlyDictionary<int, WeaponSet> WeaponSets { get; set; }
        public EquipmentRulesCatalog EquipmentCatalog { get; set; }
        public IReadOnlyDictionary<int, TrainingProfile> TrainingProfiles { get; set; }
        public IReadOnlyList<PlanetTemplateEligibilityAssignment> PlanetTemplateEligibilityAssignments { get; set; }
        public ScoutTrainingOptionCatalog ScoutTrainingOptions { get; set; }
        public IReadOnlyList<Models.Soldiers.Ratings.RatingDefinition> RatingDefinitions { get; set; }
        public IReadOnlyList<Models.Soldiers.Ratings.RatingAwardTier> RatingAwardTiers { get; set; }
        public IReadOnlyList<Models.Soldiers.Ratings.RatingConsumerAssignment> RatingConsumerAssignments { get; set; }
        public IReadOnlyList<Models.Soldiers.Ratings.AwardFamilyDefinition> AwardFamilies { get; set; }
        public IReadOnlyList<SkillRoleAssignment> SkillRoleAssignments { get; set; }
        public IReadOnlyList<FactionRoleAssignment> FactionRoleAssignments { get; set; }
        public IReadOnlyList<ScenarioProfile> ScenarioProfiles { get; set; }
        public IReadOnlyList<ScenarioFactionOption> ScenarioFactionOptions { get; set; }
        public IReadOnlyList<FactionPlanetPresenceRule> FactionPlanetPresenceRules { get; set; }
        public IReadOnlyList<ChapterGenerationProfileData> ChapterGenerationProfiles { get; set; }
        public IReadOnlyList<SectorGenerationProfile> SectorGenerationProfiles { get; set; }
        public IReadOnlyList<FactionBehaviorRulesProfile> FactionBehaviorRulesProfiles { get; set; }

    }

    public class GameRulesDataAccess
    {
        private readonly BaseSkillDataAccess _baseSkillDataAccess;
        private readonly HitLocationTemplateDataAccess _hitLocationDataAccess;
        private readonly FleetDataAccess _fleetDataAccess;
        private readonly PlanetTemplateDataAccess _planetDataAccess;
        private readonly SquadTemplateDataAccess _squadDataAccess;
        private readonly RatingDataAccess _ratingDataAccess;
        private readonly PlanetTemplateEligibilityDataAccess _planetTemplateEligibilityDataAccess;
        private readonly SkillRoleDataAccess _skillRoleDataAccess;
        private readonly FactionGenerationPolicyDataAccess _factionGenerationPolicyDataAccess;
        private readonly ChapterGenerationPolicyDataAccess _chapterGenerationPolicyDataAccess;
        private readonly ScoutTrainingOptionDataAccess _scoutTrainingOptionDataAccess;
        private readonly SectorGenerationProfileDataAccess _sectorGenerationProfileDataAccess;
        private readonly FactionBehaviorRulesDataAccess _factionBehaviorRulesDataAccess;

        private static GameRulesDataAccess _instance;

        private GameRulesDataAccess()
        {
            _baseSkillDataAccess = new BaseSkillDataAccess();
            _hitLocationDataAccess = new HitLocationTemplateDataAccess();
            _fleetDataAccess = new FleetDataAccess();
            _planetDataAccess = new PlanetTemplateDataAccess();
            _squadDataAccess = new SquadTemplateDataAccess();
            _ratingDataAccess = new RatingDataAccess();
            _planetTemplateEligibilityDataAccess = new PlanetTemplateEligibilityDataAccess();
            _skillRoleDataAccess = new SkillRoleDataAccess();
            _factionGenerationPolicyDataAccess = new FactionGenerationPolicyDataAccess();
            _chapterGenerationPolicyDataAccess = new ChapterGenerationPolicyDataAccess();
            _scoutTrainingOptionDataAccess = new ScoutTrainingOptionDataAccess();
            _sectorGenerationProfileDataAccess = new SectorGenerationProfileDataAccess();
            _factionBehaviorRulesDataAccess = new FactionBehaviorRulesDataAccess();
        }

        public static GameRulesDataAccess Instance
        {
            get
            {
                if(_instance == null)
                {
                    _instance = new GameRulesDataAccess();
                }
                return _instance;
            }
        }
        public GameRulesBlob GetData(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("The game rules database is missing.", fullPath);
            }

            var connectionString = new SqliteConnectionStringBuilder()
            {
                Mode = SqliteOpenMode.ReadOnly,
                DataSource = fullPath
            }.ToString();
            using IDbConnection dbCon = new SqliteConnection(connectionString);
            dbCon.Open();
            // Validate the schema before any loader can turn a missing table into a provider
            // exception or a later null/dictionary failure. Optional extension tables are not
            // listed by RulesDatabaseSchemaValidator and retain their explicit fallbacks below.
            RulesDatabaseSchemaValidator.Validate(dbCon);
            RulesDatabaseReferenceValidator.Validate(dbCon);
            var baseSkills = _baseSkillDataAccess.GetBaseSkills(dbCon);
            var skillRoleAssignments = _skillRoleDataAccess.GetSkillRoleAssignments(dbCon);
            var skillTemplates = GetSkillTemplates(dbCon, baseSkills);
            var hitLocations = _hitLocationDataAccess.GetHitLocationsByBodyId(dbCon);
            var squadDataBlob = _squadDataAccess.GetSquadDataBlob(dbCon, baseSkills, hitLocations);
            var scoutTrainingOptions = _scoutTrainingOptionDataAccess.GetCatalog(
                dbCon,
                squadDataBlob.TrainingProfilesById);
            var unitSquadTemplates = GetSquadTemplatesByUnitTemplateId(
                dbCon, squadDataBlob.SquadTemplatesById);
            var unitHierarchy = GetUnitTemplateHierarchy(dbCon);
            var unitTemplates = 
                GetUnitTemplatesByFactionId(dbCon, unitHierarchy, unitSquadTemplates, 
                                            squadDataBlob.SquadTemplatesById);
            var planetTemplates = _planetDataAccess.GetData(dbCon);
            var planetTemplateEligibilityAssignments =
                _planetTemplateEligibilityDataAccess.GetData(dbCon);

            var fleetDataBlob = _fleetDataAccess.GetFleetData(dbCon);
            var factions = GetFactionTemplates(dbCon, squadDataBlob.SpeciesByFactionId,  
                                               squadDataBlob.SoldierTemplatesByFactionId, 
                                               squadDataBlob.SquadTemplatesByFactionId, 
                                               unitTemplates,
                                               fleetDataBlob.BoatTemplates, 
                                               fleetDataBlob.ShipTemplates, 
                                               fleetDataBlob.FleetTemplates);
            var chapterGenerationProfiles =
                _chapterGenerationPolicyDataAccess.GetProfiles(dbCon);
            var factionRoleAssignments =
                _factionGenerationPolicyDataAccess.GetFactionRoleAssignments(dbCon);
            var scenarioFactionOptions =
                _factionGenerationPolicyDataAccess.GetScenarioFactionOptions(dbCon);
            var scenarioProfiles =
                _factionGenerationPolicyDataAccess.GetScenarioProfiles(
                    dbCon, scenarioFactionOptions);
            var factionPlanetPresenceRules =
                _factionGenerationPolicyDataAccess.GetFactionPlanetPresenceRules(dbCon);
            var sectorGenerationProfiles =
                _sectorGenerationProfileDataAccess.GetProfiles(dbCon);
            var factionBehaviorRulesProfiles = _factionBehaviorRulesDataAccess.GetProfiles(dbCon);
            EquipmentRulesCatalog compatibilityEquipmentCatalog = EquipmentRulesCatalog.FromLegacyRules(
                squadDataBlob.RangedWeaponTemplateMap,
                squadDataBlob.MeleeWeaponTemplateMap,
                squadDataBlob.ArmorTemplates,
                squadDataBlob.WeaponSetMap,
                squadDataBlob.SquadTemplatesById.Values.ToList());
            EquipmentRulesCatalog equipmentCatalog;
            try
            {
                equipmentCatalog = EquipmentRulesCatalog.FromDatabase(
                    dbCon,
                    baseSkills,
                    compatibilityEquipmentCatalog);
            }
            catch (DbException exception) when (
                exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // Focused legacy fixtures may not carry the Alpha 0.8 itemized tables. The
                // shipped database does, and therefore never takes this path.
                equipmentCatalog = compatibilityEquipmentCatalog;
            }
            var ratingDefinitions = _ratingDataAccess.GetRatingDefinitions(dbCon);
            var ratingAwardTiers = _ratingDataAccess.GetRatingAwardTiers(dbCon);
            var ratingConsumerAssignments = _ratingDataAccess.GetRatingConsumerAssignments(dbCon);
            var awardFamilies = _ratingDataAccess.GetAwardFamilies(dbCon);
            GameRulesBlob rules = new GameRulesBlob
            {
                Factions = factions,
                BaseSkills = baseSkills,
                SkillTemplates = skillTemplates,
                BodyTemplates = hitLocations,
                PlanetTemplates = planetTemplates,
                RangedWeaponTemplates = squadDataBlob.RangedWeaponTemplateMap,
                MeleeWeaponTemplates = squadDataBlob.MeleeWeaponTemplateMap,
                WeaponSets = squadDataBlob.WeaponSetMap,
                EquipmentCatalog = equipmentCatalog,
                TrainingProfiles = squadDataBlob.TrainingProfilesById,
                PlanetTemplateEligibilityAssignments = planetTemplateEligibilityAssignments,
                ScoutTrainingOptions = scoutTrainingOptions,
                RatingDefinitions = ratingDefinitions,
                RatingAwardTiers = ratingAwardTiers,
                RatingConsumerAssignments = ratingConsumerAssignments,
                AwardFamilies = awardFamilies,
                SkillRoleAssignments = skillRoleAssignments,
                FactionRoleAssignments = factionRoleAssignments,
                ScenarioProfiles = scenarioProfiles,
                ScenarioFactionOptions = scenarioFactionOptions,
                FactionPlanetPresenceRules = factionPlanetPresenceRules,
                ChapterGenerationProfiles = chapterGenerationProfiles,
                SectorGenerationProfiles = sectorGenerationProfiles,
                FactionBehaviorRulesProfiles = factionBehaviorRulesProfiles
            };
            RulesDatabaseValidator.Validate(rules);
            return rules;
        }

        private List<Faction> GetFactionTemplates(IDbConnection connection,
                                         Dictionary<int, List<Species>> factionSpeciesMap,
                                         Dictionary<int, List<SoldierTemplate>> factionSoldierTemplateMap,
                                         Dictionary<int, List<SquadTemplate>> factionSquadMap,
                                         Dictionary<int, List<UnitTemplate>> factionUnitMap,
                                         Dictionary<int, List<BoatTemplate>> factionBoatMap,
                                         Dictionary<int, List<ShipTemplate>> factionShipMap,
                                         Dictionary<int, List<FleetTemplate>> factionFleetMap)
        {
            List<Faction> factionList = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT Id, Name, color, IsPlayerFaction, IsDefaultFaction,
                    Behavior, GrowthType FROM Faction ORDER BY Id";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader[1].ToString();
                    Color color = ConvertDatabaseObjectToColor(reader[2]);
                    bool isPlayer = Convert.ToBoolean(reader[3]);
                    bool isDefault = Convert.ToBoolean(reader[4]);
                    FactionBehavior behavior = (FactionBehavior)reader.GetInt32(5);
                    GrowthType growthType = (GrowthType)reader.GetInt32(6);
                    ValidateFactionBehavior(id, name, behavior, isPlayer, isDefault, growthType);

                    var speciesMap = factionSpeciesMap.ContainsKey(id) ?
                        factionSpeciesMap[id].ToDictionary(st => st.Id) : null;
                    var soldierMap = factionSoldierTemplateMap.ContainsKey(id) ?
                        factionSoldierTemplateMap[id].ToDictionary(st => st.Id) : null;
                    var squadMap = factionSquadMap.ContainsKey(id) ?
                        factionSquadMap[id].ToDictionary(st => st.Id) : null;
                    var unitMap = factionUnitMap.ContainsKey(id) ?
                        factionUnitMap[id].ToDictionary(ut => ut.Id) : null;
                    Dictionary<int, BoatTemplate> boatMap = null;
                    Dictionary<int, ShipTemplate> shipMap = null;
                    Dictionary<int, FleetTemplate> fleetMap = null;
                    if (factionBoatMap.TryGetValue(id, out List<BoatTemplate> boats))
                    {
                        boatMap = boats.ToDictionary(bt => bt.Id);
                    }
                    if (factionShipMap.TryGetValue(id, out List<ShipTemplate> ships))
                    {
                        shipMap = ships.ToDictionary(st => st.Id);
                    }
                    if (factionFleetMap.TryGetValue(id, out List<FleetTemplate> fleets))
                    {
                        fleetMap = fleets.ToDictionary(ft => ft.Id);
                    }

                    Faction factionTemplate = new Faction(id, name, color, isPlayer, isDefault,
                                                          behavior, growthType, speciesMap,
                                                          soldierMap, squadMap, unitMap, boatMap,
                                                          shipMap, fleetMap);
                    factionList.Add(factionTemplate);
                }
            }
            return factionList;
        }

        private static void ValidateFactionBehavior(
            int id,
            string name,
            FactionBehavior behavior,
            bool isPlayer,
            bool isDefault,
            GrowthType growthType)
        {
            const FactionBehavior allKnown =
                FactionBehavior.CanInfiltrate
                | FactionBehavior.PopulationIsMilitary
                | FactionBehavior.InvadesOnVictory
                | FactionBehavior.DefendsHostWhileHidden
                | FactionBehavior.OffersExternalEnemyTruce
                | FactionBehavior.UniversallyHostile
                | FactionBehavior.Indelible
                | FactionBehavior.HasGhostPlanets
                | FactionBehavior.HasDormantPopulations
                | FactionBehavior.GeneratesInvasions
                | FactionBehavior.MobMentality;
            if ((behavior & ~allKnown) != 0)
            {
                throw new InvalidDataException(
                    $"Faction '{name}' ({id}) contains unknown behavior bits {(int)(behavior & ~allKnown)}.");
            }
            if ((behavior.HasFlag(FactionBehavior.UniversallyHostile)
                    && (isPlayer || isDefault))
                || (behavior.HasFlag(FactionBehavior.OffersExternalEnemyTruce)
                    && !growthType.Equals(GrowthType.Unrest)))
            {
                throw new InvalidDataException(
                    $"Faction '{name}' ({id}) contains an illegal behavior composition.");
            }
        }

        private List<SkillTemplate> GetSkillTemplates(IDbConnection connection,
                                                      Dictionary<int, BaseSkill> baseSkillMap)
        {
            List<SkillTemplate> skillTemplateList = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SkillTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int baseSkillId = reader.GetInt32(1);
                    float baseValue = Convert.ToSingle(reader[2]);
                    float stdDev = Convert.ToSingle(reader[3]);
                    SkillTemplate skillTemplate = new SkillTemplate
                    {
                        BaseSkill = RulesDatabaseLookup.Require(
                            baseSkillMap,
                            baseSkillId,
                            $"SkillTemplate {id}.BaseSkillId"),
                        BaseValue = baseValue,
                        StandardDeviation = stdDev
                    };

                    skillTemplateList.Add(skillTemplate);
                }
            }
            return skillTemplateList;
        }

        private Dictionary<int, List<int>> GetUnitTemplateHierarchy(IDbConnection connection)
        {
            Dictionary<int, List<int>> unitTemplateTree = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM UnitTemplateTree";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int parentUnitId = reader.GetInt32(1);
                    int childUnitId = reader.GetInt32(2);

                    if (!unitTemplateTree.ContainsKey(parentUnitId))
                    {
                        unitTemplateTree[parentUnitId] = [];
                    }
                    unitTemplateTree[parentUnitId].Add(childUnitId);
                }
            }
            return unitTemplateTree;
        }

        private Dictionary<int, List<UnitTemplate>> GetUnitTemplatesByFactionId(IDbConnection connection,
                                                                                Dictionary<int, List<int>> unitTemplateTree,
                                                                                Dictionary<int, List<SquadTemplateSlot>> unitSquadMap,
                                                                                Dictionary<int, SquadTemplate> squadTemplateMap)
        {
            Dictionary<int, List<UnitTemplate>> factionUnitTemplateMap = [];
            Dictionary<int, UnitTemplate> unitTemplateMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM UnitTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    bool isTop = Convert.ToBoolean(reader[3]);
                    SquadTemplate hqSquad;
                    if (reader[4].GetType() != typeof(DBNull))
                    {
                        int hqSquadId = reader.GetInt32(4);
                        hqSquad = RulesDatabaseLookup.Require(
                            squadTemplateMap,
                            hqSquadId,
                            $"UnitTemplate {id}.HQSquadTemplateId");
                    }
                    else
                    {
                        hqSquad = null;
                    }

                    if (!factionUnitTemplateMap.ContainsKey(factionId))
                    {
                        factionUnitTemplateMap[factionId] = [];
                    }
                    List<SquadTemplateSlot> squadSlots = unitSquadMap.TryGetValue(id, out List<SquadTemplateSlot> slots)
                        ? slots
                        : [];
                    UnitTemplate unitTemplate = new UnitTemplate(id, name, isTop, hqSquad, squadSlots);
                    factionUnitTemplateMap[factionId].Add(unitTemplate);
                    unitTemplateMap[id] = unitTemplate;
                }

                // hydrate unit children
                foreach (KeyValuePair<int, List<int>> kvp in unitTemplateTree)
                {
                    UnitTemplate parent = RulesDatabaseLookup.Require(
                        unitTemplateMap,
                        kvp.Key,
                        "UnitTemplateTree.ParentUnitTemplateId");
                    parent.SetChildUnits(kvp.Value
                        .Select(childId => RulesDatabaseLookup.Require(
                            unitTemplateMap,
                            childId,
                            $"UnitTemplateTree child of {kvp.Key}"))
                        .ToList());
                }
            }
            return factionUnitTemplateMap;
        }

        private Dictionary<int, List<SquadTemplateSlot>> GetSquadTemplatesByUnitTemplateId(IDbConnection connection,
                                                                                       Dictionary<int, SquadTemplate> squadTemplateMap)
        {
            Dictionary<int, List<SquadTemplateSlot>> unitSquadTemplateMap = [];
            using (var command = connection.CreateCommand())
            {
                // Columns: Id, UnitTemplateId, SquadTemplateId, MinCount, MaxCount.
                // MinCount squads are created up front; MaxCount caps how many a
                // unit may hold (see SquadTemplateSlot / migrate-squad-caps).
                command.CommandText = "SELECT * FROM UnitTemplateSquadTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int unitTemplateId = reader.GetInt32(1);
                    int squadTemplateId = reader.GetInt32(2);
                    int minCount = reader.GetInt32(3);
                    int maxCount = reader.GetInt32(4);

                    if (!unitSquadTemplateMap.ContainsKey(unitTemplateId))
                    {
                        unitSquadTemplateMap[unitTemplateId] = [];
                    }
                    unitSquadTemplateMap[unitTemplateId].Add(
                        new SquadTemplateSlot(
                            RulesDatabaseLookup.Require(
                                squadTemplateMap,
                                squadTemplateId,
                                $"UnitTemplateSquadTemplate {unitTemplateId}.SquadTemplateId"),
                            minCount,
                            maxCount));
                }
            }
            return unitSquadTemplateMap;
        }

        private Color ConvertDatabaseObjectToColor(object obj)
        {
            long colorInt = (long)obj;
            long a = (colorInt & 0xFF) << 24;
            long argb = a + (colorInt >> 8);
            /*long r = colorInt / 0x01000000;
            long g = (colorInt / 0x00010000) & 0x000000ff;
            long b = (colorInt / 0x00000100) & 0x000000ff;
            long a = colorInt & 0x000000ff;*/
            return Color.FromArgb((int)argb);
        }
    }
}
