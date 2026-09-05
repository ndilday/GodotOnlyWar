using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using System.Data;
using System.Data.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using OnlyWar.Models.Equippables;
namespace OnlyWar.Helpers.Database.GameRules
{
    public static class EquipmentCatalogLoader
    {
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
                "SELECT * FROM EquipmentArmorProfile ORDER BY EquipmentId");
            while (reader.Read())
            {
                result[reader.GetInt32(0)] = new ArmorProfile(
                    checked((byte)reader.GetInt32(1)),
                    checked((short)reader.GetInt32(2)),
                    Convert.ToSingle(reader.GetValue(3)),
                    reader.FieldCount > 4 && !reader.IsDBNull(4)
                        && Convert.ToBoolean(reader.GetValue(4)));
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
            try
            {
                using IDataReader reader = ExecuteReader(connection,
                    "SELECT EquipmentId, CapacityBonus FROM EquipmentGearProfile ORDER BY EquipmentId");
                while (reader.Read())
                {
                    result[reader.GetInt32(0)] = new GearProfile(Convert.ToSingle(reader.GetValue(1)));
                }
            }
            catch (DbException exception) when (
                exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // Gear profiles are an optional extension. Missing means that no equipment row
                // receives a gear-specific capacity bonus; an existing itemized catalog remains
                // authoritative for every other profile.
            }
            return result;
        }

        private static Dictionary<int, List<EquipmentRequirement>> ReadRequirements(IDbConnection connection)
        {
            Dictionary<int, List<EquipmentRequirement>> result = [];
            try
            {
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
            }
            catch (DbException exception) when (
                exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // Requirements are an optional extension. An absent table means that no
                // data-authored requirement constraints are applied.
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

    }
}
