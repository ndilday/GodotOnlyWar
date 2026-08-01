using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Data;

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
