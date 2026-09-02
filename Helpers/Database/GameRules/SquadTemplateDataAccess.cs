using OnlyWar.Models;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameRules
{
    public class SquadTemplateDataBlob
    {
        public Dictionary<int, ArmorTemplate> ArmorTemplates { get; set; }
        public Dictionary<int, RangedWeaponTemplate> RangedWeaponTemplateMap { get; set; }
        public Dictionary<int, MeleeWeaponTemplate> MeleeWeaponTemplateMap { get; set; }
        public Dictionary<int, WeaponSet> WeaponSetMap { get; set; }
        public Dictionary<int, List<SoldierTemplate>> SoldierTemplatesByFactionId { get; set; }
        public Dictionary<int, List<Species>> SpeciesByFactionId { get; set; }
        public Dictionary<int, List<SquadTemplate>> SquadTemplatesByFactionId { get; set; }
        public Dictionary<int, SquadTemplate> SquadTemplatesById { get; set; }
        public Dictionary<int, TrainingProfile> TrainingProfilesById { get; set; }
    }

    public class SquadTemplateDataAccess
    {
        public SquadTemplateDataBlob GetSquadDataBlob(IDbConnection connection, 
                                              Dictionary<int, BaseSkill> baseSkillMap,
                                              Dictionary<int, List<HitLocationTemplate>> hitLocationMap)
        {
            var attributes = GetAttributeTemplates(connection);
            var soldierTemplateSkills = GetSoldierMosTrainingBySoldierTemplateId(connection, baseSkillMap);
            var trainingProfiles = GetTrainingProfilesById(connection, baseSkillMap);
            var meleeWeapons = GetMeleeWeaponTemplates(connection, baseSkillMap);
            var species = GetSpeciesByFactionId(connection, attributes, hitLocationMap, meleeWeapons);
            var soldierTemplateRequirements = GetSoldierTemplateRequirements(connection);
            var ammunitionTypes = GetAmmunitionTypes(connection);
            var rangedWeapons = GetRangedWeaponTemplates(connection, baseSkillMap, ammunitionTypes);
            // Weapon sets are built before soldier templates because every role's weapon menu
            // (see SoldierTemplate.WeaponOptionsByGroup) references them.
            var weaponSets = GetWeaponSetMap(connection, meleeWeapons, rangedWeapons);
            var weaponOptionsByTemplateId = GetWeaponOptionsBySoldierTemplateId(connection, weaponSets);
            var soldierTemplates =
                GetSoldierTemplatesByFactionId(
                    connection,
                    soldierTemplateSkills,
                    species,
                    trainingProfiles,
                    soldierTemplateRequirements,
                    weaponOptionsByTemplateId);
            var armorTemplates = GetArmorTemplates(connection);
            Dictionary<(int ElementId, string OptionGroup),
                (SquadQuotaModelBasis ModelBasis, int ModelsPerBlock, int SlotsPerBlock)>
                quotaScaling = [];
            try
            {
                quotaScaling = GetElementQuotaScalingByQuotaKey(connection);
            }
            catch (DbException exception) when (
                exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // The scaling relation is optional for older rules databases; absent rows retain
                // the fixed MinimumRequired/MaximumAllowed behavior.
            }
            var elementQuotas = GetElementQuotasByElementId(connection, quotaScaling);
            Dictionary<int, ArmorTemplate> elementArmors = [];
            try
            {
                elementArmors = GetElementArmorsByElementId(connection, armorTemplates);
            }
            catch (DbException exception) when (
                exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // The relation is optional for legacy rules fixtures. Missing bindings inherit
                // the squad template's armor.
            }
            Dictionary<int, PersonalEquipmentRole> personalEquipmentRoles = [];
            Dictionary<int, int> personalRoleByElement = [];
            bool hasExplicitPersonalRoleBindings = false;
            try
            {
                personalEquipmentRoles = GetPersonalEquipmentRoles(connection);
                personalRoleByElement = GetPersonalRoleBindingsByElementId(connection);
                hasExplicitPersonalRoleBindings = true;
            }
            catch (DbException exception) when (
                exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // A pre-foundation focused fixture has no explicit role table. The production
                // rules database does, and therefore never uses the compatibility inference below.
            }
            var basicSoldierTemplateMap = soldierTemplates.Values
                                                          .SelectMany(st => st)
                                                          .ToDictionary(st => st.Id);
            var squadElements = GetSquadTemplateElementsBySquadId(
                connection,
                basicSoldierTemplateMap,
                weaponSets,
                elementArmors,
                elementQuotas,
                personalEquipmentRoles,
                personalRoleByElement,
                hasExplicitPersonalRoleBindings);
            var squadTemplates = GetSquadTemplatesById(connection, squadElements, weaponSets,
                                                       armorTemplates, trainingProfiles);
            return new SquadTemplateDataBlob
            {
                ArmorTemplates = armorTemplates,
                MeleeWeaponTemplateMap= meleeWeapons,
                RangedWeaponTemplateMap = rangedWeapons,
                WeaponSetMap = weaponSets,
                SquadTemplatesByFactionId = squadTemplates.Item1,
                SquadTemplatesById = squadTemplates.Item2,
                SoldierTemplatesByFactionId = soldierTemplates,
                SpeciesByFactionId = species,
                TrainingProfilesById = trainingProfiles
            };
        }

        private Dictionary<int, TrainingProfile> GetTrainingProfilesById(
            IDbConnection connection,
            Dictionary<int, BaseSkill> baseSkillMap)
        {
            Dictionary<int, List<TrainingProfileEntry>> entryMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT TrainingProfileId, TargetType, TargetId, Weight FROM TrainingProfileEntry";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int trainingProfileId = reader.GetInt32(0);
                    TrainingTargetType targetType = (TrainingTargetType)reader.GetInt32(1);
                    int targetId = reader.GetInt32(2);
                    float weight = Convert.ToSingle(reader[3]);

                    TrainingProfileEntry entry = targetType == TrainingTargetType.Skill
                        ? new TrainingProfileEntry(
                            RulesDatabaseLookup.Require(
                                baseSkillMap,
                                targetId,
                                $"TrainingProfileEntry {trainingProfileId}.TargetId"),
                            weight)
                        : new TrainingProfileEntry((Models.Soldiers.Attribute)targetId, weight);

                    if (!entryMap.ContainsKey(trainingProfileId))
                    {
                        entryMap[trainingProfileId] = [];
                    }
                    entryMap[trainingProfileId].Add(entry);
                }
            }

            Dictionary<int, TrainingProfile> profileMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, Name FROM TrainingProfile";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader[1].ToString();
                    profileMap[id] = new TrainingProfile(id, name, entryMap.ContainsKey(id) ? entryMap[id] : []);
                }
            }
            return profileMap;
        }

        private Dictionary<int, List<ValueTuple<BaseSkill, float>>> GetSoldierMosTrainingBySoldierTemplateId(
            IDbConnection connection, Dictionary<int, BaseSkill> baseSkillMap)
        {
            Dictionary<int, List<ValueTuple<BaseSkill, float>>> soldierTemplateMosMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SoldierMosTraining";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int soldierTemplateId = reader.GetInt32(0);
                    int baseSkillId = reader.GetInt32(1);
                    float points = Convert.ToSingle(reader[2]);

                    BaseSkill baseSkill = RulesDatabaseLookup.Require(
                        baseSkillMap,
                        baseSkillId,
                        $"SoldierMosTraining {soldierTemplateId}.BaseSkillId");

                    ValueTuple<BaseSkill, float> training = new ValueTuple<BaseSkill, float>(baseSkill, points);

                    if (!soldierTemplateMosMap.ContainsKey(soldierTemplateId))
                    {
                        soldierTemplateMosMap[soldierTemplateId] = [];
                    }
                    soldierTemplateMosMap[soldierTemplateId].Add(training);
                }
            }
            return soldierTemplateMosMap;
        }

        private static Dictionary<int, List<SoldierTemplateRequirement>> GetSoldierTemplateRequirements(
            IDbConnection connection)
        {
            Dictionary<int, List<SoldierTemplateRequirement>> requirementMap = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SoldierTemplateId, RequirementType, RequirementKey, Comparison, RequiredValue "
                + "FROM SoldierTemplateRequirement";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierTemplateId = reader.GetInt32(0);
                SoldierTemplateRequirement requirement = new(
                    (SoldierTemplateRequirementType)reader.GetInt32(1),
                    reader.GetString(2),
                    (SoldierTemplateRequirementComparison)reader.GetInt32(3),
                    Convert.ToSingle(reader[4]));

                if (!requirementMap.ContainsKey(soldierTemplateId))
                {
                    requirementMap[soldierTemplateId] = [];
                }
                requirementMap[soldierTemplateId].Add(requirement);
            }
            return requirementMap;
        }


        private Dictionary<int, ArmorTemplate> GetArmorTemplates(IDbConnection connection)
        {
            Dictionary<int, ArmorTemplate> armorTemplateMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM ArmorTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    //int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    //int location = reader.GetInt32(3);
                    int armorProvided = reader.GetInt32(4);
                    int stealthMod = reader.GetInt32(5);
                    float capacityModifier = reader.FieldCount > 6 && reader[6].GetType() != typeof(DBNull)
                        ? Convert.ToSingle(reader[6])
                        : 0f;
                    bool preventsRunning = reader.FieldCount > 7 && reader[7].GetType() != typeof(DBNull)
                        && Convert.ToBoolean(reader[7]);
                    ArmorTemplate armorTemplate = new ArmorTemplate(id, name, (byte)armorProvided, 
                                                                    (short)stealthMod,
                                                                    capacityModifier,
                                                                    preventsRunning);
                    armorTemplateMap[id] = armorTemplate;
                }
            }
            return armorTemplateMap;
        }

        private Dictionary<int, MeleeWeaponTemplate> GetMeleeWeaponTemplates(
            IDbConnection connection,
            Dictionary<int, BaseSkill> baseSkillMap)
        {
            Dictionary<int, MeleeWeaponTemplate> factionWeaponTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, Name, Location, RelatedSkillId, Accuracy, ArmorMultiplier, WoundMultiplier, RequiredStrength, StrengthMultiplier, ParryModifier, AttackSpeedMultiplier FROM MeleeWeaponTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader[1].ToString();
                    int location = reader.GetInt32(2);
                    int baseSkillId = reader.GetInt32(3);
                    float accuracy = Convert.ToSingle(reader[4]);
                    float armorMultiplier = Convert.ToSingle(reader[5]);
                    float woundMultiplier = Convert.ToSingle(reader[6]);
                    float requiredStrength = Convert.ToSingle(reader[7]);
                    float strengthMultiplier = Convert.ToSingle(reader[8]);
                    float parryMod = Convert.ToSingle(reader[9]);
                    float attackSpeedMultiplier = Convert.ToSingle(reader[10]);

                    BaseSkill baseSkill = RulesDatabaseLookup.Require(
                        baseSkillMap,
                        baseSkillId,
                        $"MeleeWeaponTemplate {id}.RelatedSkillId");

                    MeleeWeaponTemplate weaponTemplate =
                        new MeleeWeaponTemplate(id, name, (EquipLocation)location, baseSkill,
                                                accuracy, armorMultiplier, woundMultiplier,
                                                requiredStrength, strengthMultiplier, parryMod,
                                                attackSpeedMultiplier);
                    factionWeaponTemplateMap[id] = weaponTemplate;
                }
            }
            return factionWeaponTemplateMap;
        }

        private Dictionary<int, AmmunitionType> GetAmmunitionTypes(IDbConnection connection)
        {
            Dictionary<int, AmmunitionType> ammunitionTypes = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, Name FROM AmmunitionType";
                try
                {
                    var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        ammunitionTypes[reader.GetInt32(0)] =
                            new AmmunitionType(reader.GetInt32(0), reader.GetString(1));
                    }
                }
                catch (DbException)
                {
                    // A small rules fixture may predate the itemized tables. Legacy weapon
                    // templates remain valid with null ammunition identities in that case.
                }
            }
            return ammunitionTypes;
        }

        private Dictionary<int, RangedWeaponTemplate> GetRangedWeaponTemplates(
            IDbConnection connection,
            Dictionary<int, BaseSkill> baseSkillMap,
            Dictionary<int, AmmunitionType> ammunitionTypes)
        {
            Dictionary<int, RangedWeaponTemplate> factionWeaponTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM RangedWeaponTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    string name = reader[1].ToString();
                    int location = reader.GetInt32(2);
                    int baseSkillId = reader.GetInt32(3);
                    float accuracy = Convert.ToSingle(reader[4]);
                    float armorMultiplier = Convert.ToSingle(reader[5]);
                    float woundMultiplier = Convert.ToSingle(reader[6]);
                    float requiredStrength = Convert.ToSingle(reader[7]);
                    float damageMultiplier = Convert.ToSingle(reader[8]);
                    float maxRange = Convert.ToSingle(reader[9]);
                    byte rof = reader.GetByte(10);
                    ushort ammo = (ushort)reader.GetInt16(11);
                    ushort recoil = (ushort)reader.GetInt16(12);
                    ushort bulk = (ushort)reader.GetInt16(13);
                    bool doesDamageDegrade = Convert.ToBoolean(reader[14]);
                    ushort reloadTime = (ushort)reader.GetInt16(15);
                    byte templateType = reader.GetByte(16);
                    float areaRadius = Convert.ToSingle(reader[17]);
                    AmmunitionType ammunitionType = ammunitionTypes.GetValueOrDefault(
                        EquipmentRulesCatalog.GetAmmunitionTypeId(id));
                    AmmunitionBehavior ammunitionBehavior = templateType == 3
                        ? AmmunitionBehavior.ConsumableItem
                        : IsBiologicalWeaponName(name)
                            ? AmmunitionBehavior.SelfRegenerating
                            : AmmunitionBehavior.Magazine;
                    AmmunitionConsumptionRule consumptionRule = templateType == 0
                        ? AmmunitionConsumptionRule.PerShot
                        : AmmunitionConsumptionRule.PerAttack;

                    BaseSkill baseSkill = RulesDatabaseLookup.Require(
                        baseSkillMap,
                        baseSkillId,
                        $"RangedWeaponTemplate {id}.RelatedSkillId");

                    RangedWeaponTemplate weaponTemplate =
                        new RangedWeaponTemplate(id, name, (EquipLocation)location, baseSkill,
                                                accuracy, armorMultiplier, woundMultiplier,
                                                requiredStrength, damageMultiplier, maxRange,
                                                rof, ammo, recoil, bulk, doesDamageDegrade, reloadTime,
                                                templateType, areaRadius, ammunitionType,
                                                ammunitionBehavior, consumptionRule, ammo,
                                                reloadTime, ammo);
                    factionWeaponTemplateMap[id] = weaponTemplate;
                }
            }
            return factionWeaponTemplateMap;
        }

        private static bool IsBiologicalWeaponName(string name)
        {
            string value = name?.ToLowerInvariant() ?? string.Empty;
            return value.Contains("devourer")
                || value.Contains("deathspitter")
                || value.Contains("spinefist")
                || value.Contains("fleshborer")
                || value.Contains("symbiote")
                || value.Contains("bio-");
        }

        private Dictionary<int, WeaponSet> GetWeaponSetMap(
            IDbConnection connection,
            Dictionary<int, MeleeWeaponTemplate> meleeWeaponMap,
            Dictionary<int, RangedWeaponTemplate> rangedWeaponMap)
        {
            RangedWeaponTemplate primaryRanged, secondaryRanged, grenade;
            MeleeWeaponTemplate primaryMelee, secondaryMelee;
            Dictionary<int, WeaponSet> weaponSetMap = [];

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM WeaponSet";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    //int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();

                    if (reader[3].GetType() != typeof(DBNull))
                    {
                        int weaponId = reader.GetInt32(3);
                        primaryRanged = RulesDatabaseLookup.Require(
                            rangedWeaponMap,
                            weaponId,
                            $"WeaponSet {id}.PrimaryRangedWeaponId");
                    }
                    else
                    {
                        primaryRanged = null;
                    }

                    if (reader[4].GetType() != typeof(DBNull))
                    {
                        int weaponId = reader.GetInt32(4);
                        secondaryRanged = RulesDatabaseLookup.Require(
                            rangedWeaponMap,
                            weaponId,
                            $"WeaponSet {id}.SecondaryRangedWeaponId");
                    }
                    else
                    {
                        secondaryRanged = null;
                    }

                    if (reader[5].GetType() != typeof(DBNull))
                    {
                        int weaponId = reader.GetInt32(5);
                        primaryMelee = RulesDatabaseLookup.Require(
                            meleeWeaponMap,
                            weaponId,
                            $"WeaponSet {id}.PrimaryMeleeWeaponId");
                    }
                    else
                    {
                        primaryMelee = null;
                    }

                    if (reader[6].GetType() != typeof(DBNull))
                    {
                        int weaponId = reader.GetInt32(6);
                        secondaryMelee = RulesDatabaseLookup.Require(
                            meleeWeaponMap,
                            weaponId,
                            $"WeaponSet {id}.SecondaryMeleeWeaponId");
                    }
                    else
                    {
                        secondaryMelee = null;
                    }

                    if (reader[7].GetType() != typeof(DBNull))
                    {
                        int weaponId = reader.GetInt32(7);
                        grenade = RulesDatabaseLookup.Require(
                            rangedWeaponMap,
                            weaponId,
                            $"WeaponSet {id}.GrenadeWeaponId");
                    }
                    else
                    {
                        grenade = null;
                    }

                    WeaponSet weaponSet = new WeaponSet(id, name, primaryRanged, secondaryRanged,
                                                        primaryMelee, secondaryMelee, grenade);
                    weaponSetMap[id] = weaponSet;
                }
            }
            return weaponSetMap;
        }

        /// <summary>
        /// Loads SquadTemplateElementQuota — how many bodies of an element may draw from one of
        /// its SoldierTemplate's weapon-menu groups (see SquadTemplateElement.Quotas). Read in
        /// table order with no ORDER BY, same as every other loader in this file, which matters
        /// here: SquadFactory's random draw walks a group's menu in this order, and the rules DB
        /// migration preserved the legacy SquadTemplateWeaponOption row order when it populated
        /// this table, so reading naturally keeps that draw deterministic across the schema change.
        /// </summary>
        private Dictionary<(int ElementId, string OptionGroup),
            (SquadQuotaModelBasis ModelBasis, int ModelsPerBlock, int SlotsPerBlock)>
            GetElementQuotaScalingByQuotaKey(IDbConnection connection)
        {
            Dictionary<(int ElementId, string OptionGroup),
                (SquadQuotaModelBasis ModelBasis, int ModelsPerBlock, int SlotsPerBlock)> result = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SquadTemplateElementId, OptionGroup, ModelBasis, ModelsPerBlock, SlotsPerBlock "
                + "FROM SquadTemplateElementQuotaScaling";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result[(reader.GetInt32(0), reader.GetString(1))] = (
                    (SquadQuotaModelBasis)reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4));
            }
            return result;
        }

        private Dictionary<int, List<SquadTemplateElementQuota>> GetElementQuotasByElementId(
            IDbConnection connection,
            IReadOnlyDictionary<(int ElementId, string OptionGroup),
                (SquadQuotaModelBasis ModelBasis, int ModelsPerBlock, int SlotsPerBlock)> quotaScaling)
        {
            Dictionary<int, List<SquadTemplateElementQuota>> quotaMap = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SquadTemplateElementId, OptionGroup, MinimumRequired, MaximumAllowed "
                + "FROM SquadTemplateElementQuota";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int elementId = reader.GetInt32(0);
                string optionGroup = reader.GetString(1);
                int min = reader.GetInt32(2);
                int max = reader.GetInt32(3);
                quotaScaling.TryGetValue((elementId, optionGroup), out var scaling);

                if (!quotaMap.TryGetValue(elementId, out List<SquadTemplateElementQuota> quotas))
                {
                    quotas = [];
                    quotaMap[elementId] = quotas;
                }
                quotas.Add(new SquadTemplateElementQuota(
                    optionGroup,
                    min,
                    max,
                    scaling.ModelBasis,
                    scaling.ModelsPerBlock,
                    scaling.SlotsPerBlock));
            }
            return quotaMap;
        }

        private Dictionary<int, List<SquadTemplateElement>> GetSquadTemplateElementsBySquadId(
            IDbConnection connection,
            Dictionary<int, SoldierTemplate> soldierTemplateMap,
            Dictionary<int, WeaponSet> weaponSetMap,
            IReadOnlyDictionary<int, ArmorTemplate> elementArmorMap,
            Dictionary<int, List<SquadTemplateElementQuota>> quotaMap,
            IReadOnlyDictionary<int, PersonalEquipmentRole> personalEquipmentRoles,
            IReadOnlyDictionary<int, int> personalRoleByElement,
            bool hasExplicitPersonalRoleBindings)
        {
            Dictionary<int, List<SquadTemplateElement>> elementsMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT Id, SquadTemplateId, SoldierTemplateId, MinimumRequired, MaximumAllowed, "
                    + "DefaultWeaponSetId, RollsStrength FROM SquadTemplateElement";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int squadTemplateId = reader.GetInt32(1);
                    int soldierTemplateId = reader.GetInt32(2);
                    int min = reader.GetInt32(3);
                    int max = reader.GetInt32(4);
                    WeaponSet defaultWeapons = reader[5].GetType() != typeof(DBNull)
                        ? RulesDatabaseLookup.Require(
                            weaponSetMap,
                            reader.GetInt32(5),
                            $"SquadTemplateElement {id}.DefaultWeaponSetId")
                        : null;
                    bool rollsStrength = reader[6].GetType() != typeof(DBNull)
                        && reader.GetInt32(6) != 0;
                    ArmorTemplate defaultArmor = elementArmorMap.GetValueOrDefault(id);

                    SoldierTemplate template = RulesDatabaseLookup.Require(
                        soldierTemplateMap,
                        soldierTemplateId,
                        $"SquadTemplateElement {id}.SoldierTemplateId");
                    IReadOnlyList<SquadTemplateElementQuota> quotas =
                        quotaMap.TryGetValue(id, out List<SquadTemplateElementQuota> list) ? list : [];
                    PersonalEquipmentRole personalRole = null;
                    if (hasExplicitPersonalRoleBindings
                        && personalRoleByElement.TryGetValue(id, out int personalRoleId))
                    {
                        if (!personalEquipmentRoles.TryGetValue(personalRoleId, out personalRole))
                        {
                            throw new InvalidOperationException(
                                $"Squad template element {id} references missing personal equipment role "
                                + $"{personalRoleId}.");
                        }
                    }
                    else if (!hasExplicitPersonalRoleBindings)
                    {
                        // Compatibility only for old test fixtures. Shipped rules use the
                        // explicit SquadTemplateElementEquipmentRole relation above.
                        SquadTemplateElementQuota personalQuota = quotas.FirstOrDefault(
                            quota => quota.OptionGroup == "Command Weapon");
                        WeaponSet personalDefault = defaultWeapons
                            ?? template.GetWeaponOptions("Command Weapon").FirstOrDefault();
                        personalRole = personalQuota != null && personalDefault != null
                            ? new PersonalEquipmentRole(
                                7_000_000 + id,
                                $"{template.Name} equipment",
                                EquipmentRulesCatalog.GetKitId(personalDefault.Id))
                            : null;
                    }

                    if (!elementsMap.ContainsKey(squadTemplateId))
                    {
                        elementsMap[squadTemplateId] = [];
                    }
                    elementsMap[squadTemplateId].Add(
                        new SquadTemplateElement(
                            template, (byte)min, (byte)max, id, defaultWeapons, quotas, rollsStrength,
                            personalRole, defaultArmor));
                }
            }
            return elementsMap;
        }

        private static Dictionary<int, ArmorTemplate> GetElementArmorsByElementId(
            IDbConnection connection,
            IReadOnlyDictionary<int, ArmorTemplate> armorTemplateMap)
        {
            Dictionary<int, ArmorTemplate> result = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SquadTemplateElementId, ArmorTemplateId "
                + "FROM SquadTemplateElementArmor ORDER BY SquadTemplateElementId";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int elementId = reader.GetInt32(0);
                int armorId = reader.GetInt32(1);
                result[elementId] = RulesDatabaseLookup.Require(
                    armorTemplateMap,
                    armorId,
                    $"SquadTemplateElementArmor {elementId}.ArmorTemplateId");
            }
            return result;
        }

        private static Dictionary<int, PersonalEquipmentRole> GetPersonalEquipmentRoles(
            IDbConnection connection)
        {
            Dictionary<int, PersonalEquipmentRole> roles = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT Id, Name, DefaultKitId, CapacityModifier "
                + "FROM PersonalEquipmentRole ORDER BY Id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                roles[id] = new PersonalEquipmentRole(
                    id,
                    reader.GetString(1),
                    reader.GetInt32(2),
                    Convert.ToSingle(reader.GetValue(3)));
            }
            return roles;
        }

        private static Dictionary<int, int> GetPersonalRoleBindingsByElementId(
            IDbConnection connection)
        {
            Dictionary<int, int> bindings = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SquadTemplateElementId, PersonalEquipmentRoleId "
                + "FROM SquadTemplateElementEquipmentRole ORDER BY SquadTemplateElementId";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                bindings[reader.GetInt32(0)] = reader.GetInt32(1);
            }
            return bindings;
        }

        private ValueTuple<Dictionary<int, List<SquadTemplate>>, Dictionary<int, SquadTemplate>> GetSquadTemplatesById(
            IDbConnection connection,
            Dictionary<int, List<SquadTemplateElement>> elementMap,
            Dictionary<int, WeaponSet> weaponSetMap,
            Dictionary<int, ArmorTemplate> armorTemplateMap,
            Dictionary<int, TrainingProfile> trainingProfileMap)
        {
            Dictionary<int, SquadTemplate> squadTemplateMap = [];
            Dictionary<int, List<SquadTemplate>> squadTemplatesByFactionId = [];
            Dictionary<int, int> bodyguardMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SquadTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    string name = reader[2].ToString();
                    int defaultArmorId = reader.GetInt32(3);
                    int defaultWeaponSetId = reader.GetInt32(4);
                    int squadType = reader.GetInt32(5);
                    // BattleValue is no longer stored on SquadTemplate — it is derived from the
                    // members' point values (PRD §4.24). BodyguardSquadTemplateId and the leader
                    // work-experience profile shift up one column with its removal.
                    if (reader[6].GetType() != typeof(DBNull))
                    {
                        bodyguardMap[id] = reader.GetInt32(6);
                    }

                    ArmorTemplate defaultArmor = RulesDatabaseLookup.Require(
                        armorTemplateMap,
                        defaultArmorId,
                        $"SquadTemplate {id}.DefaultArmorId");
                    // The squad-level weapon-option menu (SquadTemplateWeaponOption) is gone —
                    // it split into SoldierTemplate.WeaponOptionsByGroup (the menu) and
                    // SquadTemplateElement.Quotas (how many bodies may draw from it). SquadTemplate
                    // still carries the field for callers that have not migrated (see
                    // Builders/SquadFactory.cs), but there is nothing left to populate it with.
                    SquadTemplate squadTemplate = new SquadTemplate(id,
                                                                    name,
                                                                    RulesDatabaseLookup.Require(
                                                                        weaponSetMap,
                                                                        defaultWeaponSetId,
                                                                        $"SquadTemplate {id}.DefaultWeaponSetId"),
                                                                    null,
                                                                    defaultArmor,
                                                                    RulesDatabaseLookup.Require(
                                                                        elementMap,
                                                                        id,
                                                                        $"SquadTemplate {id}.Elements"),
                                                                    (SquadTypes)squadType,
                                                                    reader.FieldCount > 8
                                                                        && reader[8].GetType() != typeof(DBNull)
                                                                        ? (FormationMobilityPolicy)reader.GetInt32(8)
                                                                        : null);
                    if (reader.FieldCount > 8 && reader[8].GetType() != typeof(DBNull)
                        && !Enum.IsDefined(typeof(FormationMobilityPolicy), reader.GetInt32(8)))
                    {
                        throw new InvalidOperationException(
                            $"Squad template {id} has an invalid formation mobility policy.");
                    }
                    if (reader.FieldCount > 7 && reader[7].GetType() != typeof(DBNull))
                    {
                        int profileId = reader.GetInt32(7);
                        squadTemplate.LeaderWorkExperienceProfile = RulesDatabaseLookup.Require(
                            trainingProfileMap,
                            profileId,
                            $"SquadTemplate {id}.LeaderWorkExperienceProfileId");
                    }
                    squadTemplateMap[id] = squadTemplate;
                    if (!squadTemplatesByFactionId.ContainsKey(factionId))
                    {
                        squadTemplatesByFactionId[factionId] = [];
                    }
                    squadTemplatesByFactionId[factionId].Add(squadTemplate);
                }
                foreach (KeyValuePair<int, int> kvp in bodyguardMap)
                {
                    SquadTemplate owner = RulesDatabaseLookup.Require(
                        squadTemplateMap,
                        kvp.Key,
                        "SquadTemplate.BodyguardSquadTemplateId owner");
                    owner.BodyguardSquadTemplate = RulesDatabaseLookup.Require(
                        squadTemplateMap,
                        kvp.Value,
                        $"SquadTemplate {kvp.Key}.BodyguardSquadTemplateId");
                }
            }
            return new ValueTuple<Dictionary<int, List<SquadTemplate>>, Dictionary<int, SquadTemplate>>(squadTemplatesByFactionId, squadTemplateMap);
        }

        private Dictionary<int, NormalizedValueTemplate> GetAttributeTemplates(IDbConnection connection)
        {
            Dictionary<int, NormalizedValueTemplate> attributeTemplateMap =
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM AttributeTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    float baseValue = Convert.ToSingle(reader[1]);
                    float stdDev = Convert.ToSingle(reader[2]);
                    NormalizedValueTemplate attributeTemplate = new NormalizedValueTemplate
                    {
                        BaseValue = baseValue,
                        StandardDeviation = stdDev
                    };

                    attributeTemplateMap[id] = attributeTemplate;
                }
            }
            return attributeTemplateMap;
        }

        private Dictionary<int, List<Species>> GetSpeciesByFactionId(IDbConnection connection,
                                                                     Dictionary<int, NormalizedValueTemplate> attributeMap,
                                                                     Dictionary<int, List<HitLocationTemplate>> hitLocationTemplateMap,
                                                                     Dictionary<int, MeleeWeaponTemplate> meleeWeaponTemplateMap)
        {
            Dictionary<int, List<Species>> speciesMap = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Species";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    int bodyId = reader.GetInt32(2);
                    string name = reader[3].ToString();
                    int strengthTemplateId = reader.GetInt32(4);
                    int dexterityTemplateId = reader.GetInt32(5);
                    int constitutionTemplateId = reader.GetInt32(6);
                    int intelligenceTemplateId = reader.GetInt32(7);
                    int perceptionTemplateId = reader.GetInt32(8);
                    int egoTemplateId = reader.GetInt32(9);
                    int charismaTemplateId = reader.GetInt32(10);
                    int psychicTemplateId = reader.GetInt32(11);
                    int attackSpeedTemplateId = reader.GetInt32(12);
                    int moveSpeedTemplateId = reader.GetInt32(13);
                    int sizeTemplateId = reader.GetInt32(14);
                    ushort width = (ushort)reader.GetInt16(15);
                    ushort depth = (ushort)reader.GetInt16(16);
                    // Columns appended by the migrate-evasion pass (see
                    // OnlyWar_TDD.md §6.6). Read positionally at the
                    // end of the row, after Width/Depth.
                    float meleeEvasion = (float)reader.GetDouble(17);
                    float rangedEvasion = (float)reader.GetDouble(18);
                    SpeciesAbilities abilities = (SpeciesAbilities)reader.GetInt32(19);
                    if (reader.FieldCount <= 20 || reader[20].GetType() == typeof(DBNull))
                    {
                        throw new InvalidOperationException(
                            $"Species {id} ('{name}') does not define a default unarmed weapon template.");
                    }
                    int defaultUnarmedWeaponTemplateId = reader.GetInt32(20);
                    // Column appended by the synapse-data pass (OnlyWar_TDD.md §6.6
                    // §4.1), after DefaultUnarmedWeaponTemplateId. Older schemas without it
                    // default to no synapse radius.
                    float synapseRadius = reader.FieldCount > 21 && reader[21].GetType() != typeof(DBNull)
                        ? (float)reader.GetDouble(21)
                        : 0f;
                    float baseCapacity = reader.FieldCount > 22 && reader[22].GetType() != typeof(DBNull)
                        ? Convert.ToSingle(reader[22])
                        : 16f;
                    MeleeWeaponTemplate defaultUnarmedWeapon = ResolveDefaultUnarmedWeapon(
                        id, name, defaultUnarmedWeaponTemplateId, meleeWeaponTemplateMap);
                    Species species = new Species(id, name,
                                                  RulesDatabaseLookup.Require(attributeMap, strengthTemplateId, $"Species {id}.StrengthTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, dexterityTemplateId, $"Species {id}.DexterityTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, constitutionTemplateId, $"Species {id}.ConstitutionTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, intelligenceTemplateId, $"Species {id}.IntelligenceTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, perceptionTemplateId, $"Species {id}.PerceptionTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, egoTemplateId, $"Species {id}.EgoTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, charismaTemplateId, $"Species {id}.CharismaTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, psychicTemplateId, $"Species {id}.PsychicPowerTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, attackSpeedTemplateId, $"Species {id}.AttackSpeedTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, moveSpeedTemplateId, $"Species {id}.MoveSpeedTemplateId"),
                                                  RulesDatabaseLookup.Require(attributeMap, sizeTemplateId, $"Species {id}.SizeTemplateId"),
                                                  width,
                                                  depth,
                                                  meleeEvasion,
                                                  rangedEvasion,
                                                  abilities,
                                                  new BodyTemplate(RulesDatabaseLookup.Require(
                                                      hitLocationTemplateMap,
                                                      bodyId,
                                                      $"Species {id}.BodyId")),
                                                  defaultUnarmedWeapon,
                                                  synapseRadius,
                                                  baseCapacity);

                    if (!speciesMap.ContainsKey(factionId))
                    {
                        speciesMap[factionId] = [];
                    }
                    speciesMap[factionId].Add(species);
                }
            }
            return speciesMap;
        }

        internal static MeleeWeaponTemplate ResolveDefaultUnarmedWeapon(
            int speciesId,
            string speciesName,
            int defaultUnarmedWeaponTemplateId,
            IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeaponTemplateMap)
        {
            if (meleeWeaponTemplateMap == null
                || !meleeWeaponTemplateMap.TryGetValue(
                    defaultUnarmedWeaponTemplateId, out MeleeWeaponTemplate weaponTemplate))
            {
                throw new InvalidOperationException(
                    $"Species {speciesId} ('{speciesName}') references default unarmed weapon template "
                    + $"{defaultUnarmedWeaponTemplateId}, which is not in the rules database.");
            }

            return weaponTemplate;
        }

        /// <summary>
        /// Reads the per-role weapon-set menus (Models/Soldiers/SoldierTemplate.WeaponOptionsByGroup),
        /// grouped by OptionGroup. The default no longer lives here — it moved to
        /// SquadTemplateElement.DefaultWeaponSetId, since the same role can default differently
        /// depending on which squad's element it fills.
        /// </summary>
        private static Dictionary<int, Dictionary<string, List<WeaponSet>>>
            GetWeaponOptionsBySoldierTemplateId(
                IDbConnection connection,
                Dictionary<int, WeaponSet> weaponSetMap)
        {
            Dictionary<int, Dictionary<string, List<WeaponSet>>> optionMap = [];
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SoldierTemplateId, OptionGroup, WeaponSetId FROM SoldierTemplateWeaponOption";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierTemplateId = reader.GetInt32(0);
                string optionGroup = reader.GetString(1);
                int weaponSetId = reader.GetInt32(2);
                WeaponSet weaponSet = RulesDatabaseLookup.Require(
                    weaponSetMap,
                    weaponSetId,
                    $"SoldierTemplateWeaponOption {reader.GetInt32(0)}.WeaponSetId");

                if (!optionMap.TryGetValue(soldierTemplateId, out Dictionary<string, List<WeaponSet>> groups))
                {
                    groups = [];
                    optionMap[soldierTemplateId] = groups;
                }
                if (!groups.TryGetValue(optionGroup, out List<WeaponSet> options))
                {
                    options = [];
                    groups[optionGroup] = options;
                }
                options.Add(weaponSet);
            }
            return optionMap;
        }

        private Dictionary<int, List<SoldierTemplate>> GetSoldierTemplatesByFactionId(
            IDbConnection connection,
            Dictionary<int, List<ValueTuple<BaseSkill, float>>> soldierTemplateTrainingMap,
            Dictionary<int, List<Species>> speciesMap,
            Dictionary<int, TrainingProfile> trainingProfileMap,
            Dictionary<int, List<SoldierTemplateRequirement>> requirementMap,
            Dictionary<int, Dictionary<string, List<WeaponSet>>> weaponOptionMap)
        {
            Dictionary<int, List<SoldierTemplate>> soldierTemplatesByFactionId = 
                [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM SoldierTemplate";
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int factionId = reader.GetInt32(1);
                    int speciesId = reader.GetInt32(2);
                    string name = reader[3].ToString();
                    int rank = reader.GetInt32(4);
                    int subrank = reader.GetInt32(5);
                    bool isSquadLeader = Convert.ToBoolean(reader[6]);
                    int specialistType = reader.GetInt32(7);
                    TrainingProfile workExperienceTrainingProfile = null;
                    if (reader.FieldCount > 8 && reader[8].GetType() != typeof(DBNull))
                    {
                        int profileId = reader.GetInt32(8);
                        workExperienceTrainingProfile = RulesDatabaseLookup.Require(
                            trainingProfileMap,
                            profileId,
                            $"SoldierTemplate {id}.WorkExperienceTrainingProfileId");
                    }
                    // BattleValue was appended after WorkExperienceTrainingProfileId; rows without a
                    // populated value (e.g. player soldiers, pending a later pass) default to 0.
                    int battleValue = reader.FieldCount > 9 && reader[9].GetType() != typeof(DBNull)
                        ? reader.GetInt32(9)
                        : 0;
                    float? meleeFraction = reader.FieldCount > 10 && reader[10].GetType() != typeof(DBNull)
                        ? Convert.ToSingle(reader[10])
                        : null;
                    List<ValueTuple<BaseSkill, float>> trainingList = null;
                    if (soldierTemplateTrainingMap.ContainsKey(id))
                    {
                        trainingList = soldierTemplateTrainingMap[id];
                    }
                    List<Species> factionSpecies = RulesDatabaseLookup.Require(
                        speciesMap,
                        factionId,
                        $"SoldierTemplate {id}.FactionId");
                    Species species = factionSpecies.FirstOrDefault(s => s.Id == speciesId);
                    if (species == null)
                    {
                        throw new InvalidOperationException(
                            $"Rules database SoldierTemplate {id} references missing Species "
                            + $"{speciesId} for faction {factionId}.");
                    }
                    List<SoldierTemplateRequirement> promotionRequirements =
                        requirementMap.TryGetValue(id, out List<SoldierTemplateRequirement> requirements)
                            ? requirements
                            : [];
                    IReadOnlyDictionary<string, IReadOnlyList<WeaponSet>> weaponOptionsByGroup = null;
                    if (weaponOptionMap.TryGetValue(id, out Dictionary<string, List<WeaponSet>> groups))
                    {
                        weaponOptionsByGroup = groups.ToDictionary(
                            kv => kv.Key, kv => (IReadOnlyList<WeaponSet>)kv.Value);
                    }
                    SoldierTemplate soldierTemplate =
                        new SoldierTemplate(id, species, name, (byte)rank, (byte)subrank,
                                             isSquadLeader, (byte)specialistType, trainingList,
                                             workExperienceTrainingProfile, battleValue,
                                             promotionRequirements, meleeFraction,
                                             weaponOptionsByGroup);

                    if (!soldierTemplatesByFactionId.ContainsKey(factionId))
                    {
                        soldierTemplatesByFactionId[factionId] = [];
                    }
                    soldierTemplatesByFactionId[factionId].Add(soldierTemplate);
                }
            }
            return soldierTemplatesByFactionId;
        }

    }
}
