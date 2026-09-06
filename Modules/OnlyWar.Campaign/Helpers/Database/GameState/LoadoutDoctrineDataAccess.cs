using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    internal sealed class LoadoutDoctrineDataAccess
    {
        internal LoadoutDoctrine GetChapterDoctrine(
            IDbConnection connection,
            IReadOnlyDictionary<int, WeaponSet> weaponSets)
        {
            LoadoutDoctrine doctrine = new();
            using (IDbCommand profileCommand = connection.CreateCommand())
            {
                profileCommand.CommandText = "SELECT SquadTemplateId FROM ChapterLoadout";
                using IDataReader reader = profileCommand.ExecuteReader();
                while (reader.Read())
                {
                    doctrine.SetLoadout(reader.GetInt32(0), []);
                }
            }

            using (IDbCommand itemCommand = connection.CreateCommand())
            {
                itemCommand.CommandText = "SELECT SquadTemplateId, WeaponSetId FROM ChapterLoadoutWeaponSet";
                using IDataReader reader = itemCommand.ExecuteReader();
                Dictionary<int, List<WeaponSet>> items = [];
                while (reader.Read())
                {
                    int templateId = reader.GetInt32(0);
                    if (!items.TryGetValue(templateId, out List<WeaponSet> loadout))
                    {
                        loadout = [];
                        items[templateId] = loadout;
                    }
                    loadout.Add(weaponSets[reader.GetInt32(1)]);
                }
                foreach ((int templateId, List<WeaponSet> loadout) in items)
                {
                    doctrine.SetLoadout(templateId, loadout);
                }
            }
            return doctrine;
        }

        internal void PopulatePlanetDoctrines(
            IDbConnection connection,
            IReadOnlyDictionary<int, Planet> planets,
            IReadOnlyDictionary<int, WeaponSet> weaponSets)
        {
            using (IDbCommand profileCommand = connection.CreateCommand())
            {
                profileCommand.CommandText = "SELECT PlanetId, SquadTemplateId FROM PlanetLoadout";
                using IDataReader reader = profileCommand.ExecuteReader();
                while (reader.Read())
                {
                    planets[reader.GetInt32(0)].LoadoutDoctrine.SetLoadout(reader.GetInt32(1), []);
                }
            }

            using (IDbCommand itemCommand = connection.CreateCommand())
            {
                itemCommand.CommandText = "SELECT PlanetId, SquadTemplateId, WeaponSetId FROM PlanetLoadoutWeaponSet";
                using IDataReader reader = itemCommand.ExecuteReader();
                Dictionary<(int PlanetId, int TemplateId), List<WeaponSet>> items = [];
                while (reader.Read())
                {
                    var key = (reader.GetInt32(0), reader.GetInt32(1));
                    if (!items.TryGetValue(key, out List<WeaponSet> loadout))
                    {
                        loadout = [];
                        items[key] = loadout;
                    }
                    loadout.Add(weaponSets[reader.GetInt32(2)]);
                }
                foreach (((int planetId, int templateId), List<WeaponSet> loadout) in items)
                {
                    planets[planetId].LoadoutDoctrine.SetLoadout(templateId, loadout);
                }
            }
        }

        /// <summary>
        /// Reads both character layers: chapter-wide role defaults keyed by soldier template, and
        /// personal overrides keyed by soldier. Absent rows inherit, ending at the role's authored
        /// default in the rules database.
        /// </summary>
        internal CharacterLoadoutDoctrine GetCharacterDoctrine(
            IDbConnection connection,
            IReadOnlyDictionary<int, WeaponSet> weaponSets)
        {
            CharacterLoadoutDoctrine doctrine = new();
            using (IDbCommand roleCommand = connection.CreateCommand())
            {
                roleCommand.CommandText =
                    "SELECT SoldierTemplateId, WeaponSetId FROM ChapterCharacterLoadout";
                using IDataReader reader = roleCommand.ExecuteReader();
                while (reader.Read())
                {
                    doctrine.SetRoleDefault(reader.GetInt32(0), weaponSets[reader.GetInt32(1)]);
                }
            }

            using (IDbCommand personalCommand = connection.CreateCommand())
            {
                personalCommand.CommandText = "SELECT SoldierId, WeaponSetId FROM SoldierLoadout";
                using IDataReader reader = personalCommand.ExecuteReader();
                while (reader.Read())
                {
                    doctrine.SetPersonalLoadout(reader.GetInt32(0), weaponSets[reader.GetInt32(1)]);
                }
            }
            return doctrine;
        }

        internal void SaveCharacterDoctrine(
            IDbTransaction transaction, CharacterLoadoutDoctrine doctrine)
        {
            if (doctrine == null) return;

            // Role defaults key on a rules-database soldier template, which is not part of the
            // save, so they have nothing to dangle from.
            foreach ((int soldierTemplateId, WeaponSet weaponSet) in doctrine.RoleDefaults)
            {
                using IDbCommand command = transaction.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO ChapterCharacterLoadout (SoldierTemplateId, WeaponSetId) "
                    + "VALUES (@key, @weaponSetId)";
                command.AddParam("@key", soldierTemplateId);
                command.AddParam("@weaponSetId", weaponSet.Id);
                command.ExecuteNonQuery();
            }

            // Personal loadouts do dangle: a character can be killed or discharged between the
            // loadout being set and the save. Insert only for soldiers still on the roster, so a
            // stale entry is quietly dropped rather than failing the save on its foreign key.
            foreach ((int soldierId, WeaponSet weaponSet) in doctrine.PersonalLoadouts)
            {
                using IDbCommand command = transaction.Connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO SoldierLoadout (SoldierId, WeaponSetId) "
                    + "SELECT @key, @weaponSetId "
                    + "WHERE EXISTS (SELECT 1 FROM Soldier WHERE Id = @key)";
                command.AddParam("@key", soldierId);
                command.AddParam("@weaponSetId", weaponSet.Id);
                command.ExecuteNonQuery();
            }
        }

        internal EquipmentLoadoutDoctrine GetEquipmentDoctrine(
            IDbConnection connection,
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates,
            IReadOnlyDictionary<int, EquipmentKitTemplate> equipmentKits)
        {
            EquipmentLoadoutDoctrine doctrine = new();
            Dictionary<int, EquipmentLoadout> roleLoadouts = ReadEquipmentProfiles(
                connection,
                "ChapterEquipmentRoleLoadout",
                "PersonalEquipmentRoleId",
                equipmentTemplates);
            foreach ((int roleId, EquipmentLoadout loadout) in roleLoadouts)
            {
                doctrine.SetRoleDefault(roleId, loadout);
            }

            Dictionary<int, EquipmentLoadout> personalLoadouts = ReadEquipmentProfiles(
                connection,
                "SoldierEquipmentLoadout",
                "SoldierId",
                equipmentTemplates);
            foreach ((int soldierId, EquipmentLoadout loadout) in personalLoadouts)
            {
                doctrine.SetPersonalLoadout(soldierId, loadout);
            }
            return doctrine;
        }

        internal void SaveEquipmentDoctrine(
            IDbTransaction transaction, EquipmentLoadoutDoctrine doctrine)
        {
            if (doctrine == null)
            {
                return;
            }

            foreach ((int roleId, EquipmentLoadout loadout) in doctrine.RoleDefaults)
            {
                InsertEquipmentProfile(
                    transaction,
                    "ChapterEquipmentRoleLoadout",
                    "PersonalEquipmentRoleId",
                    roleId,
                    loadout);
                InsertEquipmentItems(
                    transaction,
                    "ChapterEquipmentRoleLoadoutItem",
                    "PersonalEquipmentRoleId",
                    roleId,
                    loadout);
            }

            foreach ((int soldierId, EquipmentLoadout loadout) in doctrine.PersonalLoadouts)
            {
                using IDbCommand profileCommand = transaction.Connection.CreateCommand();
                profileCommand.Transaction = transaction;
                profileCommand.CommandText =
                    "INSERT INTO SoldierEquipmentLoadout (SoldierId, ArmorEquipmentId) "
                    + "SELECT @key, @armorId "
                    + "WHERE EXISTS (SELECT 1 FROM Soldier WHERE Id = @key)";
                profileCommand.AddParam("@key", soldierId);
                profileCommand.AddParam("@armorId", loadout.Armor?.Id);
                profileCommand.ExecuteNonQuery();

                using IDbCommand itemCommand = transaction.Connection.CreateCommand();
                itemCommand.Transaction = transaction;
                itemCommand.CommandText =
                    "INSERT INTO SoldierEquipmentLoadoutItem "
                    + "(SoldierId, EquipmentId, Quantity, InitialReadyOrder) "
                    + "SELECT @key, @equipmentId, @quantity, @readyOrder "
                    + "WHERE EXISTS (SELECT 1 FROM Soldier WHERE Id = @key)";
                itemCommand.AddParam("@key", soldierId);
                itemCommand.AddParam("@equipmentId", 0);
                itemCommand.AddParam("@quantity", 0);
                itemCommand.AddParam("@readyOrder", null);
                foreach (EquipmentLoadoutEntry item in loadout.Items)
                {
                    ((IDataParameter)itemCommand.Parameters["@equipmentId"]).Value = item.Equipment.Id;
                    ((IDataParameter)itemCommand.Parameters["@quantity"]).Value = item.Quantity;
                    ((IDataParameter)itemCommand.Parameters["@readyOrder"]).Value = item.InitialReadyOrder;
                    itemCommand.ExecuteNonQuery();
                }
            }
        }

        private static Dictionary<int, EquipmentLoadout> ReadEquipmentProfiles(
            IDbConnection connection,
            string profileTable,
            string keyColumn,
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates)
        {
            Dictionary<int, EquipmentTemplate> armorByKey = [];
            Dictionary<int, List<EquipmentLoadoutEntry>> itemsByKey = [];
            using (IDbCommand profileCommand = connection.CreateCommand())
            {
                profileCommand.CommandText =
                    $"SELECT {keyColumn}, ArmorEquipmentId FROM {profileTable}";
                using IDataReader reader = profileCommand.ExecuteReader();
                while (reader.Read())
                {
                    int key = reader.GetInt32(0);
                    armorByKey[key] = reader.IsDBNull(1)
                        ? null
                        : ResolveEquipment(equipmentTemplates, reader.GetInt32(1));
                }
            }

            string itemTable = profileTable + "Item";
            using (IDbCommand itemCommand = connection.CreateCommand())
            {
                itemCommand.CommandText =
                    $"SELECT {keyColumn}, EquipmentId, Quantity, InitialReadyOrder FROM {itemTable}";
                using IDataReader reader = itemCommand.ExecuteReader();
                while (reader.Read())
                {
                    int key = reader.GetInt32(0);
                    if (!itemsByKey.TryGetValue(key, out List<EquipmentLoadoutEntry> items))
                    {
                        items = [];
                        itemsByKey[key] = items;
                    }
                    items.Add(new EquipmentLoadoutEntry(
                        ResolveEquipment(equipmentTemplates, reader.GetInt32(1)),
                        reader.GetInt32(2),
                        reader.IsDBNull(3) ? null : reader.GetInt32(3)));
                }
            }

            foreach ((int key, List<EquipmentLoadoutEntry> items) in itemsByKey)
            {
                if (!armorByKey.ContainsKey(key))
                {
                    armorByKey[key] = null;
                }
            }

            return armorByKey.ToDictionary(
                pair => pair.Key,
                pair => new EquipmentLoadout(
                    pair.Value,
                    itemsByKey.GetValueOrDefault(pair.Key) ?? []));
        }

        private static EquipmentTemplate ResolveEquipment(
            IReadOnlyDictionary<int, EquipmentTemplate> equipmentTemplates,
            int equipmentId)
        {
            if (equipmentTemplates == null
                || !equipmentTemplates.TryGetValue(equipmentId, out EquipmentTemplate equipment))
            {
                throw new InvalidDataException(
                    $"Save references unknown equipment template {equipmentId}.");
            }
            return equipment;
        }

        private static void InsertEquipmentProfile(
            IDbTransaction transaction,
            string table,
            string keyColumn,
            int key,
            EquipmentLoadout loadout)
        {
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"INSERT INTO {table} ({keyColumn}, ArmorEquipmentId) VALUES (@key, @armorId)";
            command.AddParam("@key", key);
            command.AddParam("@armorId", loadout.Armor?.Id);
            command.ExecuteNonQuery();
        }

        private static void InsertEquipmentItems(
            IDbTransaction transaction,
            string table,
            string keyColumn,
            int key,
            EquipmentLoadout loadout)
        {
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"INSERT INTO {table} ({keyColumn}, EquipmentId, Quantity, InitialReadyOrder) "
                + "VALUES (@key, @equipmentId, @quantity, @readyOrder)";
            command.AddParam("@key", key);
            command.AddParam("@equipmentId", 0);
            command.AddParam("@quantity", 0);
            command.AddParam("@readyOrder", null);
            foreach (EquipmentLoadoutEntry item in loadout.Items)
            {
                ((IDataParameter)command.Parameters["@equipmentId"]).Value = item.Equipment.Id;
                ((IDataParameter)command.Parameters["@quantity"]).Value = item.Quantity;
                ((IDataParameter)command.Parameters["@readyOrder"]).Value = item.InitialReadyOrder;
                command.ExecuteNonQuery();
            }
        }

        internal void SaveChapterDoctrine(IDbTransaction transaction, LoadoutDoctrine doctrine)
        {
            foreach ((int templateId, List<WeaponSet> loadout) in doctrine?.Loadouts ??
                     new Dictionary<int, List<WeaponSet>>())
            {
                InsertProfile(transaction, "ChapterLoadout", null, templateId);
                foreach (WeaponSet weaponSet in loadout)
                {
                    InsertItem(transaction, "ChapterLoadoutWeaponSet", null, templateId, weaponSet.Id);
                }
            }
        }

        internal void SavePlanetDoctrine(IDbTransaction transaction, Planet planet)
        {
            foreach ((int templateId, List<WeaponSet> loadout) in planet.LoadoutDoctrine.Loadouts)
            {
                InsertProfile(transaction, "PlanetLoadout", planet.Id, templateId);
                foreach (WeaponSet weaponSet in loadout)
                {
                    InsertItem(transaction, "PlanetLoadoutWeaponSet", planet.Id, templateId, weaponSet.Id);
                }
            }
        }

        private static void InsertProfile(
            IDbTransaction transaction, string table, int? planetId, int templateId)
        {
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = planetId.HasValue
                ? $"INSERT INTO {table} (PlanetId, SquadTemplateId) VALUES (@planetId, @templateId)"
                : $"INSERT INTO {table} (SquadTemplateId) VALUES (@templateId)";
            if (planetId.HasValue) command.AddParam("@planetId", planetId.Value);
            command.AddParam("@templateId", templateId);
            command.ExecuteNonQuery();
        }

        private static void InsertItem(
            IDbTransaction transaction, string table, int? planetId, int templateId, int weaponSetId)
        {
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = planetId.HasValue
                ? $"INSERT INTO {table} (PlanetId, SquadTemplateId, WeaponSetId) VALUES (@planetId, @templateId, @weaponSetId)"
                : $"INSERT INTO {table} (SquadTemplateId, WeaponSetId) VALUES (@templateId, @weaponSetId)";
            if (planetId.HasValue) command.AddParam("@planetId", planetId.Value);
            command.AddParam("@templateId", templateId);
            command.AddParam("@weaponSetId", weaponSetId);
            command.ExecuteNonQuery();
        }
    }
}
