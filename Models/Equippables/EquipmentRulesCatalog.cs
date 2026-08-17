using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using System.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OnlyWar.Models.Equippables
{
    /// <summary>
    /// Runtime registry for the itemized rules vocabulary. The legacy loader uses this conversion
    /// while the shipped database is being rebuilt; all consumers of the new foundation receive
    /// globally unique equipment and kit ids from this registry.
    /// </summary>
    public sealed class EquipmentRulesCatalog
    {
        public IReadOnlyDictionary<int, EquipmentTemplate> EquipmentTemplates { get; }
        public IReadOnlyDictionary<int, AmmunitionType> AmmunitionTypes { get; }
        public IReadOnlyDictionary<int, EquipmentKitTemplate> EquipmentKits { get; }
        public IReadOnlyDictionary<int, PersonalEquipmentRole> PersonalEquipmentRoles { get; }

        private EquipmentRulesCatalog(
            IDictionary<int, EquipmentTemplate> equipmentTemplates,
            IDictionary<int, AmmunitionType> ammunitionTypes,
            IDictionary<int, EquipmentKitTemplate> equipmentKits,
            IDictionary<int, PersonalEquipmentRole> personalEquipmentRoles)
        {
            EquipmentTemplates = new ReadOnlyDictionary<int, EquipmentTemplate>(equipmentTemplates);
            AmmunitionTypes = new ReadOnlyDictionary<int, AmmunitionType>(ammunitionTypes);
            EquipmentKits = new ReadOnlyDictionary<int, EquipmentKitTemplate>(equipmentKits);
            PersonalEquipmentRoles = new ReadOnlyDictionary<int, PersonalEquipmentRole>(personalEquipmentRoles);
        }

        public static EquipmentRulesCatalog FromLegacyRules(
            IReadOnlyDictionary<int, RangedWeaponTemplate> rangedWeapons,
            IReadOnlyDictionary<int, MeleeWeaponTemplate> meleeWeapons,
            IReadOnlyDictionary<int, ArmorTemplate> armorTemplates,
            IReadOnlyDictionary<int, WeaponSet> weaponSets,
            IReadOnlyCollection<SquadTemplate> squadTemplates = null)
        {
            Dictionary<int, AmmunitionType> ammunitionTypes = [];
            Dictionary<int, EquipmentTemplate> equipmentTemplates = [];

            foreach (RangedWeaponTemplate template in rangedWeapons?.Values ?? Array.Empty<RangedWeaponTemplate>())
            {
                int equipmentId = GetRangedEquipmentId(template.Id);
                AmmunitionType ammunitionType = null;
                if (template.AmmunitionBehavior is not AmmunitionBehavior.Unlimited
                    and not AmmunitionBehavior.SelfRegenerating
                    and not AmmunitionBehavior.ConsumableItem)
                {
                    int ammunitionId = GetAmmunitionTypeId(template.Id);
                    ammunitionType = ammunitionTypes[ammunitionId] = new AmmunitionType(
                        ammunitionId,
                        $"{template.Name} ammunition");
                }

                AmmunitionBehavior behavior = template.AmmunitionBehavior;
                EquipmentTags tags = EquipmentTags.Weapon | EquipmentTags.Ranged;
                if (template.Location == EquipLocation.TwoHand) tags |= EquipmentTags.TwoHanded;
                if (template.TemplateType == 3)
                {
                    behavior = AmmunitionBehavior.ConsumableItem;
                    ammunitionType = null;
                    tags |= EquipmentTags.Consumable;
                }
                if (IsBiological(template.Name))
                {
                    behavior = AmmunitionBehavior.SelfRegenerating;
                    ammunitionType = null;
                    tags |= EquipmentTags.Biological;
                }

                RangedWeaponProfile profile = new(
                    template.RelatedSkill,
                    template.Accuracy,
                    template.ArmorMultiplier,
                    template.WoundMultiplier,
                    template.RequiredStrength,
                    template.DamageMultiplier,
                    template.MaximumRange,
                    template.RateOfFire,
                    template.AmmoCapacity,
                    template.Recoil,
                    template.Bulk,
                    template.DoesDamageDegradeWithRange,
                    template.Location,
                    ammunitionType,
                    behavior,
                    template.ConsumptionRule,
                    template.ReloadTime,
                    template.ReloadAmount,
                    template.RecoveryDuration,
                    template.RecoveryAmount,
                    template.TemplateType,
                    template.AreaRadius);
                equipmentTemplates[equipmentId] = new EquipmentTemplate(
                    equipmentId,
                    template.Name,
                    carryCost: template.Location == EquipLocation.TwoHand ? 2 : 1,
                    tags: tags,
                    rangedProfile: profile);
            }

            foreach (MeleeWeaponTemplate template in meleeWeapons?.Values ?? Array.Empty<MeleeWeaponTemplate>())
            {
                EquipmentTags tags = EquipmentTags.Weapon | EquipmentTags.Melee;
                if (template.Location == EquipLocation.TwoHand) tags |= EquipmentTags.TwoHanded;
                equipmentTemplates[GetMeleeEquipmentId(template.Id)] = new EquipmentTemplate(
                    GetMeleeEquipmentId(template.Id),
                    template.Name,
                    carryCost: template.Location == EquipLocation.TwoHand ? 2 : 1,
                    tags: tags,
                    meleeProfile: new MeleeWeaponProfile(
                        template.RelatedSkill,
                        template.Accuracy,
                        template.ArmorMultiplier,
                        template.WoundMultiplier,
                        template.RequiredStrength,
                        template.StrengthMultiplier,
                        template.ParryModifier,
                        template.AttackSpeedMultiplier,
                        template.Location));
            }

            foreach (ArmorTemplate template in armorTemplates?.Values ?? Array.Empty<ArmorTemplate>())
            {
                equipmentTemplates[GetArmorEquipmentId(template.Id)] = new EquipmentTemplate(
                    GetArmorEquipmentId(template.Id),
                    template.Name,
                    carryCost: 0,
                    tags: EquipmentTags.Armor,
                    armorProfile: new ArmorProfile(
                        template.ArmorProvided,
                        template.StealthModifier,
                        template.CapacityModifier));
            }

            foreach (AmmunitionType ammunitionType in ammunitionTypes.Values)
            {
                equipmentTemplates[GetAmmunitionPackageId(ammunitionType.Id)] = new EquipmentTemplate(
                    GetAmmunitionPackageId(ammunitionType.Id),
                    $"{ammunitionType.Name} package",
                    carryCost: 0.25f,
                    tags: EquipmentTags.Ammunition | EquipmentTags.AmmunitionCarrier,
                    ammunitionProfile: new AmmunitionPackageProfile(
                        ammunitionType,
                        rangedWeapons.Values.First(template => GetAmmunitionTypeId(template.Id) == ammunitionType.Id).AmmoCapacity));
            }

            Dictionary<int, EquipmentKitTemplate> kits = [];
            foreach (WeaponSet set in weaponSets?.Values ?? Array.Empty<WeaponSet>())
            {
                List<EquipmentKitEntry> entries = [];
                AddRanged(entries, set.PrimaryRangedWeapon, equipmentTemplates);
                AddRanged(entries, set.SecondaryRangedWeapon, equipmentTemplates);
                AddMelee(entries, set.PrimaryMeleeWeapon, equipmentTemplates);
                AddMelee(entries, set.SecondaryMeleeWeapon, equipmentTemplates);
                AddRanged(entries, set.GrenadeWeapon, equipmentTemplates, isGrenade: true);
                foreach (EquipmentKitEntry weaponEntry in entries.ToList())
                {
                    RangedWeaponProfile profile = weaponEntry.Equipment.RangedProfile;
                    if (profile?.AmmunitionType == null
                        || profile.AmmunitionBehavior is AmmunitionBehavior.SelfRegenerating
                            or AmmunitionBehavior.Unlimited
                            or AmmunitionBehavior.ConsumableItem)
                    {
                        continue;
                    }
                    EquipmentTemplate package = equipmentTemplates[GetAmmunitionPackageId(profile.AmmunitionType.Id)];
                    entries.Add(new EquipmentKitEntry(package, 1));
                }

                kits[GetKitId(set.Id)] = new EquipmentKitTemplate(
                    GetKitId(set.Id),
                    set.Name,
                    items: entries);
            }

            Dictionary<int, PersonalEquipmentRole> roles = [];
            foreach (SquadTemplateElement element in (squadTemplates ?? Array.Empty<SquadTemplate>())
                .SelectMany(template => template.Elements)
                .Where(element => element.PersonalEquipmentRole != null))
            {
                PersonalEquipmentRole role = element.PersonalEquipmentRole;
                if (!kits.ContainsKey(role.DefaultKitId))
                {
                    // Authored role ids are already rules-global; this branch only guards a
                    // partially migrated test fixture and keeps the catalog diagnostic useful.
                    continue;
                }
                roles[role.Id] = role;
            }
            return new EquipmentRulesCatalog(equipmentTemplates, ammunitionTypes, kits, roles);
        }

        /// <summary>
        /// Loads the itemized catalog from the named rules tables. The compatibility catalog is
        /// used only to supply rows absent from a small legacy fixture; rows with the same global
        /// id are always replaced by the data-driven definition. This keeps production rules data
        /// authoritative without making old focused test databases pretend to have the new schema.
        /// </summary>
        public static EquipmentRulesCatalog FromDatabase(
            IDbConnection connection,
            IReadOnlyDictionary<int, BaseSkill> baseSkills,
            EquipmentRulesCatalog compatibilityCatalog = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            Dictionary<int, AmmunitionType> ammunitionTypes = ReadAmmunitionTypes(connection);
            Dictionary<int, EquipmentMetadata> metadata = ReadEquipmentMetadata(connection);
            Dictionary<int, RangedWeaponProfile> rangedProfiles = ReadRangedProfiles(
                connection, baseSkills, ammunitionTypes);
            Dictionary<int, MeleeWeaponProfile> meleeProfiles = ReadMeleeProfiles(
                connection, baseSkills);
            Dictionary<int, ArmorProfile> armorProfiles = ReadArmorProfiles(connection);
            Dictionary<int, AmmunitionPackageProfile> ammunitionProfiles = ReadAmmunitionPackages(
                connection, ammunitionTypes);
            Dictionary<int, GearProfile> gearProfiles = ReadGearProfiles(connection);
            Dictionary<int, List<EquipmentRequirement>> requirements = ReadRequirements(connection);

            Dictionary<int, EquipmentTemplate> equipmentTemplates = [];
            foreach ((int id, EquipmentMetadata item) in metadata)
            {
                equipmentTemplates[id] = new EquipmentTemplate(
                    id,
                    item.Name,
                    item.CarryCost,
                    item.MaximumQuantity,
                    item.Tags,
                    rangedProfiles.GetValueOrDefault(id),
                    meleeProfiles.GetValueOrDefault(id),
                    armorProfiles.GetValueOrDefault(id),
                    ammunitionProfiles.GetValueOrDefault(id),
                    gearProfiles.GetValueOrDefault(id),
                    requirements.GetValueOrDefault(id));
            }

            ValidateProfileOwnership(equipmentTemplates, rangedProfiles.Keys, "ranged");
            ValidateProfileOwnership(equipmentTemplates, meleeProfiles.Keys, "melee");
            ValidateProfileOwnership(equipmentTemplates, armorProfiles.Keys, "armor");
            ValidateProfileOwnership(equipmentTemplates, ammunitionProfiles.Keys, "ammunition");
            ValidateProfileOwnership(equipmentTemplates, gearProfiles.Keys, "gear");

            Dictionary<int, EquipmentKitTemplate> kits = ReadKits(connection, equipmentTemplates);
            Dictionary<int, PersonalEquipmentRole> roles = ReadPersonalEquipmentRoles(connection, kits);

            if (compatibilityCatalog != null)
            {
                foreach ((int id, EquipmentTemplate equipment) in compatibilityCatalog.EquipmentTemplates)
                {
                    equipmentTemplates.TryAdd(id, equipment);
                }
                foreach ((int id, AmmunitionType ammunitionType) in compatibilityCatalog.AmmunitionTypes)
                {
                    ammunitionTypes.TryAdd(id, ammunitionType);
                }
                foreach ((int id, EquipmentKitTemplate kit) in compatibilityCatalog.EquipmentKits)
                {
                    kits.TryAdd(id, kit);
                }
                foreach ((int id, PersonalEquipmentRole role) in compatibilityCatalog.PersonalEquipmentRoles)
                {
                    roles.TryAdd(id, role);
                }
            }

            return new EquipmentRulesCatalog(equipmentTemplates, ammunitionTypes, kits, roles);
        }

        private sealed record EquipmentMetadata(
            int Id,
            string Name,
            float CarryCost,
            int MaximumQuantity,
            EquipmentTags Tags);

        private static Dictionary<int, AmmunitionType> ReadAmmunitionTypes(IDbConnection connection)
        {
            Dictionary<int, AmmunitionType> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT Id, Name FROM AmmunitionType ORDER BY Id");
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                result[id] = new AmmunitionType(id, reader.GetString(1));
            }
            return result;
        }

        private static Dictionary<int, EquipmentMetadata> ReadEquipmentMetadata(IDbConnection connection)
        {
            Dictionary<int, EquipmentMetadata> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT Id, Name, CarryCost, MaximumQuantity, Tags FROM EquipmentTemplate ORDER BY Id");
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                result[id] = new EquipmentMetadata(
                    id,
                    reader.GetString(1),
                    Convert.ToSingle(reader.GetValue(2)),
                    reader.GetInt32(3),
                    (EquipmentTags)reader.GetInt32(4));
            }
            return result;
        }

        private static Dictionary<int, RangedWeaponProfile> ReadRangedProfiles(
            IDbConnection connection,
            IReadOnlyDictionary<int, BaseSkill> baseSkills,
            IReadOnlyDictionary<int, AmmunitionType> ammunitionTypes)
        {
            Dictionary<int, RangedWeaponProfile> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT EquipmentId, RelatedSkillId, Accuracy, ArmorMultiplier, WoundMultiplier, "
                + "RequiredStrength, DamageMultiplier, MaximumRange, RateOfFire, LoadedCapacity, "
                + "Recoil, Bulk, DoesDamageDegradeWithRange, Location, AmmunitionTypeId, "
                + "AmmunitionBehavior, ConsumptionRule, ReloadDuration, ReloadAmount, "
                + "RecoveryDuration, RecoveryAmount, TemplateType, AreaRadius "
                + "FROM EquipmentRangedProfile ORDER BY EquipmentId");
            while (reader.Read())
            {
                int equipmentId = reader.GetInt32(0);
                result[equipmentId] = new RangedWeaponProfile(
                    ResolveSkill(baseSkills, reader.GetInt32(1), equipmentId),
                    Convert.ToSingle(reader.GetValue(2)),
                    Convert.ToSingle(reader.GetValue(3)),
                    Convert.ToSingle(reader.GetValue(4)),
                    Convert.ToSingle(reader.GetValue(5)),
                    Convert.ToSingle(reader.GetValue(6)),
                    Convert.ToSingle(reader.GetValue(7)),
                    checked((byte)reader.GetInt32(8)),
                    checked((ushort)reader.GetInt32(9)),
                    checked((ushort)reader.GetInt32(10)),
                    checked((ushort)reader.GetInt32(11)),
                    Convert.ToBoolean(reader.GetValue(12)),
                    (EquipLocation)reader.GetInt32(13),
                    reader.IsDBNull(14) ? null : ResolveAmmunition(
                        ammunitionTypes, reader.GetInt32(14), equipmentId),
                    (AmmunitionBehavior)reader.GetInt32(15),
                    (AmmunitionConsumptionRule)reader.GetInt32(16),
                    checked((ushort)reader.GetInt32(17)),
                    checked((ushort)reader.GetInt32(18)),
                    checked((ushort)reader.GetInt32(19)),
                    checked((ushort)reader.GetInt32(20)),
                    checked((byte)reader.GetInt32(21)),
                    Convert.ToSingle(reader.GetValue(22)));
            }
            return result;
        }

        private static Dictionary<int, MeleeWeaponProfile> ReadMeleeProfiles(
            IDbConnection connection,
            IReadOnlyDictionary<int, BaseSkill> baseSkills)
        {
            Dictionary<int, MeleeWeaponProfile> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT EquipmentId, RelatedSkillId, Accuracy, ArmorMultiplier, WoundMultiplier, "
                + "RequiredStrength, StrengthMultiplier, ParryModifier, AttackSpeedMultiplier, Location "
                + "FROM EquipmentMeleeProfile ORDER BY EquipmentId");
            while (reader.Read())
            {
                int equipmentId = reader.GetInt32(0);
                result[equipmentId] = new MeleeWeaponProfile(
                    ResolveSkill(baseSkills, reader.GetInt32(1), equipmentId),
                    Convert.ToSingle(reader.GetValue(2)),
                    Convert.ToSingle(reader.GetValue(3)),
                    Convert.ToSingle(reader.GetValue(4)),
                    Convert.ToSingle(reader.GetValue(5)),
                    Convert.ToSingle(reader.GetValue(6)),
                    Convert.ToSingle(reader.GetValue(7)),
                    Convert.ToSingle(reader.GetValue(8)),
                    (EquipLocation)reader.GetInt32(9));
            }
            return result;
        }

        private static Dictionary<int, ArmorProfile> ReadArmorProfiles(IDbConnection connection)
        {
            Dictionary<int, ArmorProfile> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT EquipmentId, ArmorProvided, StealthModifier, CapacityModifier "
                + "FROM EquipmentArmorProfile ORDER BY EquipmentId");
            while (reader.Read())
            {
                result[reader.GetInt32(0)] = new ArmorProfile(
                    checked((byte)reader.GetInt32(1)),
                    checked((short)reader.GetInt32(2)),
                    Convert.ToSingle(reader.GetValue(3)));
            }
            return result;
        }

        private static Dictionary<int, AmmunitionPackageProfile> ReadAmmunitionPackages(
            IDbConnection connection,
            IReadOnlyDictionary<int, AmmunitionType> ammunitionTypes)
        {
            Dictionary<int, AmmunitionPackageProfile> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT EquipmentId, AmmunitionTypeId, RoundsPerPackage "
                + "FROM EquipmentAmmunitionPackage ORDER BY EquipmentId");
            while (reader.Read())
            {
                int equipmentId = reader.GetInt32(0);
                result[equipmentId] = new AmmunitionPackageProfile(
                    ResolveAmmunition(ammunitionTypes, reader.GetInt32(1), equipmentId),
                    reader.GetInt32(2));
            }
            return result;
        }

        private static Dictionary<int, GearProfile> ReadGearProfiles(IDbConnection connection)
        {
            Dictionary<int, GearProfile> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT EquipmentId, CapacityBonus FROM EquipmentGearProfile ORDER BY EquipmentId");
            while (reader.Read())
            {
                result[reader.GetInt32(0)] = new GearProfile(Convert.ToSingle(reader.GetValue(1)));
            }
            return result;
        }

        private static Dictionary<int, List<EquipmentRequirement>> ReadRequirements(IDbConnection connection)
        {
            Dictionary<int, List<EquipmentRequirement>> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT EquipmentId, RequirementKind, AllowedIds, MinimumValue, SkillName, "
                + "EquipmentTag, MaximumDuplicates FROM EquipmentRequirement ORDER BY EquipmentId");
            while (reader.Read())
            {
                int equipmentId = reader.GetInt32(0);
                EquipmentRequirementKind kind = (EquipmentRequirementKind)reader.GetInt32(1);
                IReadOnlyList<int> allowedIds = reader.IsDBNull(2)
                    ? Array.Empty<int>()
                    : ParseIds(reader.GetString(2));
                EquipmentRequirement requirement = new(
                    kind,
                    allowedIds,
                    reader.IsDBNull(3) ? 0 : Convert.ToSingle(reader.GetValue(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? EquipmentTags.None : (EquipmentTags)reader.GetInt32(5),
                    reader.IsDBNull(6) ? 0 : reader.GetInt32(6));
                if (!result.TryGetValue(equipmentId, out List<EquipmentRequirement> requirements))
                {
                    requirements = [];
                    result[equipmentId] = requirements;
                }
                requirements.Add(requirement);
            }
            return result;
        }

        private static Dictionary<int, EquipmentKitTemplate> ReadKits(
            IDbConnection connection,
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates)
        {
            Dictionary<int, (string Name, int? ArmorId)> metadata = [];
            using (IDataReader reader = ExecuteReader(connection,
                "SELECT Id, Name, ArmorEquipmentId FROM EquipmentKitTemplate ORDER BY Id"))
            {
                while (reader.Read())
                {
                    metadata[reader.GetInt32(0)] = (
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetInt32(2));
                }
            }

            Dictionary<int, List<EquipmentKitEntry>> entries = [];
            using (IDataReader reader = ExecuteReader(connection,
                "SELECT KitId, EquipmentId, Quantity, InitialReadyOrder "
                + "FROM EquipmentKitItem ORDER BY KitId, EquipmentId, InitialReadyOrder"))
            {
                while (reader.Read())
                {
                    int kitId = reader.GetInt32(0);
                    EquipmentTemplate equipment = ResolveEquipment(
                        equipmentTemplates, reader.GetInt32(1), $"kit {kitId}");
                    EquipmentKitEntry entry = new(
                        equipment,
                        reader.GetInt32(2),
                        reader.IsDBNull(3) ? null : reader.GetInt32(3));
                    if (!entries.TryGetValue(kitId, out List<EquipmentKitEntry> kitEntries))
                    {
                        kitEntries = [];
                        entries[kitId] = kitEntries;
                    }
                    kitEntries.Add(entry);
                }
            }

            Dictionary<int, EquipmentKitTemplate> result = [];
            foreach (KeyValuePair<int, (string Name, int? ArmorId)> pair in metadata)
            {
                    int kitId = pair.Key;
                    string name = pair.Value.Name;
                    int? armorId = pair.Value.ArmorId;
                    EquipmentTemplate armor = armorId.HasValue
                        ? ResolveEquipment(equipmentTemplates, armorId.Value, $"kit {kitId} armor")
                        : null;
                    result[kitId] = new EquipmentKitTemplate(
                        kitId,
                        name,
                        armor,
                        entries.GetValueOrDefault(kitId));
            }
            return result;
        }

        private static Dictionary<int, PersonalEquipmentRole> ReadPersonalEquipmentRoles(
            IDbConnection connection,
            IReadOnlyDictionary<int, EquipmentKitTemplate> kits)
        {
            Dictionary<int, PersonalEquipmentRole> result = [];
            using IDataReader reader = ExecuteReader(connection,
                "SELECT Id, Name, DefaultKitId, CapacityModifier FROM PersonalEquipmentRole ORDER BY Id");
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                int kitId = reader.GetInt32(2);
                if (!kits.ContainsKey(kitId))
                {
                    throw new InvalidOperationException(
                        $"Personal equipment role {id} references missing kit {kitId}.");
                }
                result[id] = new PersonalEquipmentRole(
                    id,
                    reader.GetString(1),
                    kitId,
                    Convert.ToSingle(reader.GetValue(3)));
            }
            return result;
        }

        private static IDataReader ExecuteReader(IDbConnection connection, string sql)
        {
            IDbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteReader();
        }

        private static BaseSkill ResolveSkill(
            IReadOnlyDictionary<int, BaseSkill> baseSkills,
            int skillId,
            int equipmentId)
        {
            if (baseSkills == null || !baseSkills.TryGetValue(skillId, out BaseSkill skill))
            {
                throw new InvalidOperationException(
                    $"Equipment {equipmentId} references missing base skill {skillId}.");
            }
            return skill;
        }

        private static AmmunitionType ResolveAmmunition(
            IReadOnlyDictionary<int, AmmunitionType> ammunitionTypes,
            int ammunitionId,
            int equipmentId)
        {
            if (!ammunitionTypes.TryGetValue(ammunitionId, out AmmunitionType ammunition))
            {
                throw new InvalidOperationException(
                    $"Equipment {equipmentId} references missing ammunition type {ammunitionId}.");
            }
            return ammunition;
        }

        private static EquipmentTemplate ResolveEquipment(
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates,
            int equipmentId,
            string owner)
        {
            if (!equipmentTemplates.TryGetValue(equipmentId, out EquipmentTemplate equipment))
            {
                throw new InvalidOperationException(
                    $"{owner} references missing equipment template {equipmentId}.");
            }
            return equipment;
        }

        private static void ValidateProfileOwnership(
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates,
            IEnumerable<int> profileIds,
            string profileName)
        {
            int orphan = profileIds.FirstOrDefault(id => !equipmentTemplates.ContainsKey(id));
            if (orphan != 0)
            {
                throw new InvalidOperationException(
                    $"{profileName} equipment profile {orphan} has no EquipmentTemplate row.");
            }
        }

        private static IReadOnlyList<int> ParseIds(string serialized)
        {
            return serialized
                .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture))
                .Distinct()
                .ToList();
        }

        public static int GetRangedEquipmentId(int rangedTemplateId) => 1_000_000 + rangedTemplateId;
        public static int GetMeleeEquipmentId(int meleeTemplateId) => 2_000_000 + meleeTemplateId;
        public static int GetArmorEquipmentId(int armorTemplateId) => 3_000_000 + armorTemplateId;
        public static int GetAmmunitionTypeId(int rangedTemplateId) => 4_000_000 + rangedTemplateId;
        public static int GetAmmunitionPackageId(int ammunitionTypeId) => 5_000_000 + ammunitionTypeId;
        public static int GetKitId(int weaponSetId) => 6_000_000 + weaponSetId;

        private static void AddRanged(
            ICollection<EquipmentKitEntry> entries,
            RangedWeaponTemplate template,
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates,
            bool isGrenade = false)
        {
            if (template == null) return;
            EquipmentTemplate equipment = equipmentTemplates[GetRangedEquipmentId(template.Id)];
            entries.Add(new EquipmentKitEntry(
                equipment,
                isGrenade && template.AmmunitionBehavior == AmmunitionBehavior.ConsumableItem ? 1 : 1,
                isGrenade ? null : entries.Count));
        }

        private static void AddMelee(
            ICollection<EquipmentKitEntry> entries,
            MeleeWeaponTemplate template,
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates)
        {
            if (template == null) return;
            entries.Add(new EquipmentKitEntry(
                equipmentTemplates[GetMeleeEquipmentId(template.Id)],
                1,
                entries.Count));
        }

        private static bool IsBiological(string name)
        {
            string value = name?.ToLowerInvariant() ?? string.Empty;
            return value.Contains("devourer")
                || value.Contains("deathspitter")
                || value.Contains("spinefist")
                || value.Contains("fleshborer")
                || value.Contains("symbiote")
                || value.Contains("bio-");
        }
    }
}
