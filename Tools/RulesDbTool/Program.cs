using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: RulesDbTool <schema|training-source|migrate-training> <db-path>");
    return 1;
}

string command = args[0];
string dbPath = args[1];
string connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = command == "migrate-training" ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly
}.ToString();

using SqliteConnection connection = new(connectionString);
connection.Open();

switch (command)
{
    case "schema":
        PrintSchema(connection);
        break;
    case "training-source":
        PrintTrainingSourceData(connection);
        break;
    case "migrate-training":
        MigrateTrainingProfiles(connection);
        break;
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        return 1;
}

return 0;

static void PrintSchema(SqliteConnection connection)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' ORDER BY name";
    using SqliteDataReader reader = command.ExecuteReader();
    while (reader.Read())
    {
        Console.WriteLine($"--- {reader.GetString(0)} ---");
        Console.WriteLine(reader.GetString(1));
    }
}

static void PrintTrainingSourceData(SqliteConnection connection)
{
    Console.WriteLine("--- SoldierTemplate ---");
    using (SqliteCommand command = connection.CreateCommand())
    {
        command.CommandText = "SELECT Id, Name FROM SoldierTemplate ORDER BY Id";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"{reader.GetInt32(0)}\t{reader.GetString(1)}");
        }
    }

    Console.WriteLine("--- BaseSkill ---");
    using (SqliteCommand command = connection.CreateCommand())
    {
        command.CommandText = "SELECT Id, Name FROM BaseSkill ORDER BY Id";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            Console.WriteLine($"{reader.GetInt32(0)}\t{reader.GetString(1)}");
        }
    }
}

static void MigrateTrainingProfiles(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    Execute(connection, transaction, """
        CREATE TABLE IF NOT EXISTS TrainingProfile (
            Id INTEGER PRIMARY KEY,
            Name TEXT NOT NULL UNIQUE
        );
        """);

    Execute(connection, transaction, """
        CREATE TABLE IF NOT EXISTS TrainingProfileEntry (
            TrainingProfileId INTEGER NOT NULL,
            TargetType INTEGER NOT NULL,
            TargetId INTEGER NOT NULL,
            Weight REAL NOT NULL,
            FOREIGN KEY (TrainingProfileId) REFERENCES TrainingProfile(Id)
        );
        """);

    EnsureColumn(connection, transaction, "SoldierTemplate", "WorkExperienceTrainingProfileId", "INTEGER");

    Dictionary<string, int> skillIds = LoadIds(connection, transaction, "BaseSkill");
    Dictionary<string, int> soldierTemplateIds = LoadIds(connection, transaction, "SoldierTemplate");

    int nextProfileId = ExecuteScalarInt(connection, transaction, "SELECT COALESCE(MAX(Id), 0) + 1 FROM TrainingProfile");

    UpsertProfile(connection, transaction, ref nextProfileId, "veteran_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Drive (Bike)", 1),
        Skill(skillIds, "Jump Pack", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Sword", 1)
    ], soldierTemplateIds, ["Veteran"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "tactical_marine_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Sword", 1),
        Skill(skillIds, "Gunnery (Rocket)", 1),
        Skill(skillIds, "Gunnery (Bolter)", 1),
        Skill(skillIds, "Gun (Plasma)", 1),
        Skill(skillIds, "Gun (Flamer)", 1)
    ], soldierTemplateIds, ["Tactical Marine"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "tactical_sergeant_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Sword", 1),
        Skill(skillIds, "Tactics", 2),
        Skill(skillIds, "Leadership", 2)
    ], soldierTemplateIds, ["Sergeant"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "assault_marine_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Drive (Bike)", 1),
        Skill(skillIds, "Jump Pack", 1),
        Skill(skillIds, "Gun (Bolter)", 2),
        Skill(skillIds, "Sword", 2)
    ], soldierTemplateIds, ["Assault Marine"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "assault_sergeant_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Drive (Bike)", 1),
        Skill(skillIds, "Jump Pack", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Sword", 1),
        Skill(skillIds, "Tactics", 1),
        Skill(skillIds, "Leadership", 1)
    ], soldierTemplateIds, ["Sergeant (A)"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "devastator_marine_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Gunnery (Bolter)", 1),
        Skill(skillIds, "Gun (Plasma)", 1),
        Skill(skillIds, "Gun (Flamer)", 1),
        Skill(skillIds, "Gunnery (Rocket)", 1),
        Skill(skillIds, "Gunnery (Laser)", 1)
    ], soldierTemplateIds, ["Devastator Marine"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "devastator_sergeant_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Gunnery (Bolter)", 1),
        Skill(skillIds, "Tactics", 2),
        Skill(skillIds, "Leadership", 2)
    ], soldierTemplateIds, ["Sergeant (D)"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "scout_marine_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Gunnery (Bolter)", 2),
        Skill(skillIds, "Stealth", 1),
        Skill(skillIds, "Gun (Sniper)", 1),
        Skill(skillIds, "Gun (Shotgun)", 1)
    ], soldierTemplateIds, ["Scout Marine"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "scout_sergeant_work", [
        Skill(skillIds, "Marine", 1),
        Skill(skillIds, "Power Armor", 1),
        Skill(skillIds, "Armory (Small Arms)", 1),
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Gunnery (Bolter)", 1),
        Skill(skillIds, "Stealth", 1),
        Skill(skillIds, "Tactics", 1),
        Skill(skillIds, "Leadership", 1),
        Skill(skillIds, "Teaching", 1)
    ], soldierTemplateIds, ["Scout Sergeant"]);

    UpsertProfile(connection, transaction, ref nextProfileId, "scout_focus_melee", [
        Skill(skillIds, "Sword", 1),
        Skill(skillIds, "Shield", 1),
        Skill(skillIds, "Axe", 1),
        Skill(skillIds, "Fist", 1)
    ], soldierTemplateIds, []);

    UpsertProfile(connection, transaction, ref nextProfileId, "scout_focus_ranged", [
        Skill(skillIds, "Gun (Bolter)", 1),
        Skill(skillIds, "Gunnery (Laser)", 1),
        Skill(skillIds, "Gun (Flamer)", 1),
        Skill(skillIds, "Gun (Sniper)", 1),
        Skill(skillIds, "Gun (Shotgun)", 1)
    ], soldierTemplateIds, []);

    UpsertProfile(connection, transaction, ref nextProfileId, "scout_focus_vehicles", [
        Skill(skillIds, "Drive (Bike)", 1),
        Skill(skillIds, "Pilot (Land Speeder)", 1),
        Skill(skillIds, "Drive (Rhino)", 1),
        Skill(skillIds, "Gunnery (Bolter)", 1)
    ], soldierTemplateIds, []);

    UpsertProfile(connection, transaction, ref nextProfileId, "scout_focus_physical", [
        AttributeEntry((int)OnlyWarAttribute.Strength, 1),
        AttributeEntry((int)OnlyWarAttribute.Dexterity, 1),
        AttributeEntry((int)OnlyWarAttribute.Constitution, 1)
    ], soldierTemplateIds, []);

    transaction.Commit();
    Console.WriteLine("Training profile migration complete.");
}

static TrainingEntry Skill(Dictionary<string, int> skillIds, string name, float weight)
{
    return new TrainingEntry(1, skillIds[name], weight);
}

static TrainingEntry AttributeEntry(int attributeId, float weight)
{
    return new TrainingEntry(2, attributeId, weight);
}

static void UpsertProfile(
    SqliteConnection connection,
    SqliteTransaction transaction,
    ref int nextProfileId,
    string name,
    IReadOnlyList<TrainingEntry> entries,
    IReadOnlyDictionary<string, int> soldierTemplateIds,
    IReadOnlyList<string> soldierTemplateNames)
{
    int profileId = ExecuteScalarInt(connection, transaction, "SELECT Id FROM TrainingProfile WHERE Name = $name", ("$name", name));
    if (profileId == 0)
    {
        profileId = nextProfileId++;
        Execute(connection, transaction, "INSERT INTO TrainingProfile (Id, Name) VALUES ($id, $name)", ("$id", profileId), ("$name", name));
    }

    Execute(connection, transaction, "DELETE FROM TrainingProfileEntry WHERE TrainingProfileId = $id", ("$id", profileId));
    foreach (TrainingEntry entry in entries)
    {
        Execute(
            connection,
            transaction,
            "INSERT INTO TrainingProfileEntry (TrainingProfileId, TargetType, TargetId, Weight) VALUES ($profileId, $targetType, $targetId, $weight)",
            ("$profileId", profileId),
            ("$targetType", entry.TargetType),
            ("$targetId", entry.TargetId),
            ("$weight", entry.Weight));
    }

    foreach (string soldierTemplateName in soldierTemplateNames)
    {
        Execute(
            connection,
            transaction,
            "UPDATE SoldierTemplate SET WorkExperienceTrainingProfileId = $profileId WHERE Id = $soldierTemplateId",
            ("$profileId", profileId),
            ("$soldierTemplateId", soldierTemplateIds[soldierTemplateName]));
    }
}

static void EnsureColumn(SqliteConnection connection, SqliteTransaction transaction, string table, string column, string definition)
{
    using SqliteCommand pragma = connection.CreateCommand();
    pragma.Transaction = transaction;
    pragma.CommandText = $"PRAGMA table_info({table})";
    using SqliteDataReader reader = pragma.ExecuteReader();
    while (reader.Read())
    {
        if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    Execute(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
}

static Dictionary<string, int> LoadIds(SqliteConnection connection, SqliteTransaction transaction, string table)
{
    Dictionary<string, int> ids = [];
    using SqliteCommand command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = $"SELECT Id, Name FROM {table}";
    using SqliteDataReader reader = command.ExecuteReader();
    while (reader.Read())
    {
        ids[reader.GetString(1)] = reader.GetInt32(0);
    }

    return ids;
}

static int ExecuteScalarInt(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
{
    using SqliteCommand command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach ((string name, object value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    object result = command.ExecuteScalar();
    return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
}

static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
{
    using SqliteCommand command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach ((string name, object value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    command.ExecuteNonQuery();
}

internal readonly record struct TrainingEntry(int TargetType, int TargetId, float Weight);

internal enum OnlyWarAttribute
{
    Strength = 1,
    Dexterity = 2,
    Constitution = 3
}
