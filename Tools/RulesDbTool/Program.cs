using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: RulesDbTool <schema|training-source|migrate-training|migrate-progenoid|migrate-ratings|migrate-planet-scales|migrate-fortification|migrate-tyranids|migrate-tyranid-squads|migrate-evasion|migrate-squad-caps|migrate-veteran-sergeant|migrate-collapse-sergeants|migrate-chaplaincy|migrate-company-judiciar|migrate-remove-veteran-captain|migrate-remove-recruitment-captain|remove-unused-unit-templates> <db-path>");
    return 1;
}

string command = args[0];
string dbPath = args[1];
string connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = command is "migrate-training" or "migrate-progenoid" or "migrate-ratings" or "migrate-planet-scales" or "migrate-fortification" or "migrate-tyranids" or "migrate-tyranid-squads" or "migrate-evasion" or "migrate-squad-caps" or "migrate-veteran-sergeant" or "migrate-collapse-sergeants" or "migrate-chaplaincy" or "migrate-company-judiciar" or "migrate-remove-veteran-captain" or "migrate-remove-recruitment-captain" or "remove-unused-unit-templates" ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly
}.ToString();

using SqliteConnection connection = new(connectionString);
connection.Open();

switch (command)
{
    case "schema":
        PrintSchema(connection);
        break;
    case "remove-unused-unit-templates":
        RemoveUnusedUnitTemplates(connection);
        break;
    case "training-source":
        PrintTrainingSourceData(connection);
        break;
    case "migrate-training":
        MigrateTrainingProfiles(connection);
        break;
    case "migrate-progenoid":
        MigrateProgenoidLocations(connection);
        break;
    case "migrate-ratings":
        MigrateRatings(connection);
        break;
    case "migrate-planet-scales":
        MigratePlanetScales(connection);
        break;
    case "migrate-fortification":
        MigrateFortificationSkill(connection);
        break;
    case "migrate-tyranids":
        MigrateTyranids(connection);
        break;
    case "migrate-tyranid-squads":
        MigrateTyranidSquads(connection);
        break;
    case "migrate-evasion":
        MigrateEvasion(connection);
        break;
    case "migrate-squad-caps":
        MigrateSquadCaps(connection);
        break;
    case "migrate-veteran-sergeant":
        MigrateVeteranSergeant(connection);
        break;
    case "migrate-collapse-sergeants":
        MigrateCollapseSergeants(connection);
        break;
    case "migrate-chaplaincy":
        MigrateChaplaincy(connection);
        break;
    case "migrate-company-judiciar":
        MigrateCompanyJudiciar(connection);
        break;
    case "migrate-remove-veteran-captain":
        MigrateRemoveVeteranCaptain(connection);
        break;
    case "migrate-remove-recruitment-captain":
        MigrateRemoveRecruitmentCaptain(connection);
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

static void RemoveUnusedUnitTemplates(SqliteConnection connection)
{
    // Only the player faction's UnitTemplate hierarchy is ever instantiated
    // (NewChapterBuilder builds the Space Marine chapter from its top-level
    // UnitTemplate). Every non-player faction's fixed UnitTemplate "armies" were
    // legacy scaffolding that nothing reads — non-player forces are assembled
    // dynamically from SquadTemplates by ForceGenerator. Drop the dead data.
    using SqliteTransaction transaction = connection.BeginTransaction();

    const string nonPlayerUnits =
        "SELECT Id FROM UnitTemplate WHERE FactionId IN (SELECT Id FROM Faction WHERE IsPlayerFaction = 0)";

    int doomed = ExecuteScalarInt(connection, transaction, $"SELECT COUNT(*) FROM ({nonPlayerUnits})");
    if (doomed == 0)
    {
        Console.WriteLine("No non-player UnitTemplates found; nothing to do.");
        return;
    }

    int treeRows = Execute(connection, transaction,
        $"DELETE FROM UnitTemplateTree WHERE ParentUnitTemplateId IN ({nonPlayerUnits}) OR ChildUnitTemplateId IN ({nonPlayerUnits})");
    int squadRows = Execute(connection, transaction,
        $"DELETE FROM UnitTemplateSquadTemplate WHERE UnitTemplateId IN ({nonPlayerUnits})");
    int unitRows = Execute(connection, transaction,
        "DELETE FROM UnitTemplate WHERE FactionId IN (SELECT Id FROM Faction WHERE IsPlayerFaction = 0)");

    transaction.Commit();
    Console.WriteLine($"Removed {unitRows} non-player UnitTemplate rows " +
                      $"({squadRows} UnitTemplateSquadTemplate, {treeRows} UnitTemplateTree links).");
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

static void MigrateProgenoidLocations(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // Semantic flag replacing the hardcoded "Face"/"Torso" name checks in
    // BattleTurnResolver's geneseed-status logic (TDD §8.3). A hit location that
    // holds a progenoid gland destroys the soldier's geneseed when severed.
    EnsureColumn(connection, transaction, "HitLocationTemplate", "HoldsProgenoid", "INTEGER NOT NULL DEFAULT 0");

    Execute(connection, transaction,
        "UPDATE HitLocationTemplate SET HoldsProgenoid = 1 WHERE Name IN ('Face', 'Torso')");

    int updated = ExecuteScalarInt(connection, transaction,
        "SELECT COUNT(*) FROM HitLocationTemplate WHERE HoldsProgenoid = 1");

    transaction.Commit();
    Console.WriteLine($"Progenoid migration complete. {updated} hit-location rows flagged.");
}

static void MigrateRatings(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    Execute(connection, transaction, """
        CREATE TABLE IF NOT EXISTS RatingDefinition (
            Id          INTEGER PRIMARY KEY,
            RatingKey   TEXT NOT NULL UNIQUE,
            DisplayName TEXT NOT NULL,
            Aggregation INTEGER NOT NULL
        );
        """);
    Execute(connection, transaction, """
        CREATE TABLE IF NOT EXISTS RatingComponent (
            Id                 INTEGER PRIMARY KEY,
            RatingDefinitionId INTEGER NOT NULL REFERENCES RatingDefinition(Id),
            Ordinal            INTEGER NOT NULL,
            ComponentType      INTEGER NOT NULL,
            TargetId           INTEGER NOT NULL
        );
        """);
    Execute(connection, transaction, """
        CREATE TABLE IF NOT EXISTS RatingNormalizationFactor (
            Id                 INTEGER PRIMARY KEY,
            RatingDefinitionId INTEGER NOT NULL REFERENCES RatingDefinition(Id),
            Ordinal            INTEGER NOT NULL,
            Low                REAL NOT NULL,
            High               REAL NOT NULL
        );
        """);
    Execute(connection, transaction, """
        CREATE TABLE IF NOT EXISTS RatingAwardTier (
            Id                 INTEGER PRIMARY KEY,
            RatingDefinitionId INTEGER NOT NULL REFERENCES RatingDefinition(Id),
            Level              INTEGER NOT NULL,
            Threshold          REAL NOT NULL,
            EffectType         INTEGER NOT NULL,
            AwardType          TEXT,
            NameTemplate       TEXT NOT NULL
        );
        """);

    // Idempotent: clear and re-seed (rules data, not user data).
    Execute(connection, transaction, "DELETE FROM RatingAwardTier");
    Execute(connection, transaction, "DELETE FROM RatingNormalizationFactor");
    Execute(connection, transaction, "DELETE FROM RatingComponent");
    Execute(connection, transaction, "DELETE FROM RatingDefinition");

    Dictionary<string, int> skill = LoadIds(connection, transaction, "BaseSkill");

    // Attribute enum values (Models.Soldiers.Attribute).
    const int Strength = 1, Dexterity = 2, Constitution = 3, Ego = 6;
    // SkillCategory enum values.
    const int RangedCategory = 1;
    // RatingComponentType enum values.
    const int AttributeValue = 0, SkillTotal = 1, BestSkillBonusInCategory = 2;
    // RatingAggregation enum values.
    const int Product = 0, Sum = 1;
    // RatingAwardEffect enum values.
    const int AwardEffect = 0, HistoryFlag = 1;

    int componentId = 1, factorId = 1, tierId = 1;

    void Component(int defId, int ordinal, int type, int targetId) => Execute(connection, transaction,
        "INSERT INTO RatingComponent (Id, RatingDefinitionId, Ordinal, ComponentType, TargetId) VALUES ($id,$d,$o,$t,$tg)",
        ("$id", componentId++), ("$d", defId), ("$o", ordinal), ("$t", type), ("$tg", targetId));

    void Factor(int defId, int ordinal, double low, double high) => Execute(connection, transaction,
        "INSERT INTO RatingNormalizationFactor (Id, RatingDefinitionId, Ordinal, Low, High) VALUES ($id,$d,$o,$l,$h)",
        ("$id", factorId++), ("$d", defId), ("$o", ordinal), ("$l", low), ("$h", high));

    void Definition(int id, string key, string display, int aggregation) => Execute(connection, transaction,
        "INSERT INTO RatingDefinition (Id, RatingKey, DisplayName, Aggregation) VALUES ($id,$k,$n,$a)",
        ("$id", id), ("$k", key), ("$n", display), ("$a", aggregation));

    void Tier(int defId, int level, double threshold, int effect, string awardType, string name) => Execute(connection, transaction,
        "INSERT INTO RatingAwardTier (Id, RatingDefinitionId, Level, Threshold, EffectType, AwardType, NameTemplate) VALUES ($id,$d,$l,$t,$e,$at,$n)",
        ("$id", tierId++), ("$d", defId), ("$l", level), ("$t", threshold), ("$e", effect),
        ("$at", (object)awardType ?? DBNull.Value), ("$n", name));

    // 1. melee = STR * skillTotal(Sword)
    Definition(1, "melee", "Melee", Product);
    Component(1, 0, AttributeValue, Strength);
    Component(1, 1, SkillTotal, skill["Sword"]);
    Factor(1, 0, 1.44, 1.76);
    Factor(1, 1, 1.44, 1.76);
    Tier(1, 4, 115, AwardEffect, "Sword", "Adamantium Sword of the Emperor");
    Tier(1, 3, 105, AwardEffect, "Sword", "Gold Sword of the Emperor");
    Tier(1, 2, 99, AwardEffect, "Sword", "Silver Sword of the Emperor");
    Tier(1, 1, 90, AwardEffect, "Sword", "Bronze Sword of the Emperor");

    // 2. ranged = DEX + bestBonusInCategory(Ranged)
    Definition(2, "ranged", "Ranged", Sum);
    Component(2, 0, AttributeValue, Dexterity);
    Component(2, 1, BestSkillBonusInCategory, RangedCategory);
    Factor(2, 0, 0.144, 0.176);
    Tier(2, 4, 120, AwardEffect, "Gun", "Adamantium {bestSkillInCategory} of the Emperor");
    Tier(2, 3, 115, AwardEffect, "Gun", "Gold {bestSkillInCategory} of the Emperor");
    Tier(2, 2, 110, AwardEffect, "Gun", "Silver {bestSkillInCategory} of the Emperor");
    Tier(2, 1, 105, AwardEffect, "Gun", "Bronze {bestSkillInCategory} of the Emperor");

    // 3. leadership = EGO * skillTotal(Leadership) * skillTotal(Tactics)
    Definition(3, "leadership", "Leadership", Product);
    Component(3, 0, AttributeValue, Ego);
    Component(3, 1, SkillTotal, skill["Leadership"]);
    Component(3, 2, SkillTotal, skill["Tactics"]);
    Factor(3, 0, 12.6, 15.4);
    Factor(3, 1, 1.26, 1.54);
    Factor(3, 2, 1.26, 1.54);
    Tier(3, 4, 95, AwardEffect, "Voice", "Adamantium Voice of the Emperor");
    Tier(3, 3, 65, AwardEffect, "Voice", "Gold Voice of the Emperor");
    Tier(3, 2, 55, AwardEffect, "Voice", "Silver Voice of the Emperor");
    Tier(3, 1, 50, AwardEffect, "Voice", "Bronze Voice of the Emperor");

    // 4. ancient = EGO * CON
    Definition(4, "ancient", "Ancient", Product);
    Component(4, 0, AttributeValue, Ego);
    Component(4, 1, AttributeValue, Constitution);
    Factor(4, 0, 1.26, 1.54);
    Factor(4, 1, 2.88, 3.52);
    Tier(4, 4, 120, AwardEffect, "Banner", "Adamantium Banner of the Emperor");
    Tier(4, 3, 110, AwardEffect, "Banner", "Gold Banner of the Emperor");
    Tier(4, 2, 100, AwardEffect, "Banner", "Silver Banner of the Emperor");
    Tier(4, 1, 95, AwardEffect, "Banner", "Bronze Banner of the Emperor");

    // 5. medical = skillTotal(Diagnosis) * skillTotal(First Aid)
    Definition(5, "medical", "Medical", Product);
    Component(5, 0, SkillTotal, skill["Diagnosis"]);
    Component(5, 1, SkillTotal, skill["First Aid"]);
    Factor(5, 0, 0.99, 1.21);
    Factor(5, 1, 1.17, 1.43);
    Tier(5, 1, 115, HistoryFlag, null, "Flagged for potential training as Apothecary");

    // 6. tech = skillTotal(Armory (Small Arms)) * skillTotal(Armory (Vehicle))
    Definition(6, "tech", "Tech", Product);
    Component(6, 0, SkillTotal, skill["Armory (Small Arms)"]);
    Component(6, 1, SkillTotal, skill["Armory (Vehicle)"]);
    Factor(6, 0, 1.17, 1.43);
    Factor(6, 1, 1.17, 1.43);
    Tier(6, 1, 80, HistoryFlag, null, "Flagged for potential training as Techmarine");

    // 7. piety = skillTotal(Theology (Emperor of Man))
    Definition(7, "piety", "Piety", Product);
    Component(7, 0, SkillTotal, skill["Theology (Emperor of Man)"]);
    Factor(7, 0, 0.108, 0.132);
    Tier(7, 1, 50, HistoryFlag, null, "Awarded Devout badge and declared a Novice");

    transaction.Commit();
    Console.WriteLine("Rating definitions migration complete.");
}

static void MigratePlanetScales(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // The Population and CarryingCapacity columns describe a log-normal distribution sitting
    // on a hard floor (value = Floor + 10^z * Scale), not a normal distribution. Rename the
    // misleading *Base / *StandardDeviation columns to *Floor / *Scale to match the model
    // (LogNormalValueTemplate), adding them fresh on a clean DB.
    RenameOrAddColumn(connection, transaction, "PlanetTemplate", "PopulationBase", "PopulationFloor", "BIGINT NOT NULL DEFAULT 0");
    RenameOrAddColumn(connection, transaction, "PlanetTemplate", "PopulationStandardDeviation", "PopulationScale", "REAL NOT NULL DEFAULT 0");
    RenameOrAddColumn(connection, transaction, "PlanetTemplate", "CarryingCapacityBase", "CarryingCapacityFloor", "BIGINT NOT NULL DEFAULT 0");
    RenameOrAddColumn(connection, transaction, "PlanetTemplate", "CarryingCapacityStandardDeviation", "CarryingCapacityScale", "REAL NOT NULL DEFAULT 0");

    // Canon-grounded per-type values (raw headcount). Floor = minimum, Scale = median of the
    // variable part; typical world = Floor + Scale, with a long upward tail. Carrying capacity
    // is the population scale multiplied by a per-type headroom so dense biomes start near full
    // and sparse ones have room to grow. See PRD Strategic Layer Phase 2.
    (string Name, long PopFloor, double PopScale, double Headroom)[] types =
    [
        ("Hive",      50_000_000_000L, 150_000_000_000d, 1.3),  // 5-20 hives x 10-100B; overcrowded
        ("Forge",      1_000_000_000L,   2_000_000_000d, 1.1),  // billions; life-support limited
        ("Civilised",    500_000_000L,   1_500_000_000d, 1.5),  // Earth-like; room to develop
        ("Agri",          50_000_000L,     150_000_000d, 4.0),  // low density; abundant farmland
        ("Feudal",        10_000_000L,      40_000_000d, 3.0),  // pre-industrial; land-limited
        ("Feral",            200_000L,         800_000d, 5.0),  // tribal; vast unused wilderness
        ("Death",             10_000L,         300_000d, 1.2),  // hostile; near max sustainable
    ];

    foreach ((string name, long popFloor, double popScale, double headroom) in types)
    {
        Execute(connection, transaction,
            @"UPDATE PlanetTemplate
              SET PopulationFloor = $pf,
                  PopulationScale = $ps,
                  CarryingCapacityFloor = CAST($pf * $h AS INTEGER),
                  CarryingCapacityScale = $ps * $h
              WHERE Name = $name",
            ("$pf", popFloor), ("$ps", popScale), ("$h", headroom), ("$name", name));
    }

    transaction.Commit();
    Console.WriteLine("Planet population/capacity scale migration complete.");
}

static void MigrateFortificationSkill(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // New combat-engineering skill used to build regional fortifications (PRD Strategic Layer
    // Phase 2 player-constructable defenses). Mirrors the existing "Engineering (Cybernetics)"
    // entry: Tech category (7), Intelligence attribute (4), difficulty 2.
    const string skillName = "Engineering (Fortification)";
    int skillId = ExecuteScalarInt(connection, transaction, "SELECT Id FROM BaseSkill WHERE Name = $n", ("$n", skillName));
    if (skillId == 0)
    {
        skillId = ExecuteScalarInt(connection, transaction, "SELECT COALESCE(MAX(Id), 0) + 1 FROM BaseSkill");
        Execute(connection, transaction,
            "INSERT INTO BaseSkill (Id, Name, SkillCategory, Attribute, Difficulty) VALUES ($id, $n, 7, 4, 2)",
            ("$id", skillId), ("$n", skillName));
    }

    // Every combat marine trains it at a low weight so any squad can fortify, slowly.
    string[] combatProfiles =
    [
        "veteran_work", "tactical_marine_work", "tactical_sergeant_work",
        "assault_marine_work", "assault_sergeant_work",
        "devastator_marine_work", "devastator_sergeant_work",
        "scout_marine_work", "scout_sergeant_work"
    ];

    foreach (string profileName in combatProfiles)
    {
        int profileId = ExecuteScalarInt(connection, transaction, "SELECT Id FROM TrainingProfile WHERE Name = $n", ("$n", profileName));
        if (profileId == 0)
        {
            continue;
        }
        // TargetType 1 = skill (matches the training-profile migration). Idempotent insert.
        int existing = ExecuteScalarInt(connection, transaction,
            "SELECT COUNT(*) FROM TrainingProfileEntry WHERE TrainingProfileId = $p AND TargetType = 1 AND TargetId = $s",
            ("$p", profileId), ("$s", skillId));
        if (existing == 0)
        {
            Execute(connection, transaction,
                "INSERT INTO TrainingProfileEntry (TrainingProfileId, TargetType, TargetId, Weight) VALUES ($p, 1, $s, 1)",
                ("$p", profileId), ("$s", skillId));
        }
    }

    transaction.Commit();
    Console.WriteLine($"Fortification skill migration complete (skill id {skillId}).");
}

static void MigrateTyranids(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // Idempotency guard: bail if the Lictor species already exists.
    int existing = ExecuteScalarInt(connection, transaction, "SELECT COUNT(*) FROM Species WHERE Name = 'Lictor'");
    if (existing > 0)
    {
        Console.WriteLine("Tyranid migration already applied; nothing to do.");
        return;
    }

    const int Tyranids = 2;        // Faction.Id
    const int TyranidBody = 1;     // BodyType shared by Warriors/gaunts/Genestealers

    // Two attribute values the existing AttributeTemplate table doesn't cover.
    // BaseValue / StandardDeviation follow the ~10% spread the other rows use.
    int dexTpl = ExecuteScalarInt(connection, transaction, "SELECT COALESCE(MAX(Id), 0) + 1 FROM AttributeTemplate");
    Execute(connection, transaction,
        "INSERT INTO AttributeTemplate (Id, BaseValue, StandardDeviation) VALUES ($id, 18.0, 1.8)", ("$id", dexTpl)); // Lictor Dex (BS3-ish; WS6 carried by Melee skill)
    int conTpl = dexTpl + 1;
    Execute(connection, transaction,
        "INSERT INTO AttributeTemplate (Id, BaseValue, StandardDeviation) VALUES ($id, 80.0, 8.0)", ("$id", conTpl)); // Ravener Con (T x W x 4 = 80)

    // Existing AttributeTemplate Ids referenced below (value in comment):
    // 21=24  25=120  20=20  19=16  16=12  14=10  10=8  2=0(psychic)
    // 27=40  24=60(atkspd)  11=8(move)  7=3.06(size)  6=2.6(size)
    int lictorSpecies = ExecuteScalarInt(connection, transaction, "SELECT COALESCE(MAX(Id), 0) + 1 FROM Species");
    InsertSpecies(connection, transaction, lictorSpecies, Tyranids, TyranidBody, "Lictor",
        str: 21, dex: dexTpl, con: 25, intel: 16, per: 20, ego: 20, cha: 16, psy: 2,
        atkSpd: 24, moveSpd: 11, size: 7, width: 1, depth: 1);
    int ravenerSpecies = lictorSpecies + 1;
    InsertSpecies(connection, transaction, ravenerSpecies, Tyranids, TyranidBody, "Ravener",
        str: 20, dex: 19, con: conTpl, intel: 10, per: 16, ego: 10, cha: 10, psy: 2,
        atkSpd: 27, moveSpd: 16, size: 6, width: 1, depth: 2);

    // SoldierTemplates. Rank slots them between Genestealer (20) and Warrior (50):
    // the Lictor as an elite vanguard, the Ravener as fast attack.
    int lictorSoldier = ExecuteScalarInt(connection, transaction, "SELECT COALESCE(MAX(Id), 0) + 1 FROM SoldierTemplate");
    InsertSoldierTemplate(connection, transaction, lictorSoldier, Tyranids, lictorSpecies, "Lictor", rank: 45);
    int ravenerSoldier = lictorSoldier + 1;
    InsertSoldierTemplate(connection, transaction, ravenerSoldier, Tyranids, ravenerSpecies, "Ravener", rank: 30);

    // Skill training. 47 = Generic Melee, 48 = Generic Ranged, 11 = Stealth.
    // Generic Melee is tiered in the existing data (16 = major threat, 8 = medium).
    const int GenericMelee = 47, GenericRanged = 48, Stealth = 11;
    InsertMosTraining(connection, transaction, lictorSoldier, GenericMelee, 16);   // top-tier melee; WS6 lives here, not in Dex
    InsertMosTraining(connection, transaction, lictorSoldier, GenericRanged, 1);   // flesh hooks
    InsertMosTraining(connection, transaction, lictorSoldier, Stealth, 8);         // chameleonic ambush signature
    InsertMosTraining(connection, transaction, ravenerSoldier, GenericMelee, 8);   // medium melee threat, like a Genestealer
    InsertMosTraining(connection, transaction, ravenerSoldier, GenericRanged, 1);

    transaction.Commit();
    Console.WriteLine($"Tyranid migration complete. Added Lictor (species {lictorSpecies}, soldier {lictorSoldier}) and Ravener (species {ravenerSpecies}, soldier {ravenerSoldier}).");
}

static void MigrateTyranidSquads(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    int existing = ExecuteScalarInt(connection, transaction, "SELECT COUNT(*) FROM SquadTemplate WHERE Name = 'Lictor'");
    if (existing > 0)
    {
        Console.WriteLine("Tyranid squad migration already applied; nothing to do.");
        return;
    }

    // SoldierTemplate Ids created by migrate-tyranids (look up by name so this stays
    // independent of the exact ids that pass produced).
    int lictorSoldier = ExecuteScalarInt(connection, transaction, "SELECT Id FROM SoldierTemplate WHERE Name = 'Lictor' AND FactionId = 2");
    int ravenerSoldier = ExecuteScalarInt(connection, transaction, "SELECT Id FROM SoldierTemplate WHERE Name = 'Ravener' AND FactionId = 2");
    if (lictorSoldier == 0 || ravenerSoldier == 0)
    {
        throw new InvalidOperationException("Run migrate-tyranids first; Lictor/Ravener SoldierTemplates are missing.");
    }

    const int Tyranids = 2;
    // SquadTypes flags: Scout = 0x2, Elite = 0x4.
    const int Scout = 0x2, Elite = 0x4;
    // Existing Tyranid rules data referenced below:
    const int Chitin15mm = 5;       // ArmorTemplate Id
    const int ScythingTalons = 17;  // WeaponSet Id (melee-only)

    // Lictor: solo elite ambusher. Scout (infiltrates like a Genestealer) + Elite ("Veteran"
    // slot). Tougher chitin than the Genestealer to match its T5 statline.
    int lictorSquad = ExecuteScalarInt(connection, transaction, "SELECT COALESCE(MAX(Id), 0) + 1 FROM SquadTemplate");
    InsertSquadTemplate(connection, transaction, lictorSquad, Tyranids, "Lictor",
        defaultArmorId: Chitin15mm, defaultWeaponSetId: ScythingTalons,
        squadType: Scout | Elite, battleValue: 75);
    InsertSquadElement(connection, transaction, lictorSquad, lictorSoldier, min: 1, max: 1);

    // Ravener: fast-attack pack of 5, no separate leader statline. Scout.
    int ravenerSquad = lictorSquad + 1;
    InsertSquadTemplate(connection, transaction, ravenerSquad, Tyranids, "Ravener Pack",
        defaultArmorId: Chitin15mm, defaultWeaponSetId: ScythingTalons,
        squadType: Scout, battleValue: 225);
    InsertSquadElement(connection, transaction, ravenerSquad, ravenerSoldier, min: 5, max: 5);

    transaction.Commit();
    Console.WriteLine($"Tyranid squad migration complete. Added Lictor (squad {lictorSquad}, solo) and Ravener Pack (squad {ravenerSquad}, x5).");
}

static void MigrateEvasion(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // Defensive "harder to hit" levers plus engine-interpreted capability flags
    // (see Design/EvasionBurrowAndAmbush.md). Columns are APPENDED to the end of
    // the Species table because GetSpeciesByFactionId reads positionally; do not
    // reorder. Defaults of 0 mean every existing species (marines, etc.) is
    // unaffected until explicitly tuned.
    EnsureColumn(connection, transaction, "Species", "MeleeEvasion", "REAL NOT NULL DEFAULT 0");
    EnsureColumn(connection, transaction, "Species", "RangedEvasion", "REAL NOT NULL DEFAULT 0");
    EnsureColumn(connection, transaction, "Species", "Abilities", "INTEGER NOT NULL DEFAULT 0");

    // SpeciesAbilities.Burrow = 1 << 0.
    const int Burrow = 1;

    // Tuning. Scale reference: melee/ranged totals sit on the ~10-point roll scale,
    // so 2-3 is a meaningful dodge and 5+ is nearly untouchable. Marine baseline = 0.
    SetEvasion(connection, transaction, "Genestealer", meleeEvasion: 2, rangedEvasion: 2, abilities: 0);    // weaving close-combat horror
    SetEvasion(connection, transaction, "Lictor", meleeEvasion: 0, rangedEvasion: 3, abilities: 0);          // chameleonic — hard to shoot, melee carried by skill
    SetEvasion(connection, transaction, "Ravener", meleeEvasion: 2, rangedEvasion: 2, abilities: Burrow);    // serpentine, fast, tunnels

    transaction.Commit();
    Console.WriteLine("Evasion migration complete (columns ensured; Genestealer/Lictor/Ravener tuned).");
}

static void MigrateSquadCaps(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // A unit template used to enumerate every squad it must contain as a separate
    // UnitTemplateSquadTemplate row (multiplicity = row count), which forced the
    // chapter to be generated full of empty squads. Replace that with an explicit
    // cap: one row per (unit, squad) carrying MinCount/MaxCount. MinCount squads
    // are created eagerly (the chapter's command singletons); line squads get
    // MinCount 0 and are created on demand as soldiers arrive. See SquadTemplateSlot.
    EnsureColumn(connection, transaction, "UnitTemplateSquadTemplate", "MinCount", "INTEGER NOT NULL DEFAULT 0");
    EnsureColumn(connection, transaction, "UnitTemplateSquadTemplate", "MaxCount", "INTEGER NOT NULL DEFAULT 1");

    int duplicateGroups = ExecuteScalarInt(connection, transaction,
        @"SELECT COUNT(*) FROM (
            SELECT UnitTemplateId, SquadTemplateId FROM UnitTemplateSquadTemplate
            GROUP BY UnitTemplateId, SquadTemplateId HAVING COUNT(*) > 1)");
    if (duplicateGroups == 0)
    {
        Console.WriteLine("Squad caps already collapsed; nothing to do.");
        return;
    }

    // Snapshot the collapsed rows: one per (unit, squad). MaxCount = how many rows
    // existed; MinCount = MaxCount for a top-level unit's squads (the always-present
    // command squads), else 0 (line squads created on demand).
    List<(int UnitId, int SquadId, int Count, int IsTopLevel)> rows = [];
    using (SqliteCommand command = connection.CreateCommand())
    {
        command.Transaction = transaction;
        command.CommandText =
            @"SELECT j.UnitTemplateId, j.SquadTemplateId, COUNT(*) AS Cnt, ut.IsTopLevelUnit
              FROM UnitTemplateSquadTemplate j
              JOIN UnitTemplate ut ON ut.Id = j.UnitTemplateId
              GROUP BY j.UnitTemplateId, j.SquadTemplateId";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                      Convert.ToInt32(reader.GetValue(3))));
        }
    }

    Execute(connection, transaction, "DELETE FROM UnitTemplateSquadTemplate");
    int nextId = 1;
    foreach ((int unitId, int squadId, int count, int isTopLevel) in rows)
    {
        int maxCount = count;
        int minCount = isTopLevel != 0 ? count : 0;
        Execute(connection, transaction,
            @"INSERT INTO UnitTemplateSquadTemplate (Id, UnitTemplateId, SquadTemplateId, MinCount, MaxCount)
              VALUES ($id, $u, $s, $min, $max)",
            ("$id", nextId++), ("$u", unitId), ("$s", squadId), ("$min", minCount), ("$max", maxCount));
    }

    transaction.Commit();
    Console.WriteLine($"Squad caps migration complete. Collapsed to {rows.Count} (unit, squad) rows.");
}

static void MigrateVeteranSergeant(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    const int PlayerFaction = 1;

    // Idempotency guard.
    if (ExecuteScalarInt(connection, transaction,
        "SELECT COUNT(*) FROM SoldierTemplate WHERE Name = 'Veteran Sergeant' AND FactionId = 1") > 0)
    {
        Console.WriteLine("Veteran Sergeant migration already applied; nothing to do.");
        return;
    }

    // The line Veteran (Elite) Squad's leader element pointed at the Veteran Captain,
    // a company-command rank that belongs only to the Veteran HQ Squad. Because the
    // line squads are seeded with an ordinary sergeant, that left an unfilled Captain
    // slot in every First Company squad, which the transfer UI then offered as a
    // promotion target. Introduce a proper Veteran Sergeant rank to lead the line
    // veteran squads and repoint the squad's leader slot to it.
    int sergeantId = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Sergeant' AND FactionId = 1");
    if (sergeantId == 0)
    {
        throw new InvalidOperationException("Line 'Sergeant' soldier template is missing; cannot model the Veteran Sergeant on it.");
    }

    // Model the Veteran Sergeant on the line Sergeant: same species, rank-5 squad
    // leader, and the sergeant work-experience training profile.
    int speciesId = ExecuteScalarInt(connection, transaction,
        "SELECT SpeciesId FROM SoldierTemplate WHERE Id = $id", ("$id", sergeantId));
    int profileId = ExecuteScalarInt(connection, transaction,
        "SELECT WorkExperienceTrainingProfileId FROM SoldierTemplate WHERE Id = $id", ("$id", sergeantId));

    int veteranSergeant = ExecuteScalarInt(connection, transaction,
        "SELECT COALESCE(MAX(Id), 0) + 1 FROM SoldierTemplate");
    // SubRank 15 keeps it distinct from the existing rank-5 entries (1-14).
    // IsSquadLeader so Squad.SquadLeader recognizes it as the squad's leader.
    Execute(connection, transaction,
        @"INSERT INTO SoldierTemplate
            (Id, FactionId, SpeciesId, Name, Rank, SubRank, IsSquadLeader, SpecialistType, WorkExperienceTrainingProfileId)
          VALUES ($id, $f, $s, 'Veteran Sergeant', 5, 15, 1, 0, $p)",
        ("$id", veteranSergeant), ("$f", PlayerFaction), ("$s", speciesId),
        ("$p", profileId == 0 ? DBNull.Value : profileId));

    // Mirror the line Sergeant's MOS training (leadership/tactics emphasis).
    Execute(connection, transaction,
        @"INSERT INTO SoldierMosTraining (SoldierTemplateId, BaseSkillId, PointsAdded)
          SELECT $new, BaseSkillId, PointsAdded FROM SoldierMosTraining WHERE SoldierTemplateId = $old",
        ("$new", veteranSergeant), ("$old", sergeantId));

    // Repoint the line Veteran Squad's leader slot from Veteran Captain to the new rank.
    int veteranCaptain = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Veteran Captain' AND FactionId = 1");
    int updated = Execute(connection, transaction,
        @"UPDATE SquadTemplateElement
          SET SoldierTemplateId = $vs
          WHERE SoldierTemplateId = $vc
            AND SquadTemplateId IN (SELECT Id FROM SquadTemplate WHERE Name = 'Veteran Squad' AND FactionId = 1)",
        ("$vs", veteranSergeant), ("$vc", veteranCaptain));

    transaction.Commit();
    Console.WriteLine($"Veteran Sergeant migration complete. Added soldier {veteranSergeant}; repointed {updated} Veteran Squad leader element(s).");
}

static void MigrateCollapseSergeants(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // "Sergeant" is a single rank; the "Sergeant (A)" and "Sergeant (D)" variants only
    // existed to give assault and devastator squad leaders a different training emphasis.
    // What a sergeant trains toward is really a function of the squad he leads, not a
    // distinct soldier template, so we move that leadership/tactics-plus-combat-focus
    // training onto the squad template (LeaderWorkExperienceProfileId) and fold the two
    // variants back into the plain Sergeant.
    int assaultSergeant = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Sergeant (A)' AND FactionId = 1");
    int devastatorSergeant = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Sergeant (D)' AND FactionId = 1");
    if (assaultSergeant == 0 && devastatorSergeant == 0)
    {
        Console.WriteLine("Sergeant collapse already applied; nothing to do.");
        return;
    }

    int sergeant = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Sergeant' AND FactionId = 1");
    if (sergeant == 0)
    {
        throw new InvalidOperationException(
            "Line 'Sergeant' soldier template is missing; cannot collapse the assault/devastator variants onto it.");
    }

    EnsureColumn(connection, transaction, "SquadTemplate", "LeaderWorkExperienceProfileId", "INTEGER");

    // The three line-squad sergeant training profiles already exist (migrate-training);
    // attach each to the squad type whose leader should train that way.
    SetSquadLeaderProfile(connection, transaction, "Tactical Squad", "tactical_sergeant_work");
    SetSquadLeaderProfile(connection, transaction, "Assault Squad", "assault_sergeant_work");
    SetSquadLeaderProfile(connection, transaction, "Devastator Squad", "devastator_sergeant_work");

    // Repoint every squad leader slot pointing at a variant to the plain Sergeant, then
    // drop the variant and its MOS rows. The TrainingProfile rows themselves are kept:
    // they are now referenced by the squad templates' leader profiles.
    int repointed = 0;
    foreach (int variant in new[] { assaultSergeant, devastatorSergeant })
    {
        if (variant == 0)
        {
            continue;
        }
        repointed += Execute(connection, transaction,
            "UPDATE SquadTemplateElement SET SoldierTemplateId = $s WHERE SoldierTemplateId = $v",
            ("$s", sergeant), ("$v", variant));
        Execute(connection, transaction,
            "DELETE FROM SoldierMosTraining WHERE SoldierTemplateId = $v", ("$v", variant));
        Execute(connection, transaction,
            "DELETE FROM SoldierTemplate WHERE Id = $v", ("$v", variant));
    }

    transaction.Commit();
    Console.WriteLine(
        $"Sergeant collapse complete. Folded (A)/(D) variants into Sergeant {sergeant}; " +
        $"repointed {repointed} squad element(s); moved leader training profiles onto squad templates.");
}

static void SetSquadLeaderProfile(SqliteConnection connection, SqliteTransaction transaction,
                                  string squadName, string profileName)
{
    int profileId = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM TrainingProfile WHERE Name = $n", ("$n", profileName));
    if (profileId == 0)
    {
        throw new InvalidOperationException(
            $"Training profile '{profileName}' is missing; run migrate-training before migrate-collapse-sergeants.");
    }
    int updated = Execute(connection, transaction,
        "UPDATE SquadTemplate SET LeaderWorkExperienceProfileId = $p WHERE Name = $n AND FactionId = 1",
        ("$p", profileId), ("$n", squadName));
    if (updated == 0)
    {
        Console.WriteLine($"  warning: squad template '{squadName}' not found; leader profile not set.");
    }
}

// Canonically a Chaplain is seconded to a specific company, so give each company HQ
// squad a Chaplain slot and (in NewChapterBuilder) assign one Chaplain per company that
// has a Captain. Chaplaincy initiates who cannot yet fill a company slot are given the
// Judiciar rank (9th-edition Chaplains-in-training) and kept in the Reclusium, so we add
// a Judiciar soldier template and a Judiciar slot to the Reclusium alongside the Chaplains.
static void MigrateChaplaincy(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    int chaplain = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Chaplain' AND FactionId = 1");
    if (chaplain == 0)
    {
        throw new InvalidOperationException(
            "'Chaplain' soldier template is missing; cannot model the Judiciar on it.");
    }

    int judiciar = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Judiciar' AND FactionId = 1");
    if (judiciar != 0)
    {
        Console.WriteLine("Chaplaincy migration already applied; nothing to do.");
        return;
    }

    // Model the Judiciar on the Chaplain: same species and chaplaincy SpecialistType,
    // not a squad leader. Rank 4 / SubRank 0 keeps it just below the Chaplain (SubRank 2)
    // as the most junior chaplaincy rank, without renumbering the existing Chaplain.
    int speciesId = ExecuteScalarInt(connection, transaction,
        "SELECT SpeciesId FROM SoldierTemplate WHERE Id = $id", ("$id", chaplain));
    int specialistType = ExecuteScalarInt(connection, transaction,
        "SELECT SpecialistType FROM SoldierTemplate WHERE Id = $id", ("$id", chaplain));

    judiciar = ExecuteScalarInt(connection, transaction,
        "SELECT COALESCE(MAX(Id), 0) + 1 FROM SoldierTemplate");
    Execute(connection, transaction,
        @"INSERT INTO SoldierTemplate
            (Id, FactionId, SpeciesId, Name, Rank, SubRank, IsSquadLeader, SpecialistType, WorkExperienceTrainingProfileId)
          VALUES ($id, 1, $s, 'Judiciar', 4, 0, 0, $sp, NULL)",
        ("$id", judiciar), ("$s", speciesId), ("$sp", specialistType));

    // Mirror the Chaplain's MOS training (theology/ritual emphasis).
    Execute(connection, transaction,
        @"INSERT INTO SoldierMosTraining (SoldierTemplateId, BaseSkillId, PointsAdded)
          SELECT $new, BaseSkillId, PointsAdded FROM SoldierMosTraining WHERE SoldierTemplateId = $old",
        ("$new", judiciar), ("$old", chaplain));

    // Give every company HQ squad a single optional Chaplain slot.
    string[] companyHqSquads = { "Veteran HQ Squad", "HQ Squad", "Scout HQ Squad" };
    foreach (string squadName in companyHqSquads)
    {
        int squadId = ExecuteScalarInt(connection, transaction,
            "SELECT Id FROM SquadTemplate WHERE Name = $n AND FactionId = 1", ("$n", squadName));
        if (squadId == 0)
        {
            Console.WriteLine($"  warning: squad template '{squadName}' not found; no Chaplain slot added.");
            continue;
        }
        EnsureSquadElement(connection, transaction, squadId, chaplain, 0, 1);
    }

    // The Reclusium keeps its Chaplain slots and gains room for Judiciars in training.
    int reclusium = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SquadTemplate WHERE Name = 'Reclusium' AND FactionId = 1");
    if (reclusium != 0)
    {
        EnsureSquadElement(connection, transaction, reclusium, judiciar, 0, 50);
    }

    transaction.Commit();
    Console.WriteLine(
        $"Chaplaincy migration complete. Added Judiciar soldier {judiciar}; " +
        "added Chaplain slots to company HQ squads and a Judiciar slot to the Reclusium.");
}

// A Judiciar understudies the company Chaplain, so give each company HQ squad its own
// Judiciar slot as well (NewChapterBuilder fills it only when the company has a Captain).
// Only Judiciars beyond these company slots fall back to the Reclusium.
static void MigrateCompanyJudiciar(SqliteConnection connection)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    int judiciar = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Judiciar' AND FactionId = 1");
    if (judiciar == 0)
    {
        throw new InvalidOperationException(
            "'Judiciar' soldier template is missing; run migrate-chaplaincy before migrate-company-judiciar.");
    }

    string[] companyHqSquads = { "Veteran HQ Squad", "HQ Squad", "Scout HQ Squad" };
    foreach (string squadName in companyHqSquads)
    {
        int squadId = ExecuteScalarInt(connection, transaction,
            "SELECT Id FROM SquadTemplate WHERE Name = $n AND FactionId = 1", ("$n", squadName));
        if (squadId == 0)
        {
            Console.WriteLine($"  warning: squad template '{squadName}' not found; no Judiciar slot added.");
            continue;
        }
        EnsureSquadElement(connection, transaction, squadId, judiciar, 0, 1);
    }

    transaction.Commit();
    Console.WriteLine("Company Judiciar migration complete. Added a Judiciar slot to each company HQ squad.");
}

// Inserts a squad element only if the (squad, soldier) pairing does not already exist,
// so the migration is safe to re-run.
static void EnsureSquadElement(SqliteConnection connection, SqliteTransaction transaction,
                               int squadTemplateId, int soldierTemplateId, int min, int max)
{
    int existing = ExecuteScalarInt(connection, transaction,
        @"SELECT COUNT(*) FROM SquadTemplateElement
          WHERE SquadTemplateId = $sq AND SoldierTemplateId = $so",
        ("$sq", squadTemplateId), ("$so", soldierTemplateId));
    if (existing > 0)
    {
        return;
    }
    InsertSquadElement(connection, transaction, squadTemplateId, soldierTemplateId, min, max);
}

static void MigrateRemoveVeteranCaptain(SqliteConnection connection) =>
    RemoveCaptainVariant(connection, "Veteran Captain");

static void MigrateRemoveRecruitmentCaptain(SqliteConnection connection) =>
    RemoveCaptainVariant(connection, "Recruitment Captain");

static void RemoveCaptainVariant(SqliteConnection connection, string variantName)
{
    using SqliteTransaction transaction = connection.BeginTransaction();

    // Every company HQ is led by a plain Captain; the per-company captain variants
    // (Veteran Captain for the Veteran Company, Recruitment Captain for the Scout
    // Company) are redundant. Repoint any squad leader slot still pointing at the
    // variant to the plain Captain and drop the template. A leftover variant slot also
    // caused the transfer UI to offer an "opening" in an HQ squad that already had a
    // Captain, because the slot's specific template read as unfilled.
    int variant = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = $n AND FactionId = 1", ("$n", variantName));
    if (variant == 0)
    {
        Console.WriteLine($"{variantName} already removed; nothing to do.");
        return;
    }

    int captain = ExecuteScalarInt(connection, transaction,
        "SELECT Id FROM SoldierTemplate WHERE Name = 'Captain' AND FactionId = 1");
    if (captain == 0)
    {
        throw new InvalidOperationException($"'Captain' soldier template is missing; cannot retarget {variantName} references.");
    }

    int repointed = Execute(connection, transaction,
        "UPDATE SquadTemplateElement SET SoldierTemplateId = $c WHERE SoldierTemplateId = $v",
        ("$c", captain), ("$v", variant));
    Execute(connection, transaction,
        "DELETE FROM SoldierMosTraining WHERE SoldierTemplateId = $v", ("$v", variant));
    Execute(connection, transaction,
        "DELETE FROM SoldierTemplate WHERE Id = $v", ("$v", variant));

    transaction.Commit();
    Console.WriteLine($"Removed {variantName} (soldier {variant}); repointed {repointed} squad element(s) to Captain {captain}.");
}

static void SetEvasion(SqliteConnection connection, SqliteTransaction transaction, string speciesName,
                       double meleeEvasion, double rangedEvasion, int abilities)
{
    int exists = ExecuteScalarInt(connection, transaction,
        "SELECT COUNT(*) FROM Species WHERE Name = $n", ("$n", speciesName));
    if (exists == 0)
    {
        Console.WriteLine($"  warning: species '{speciesName}' not found; skipped.");
        return;
    }
    Execute(connection, transaction,
        "UPDATE Species SET MeleeEvasion = $m, RangedEvasion = $r, Abilities = $a WHERE Name = $n",
        ("$m", meleeEvasion), ("$r", rangedEvasion), ("$a", abilities), ("$n", speciesName));
}

static void InsertSquadTemplate(SqliteConnection connection, SqliteTransaction transaction, int id, int factionId,
                                string name, int defaultArmorId, int defaultWeaponSetId, int squadType, int battleValue)
{
    Execute(connection, transaction,
        @"INSERT INTO SquadTemplate
            (Id, FactionId, Name, DefaultArmorId, DefaultWeaponSetId, SquadType, BattleValue, BodyguardSquadTemplateId)
          VALUES ($id, $f, $n, $a, $w, $t, $bv, NULL)",
        ("$id", id), ("$f", factionId), ("$n", name), ("$a", defaultArmorId),
        ("$w", defaultWeaponSetId), ("$t", squadType), ("$bv", battleValue));
}

static void InsertSquadElement(SqliteConnection connection, SqliteTransaction transaction, int squadTemplateId,
                               int soldierTemplateId, int min, int max)
{
    int id = ExecuteScalarInt(connection, transaction, "SELECT COALESCE(MAX(Id), 0) + 1 FROM SquadTemplateElement");
    Execute(connection, transaction,
        @"INSERT INTO SquadTemplateElement (Id, SquadTemplateId, SoldierTemplateId, MinimumRequired, MaximumAllowed)
          VALUES ($id, $sq, $so, $min, $max)",
        ("$id", id), ("$sq", squadTemplateId), ("$so", soldierTemplateId), ("$min", min), ("$max", max));
}

static void InsertSpecies(SqliteConnection connection, SqliteTransaction transaction, int id, int factionId, int bodyTypeId,
                          string name, int str, int dex, int con, int intel, int per, int ego, int cha, int psy,
                          int atkSpd, int moveSpd, int size, int width, int depth)
{
    Execute(connection, transaction,
        @"INSERT INTO Species
            (Id, FactionId, BodyTypeId, Name, StrengthTemplateId, DexterityTemplateId, ConstitutionTemplateId,
             IntelligenceTemplateId, PerceptionTemplateId, EgoTemplateId, CharismaTemplateId, PsychicTemplateId,
             AttackSpeedTemplateId, MoveSpeedTemplateId, SizeTemplateId, Width, Depth)
          VALUES ($id, $f, $b, $n, $str, $dex, $con, $int, $per, $ego, $cha, $psy, $atk, $move, $size, $w, $d)",
        ("$id", id), ("$f", factionId), ("$b", bodyTypeId), ("$n", name),
        ("$str", str), ("$dex", dex), ("$con", con), ("$int", intel), ("$per", per), ("$ego", ego),
        ("$cha", cha), ("$psy", psy), ("$atk", atkSpd), ("$move", moveSpd), ("$size", size), ("$w", width), ("$d", depth));
}

static void InsertSoldierTemplate(SqliteConnection connection, SqliteTransaction transaction, int id, int factionId,
                                  int speciesId, string name, int rank)
{
    Execute(connection, transaction,
        @"INSERT INTO SoldierTemplate
            (Id, FactionId, SpeciesId, Name, Rank, SubRank, IsSquadLeader, SpecialistType, WorkExperienceTrainingProfileId)
          VALUES ($id, $f, $s, $n, $rank, 1, 0, 0, NULL)",
        ("$id", id), ("$f", factionId), ("$s", speciesId), ("$n", name), ("$rank", rank));
}

static void InsertMosTraining(SqliteConnection connection, SqliteTransaction transaction, int soldierTemplateId, int baseSkillId, double points)
{
    Execute(connection, transaction,
        "INSERT INTO SoldierMosTraining (SoldierTemplateId, BaseSkillId, PointsAdded) VALUES ($s, $sk, $p)",
        ("$s", soldierTemplateId), ("$sk", baseSkillId), ("$p", points));
}

static void RenameOrAddColumn(SqliteConnection connection, SqliteTransaction transaction,
                              string table, string oldName, string newName, string addDefinition)
{
    if (ColumnExists(connection, transaction, table, newName))
    {
        return;
    }
    if (ColumnExists(connection, transaction, table, oldName))
    {
        Execute(connection, transaction, $"ALTER TABLE {table} RENAME COLUMN {oldName} TO {newName}");
    }
    else
    {
        Execute(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {newName} {addDefinition}");
    }
}

static bool ColumnExists(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
{
    using SqliteCommand pragma = connection.CreateCommand();
    pragma.Transaction = transaction;
    pragma.CommandText = $"PRAGMA table_info({table})";
    using SqliteDataReader reader = pragma.ExecuteReader();
    while (reader.Read())
    {
        if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }
    return false;
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

static int Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
{
    using SqliteCommand command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach ((string name, object value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    return command.ExecuteNonQuery();
}

internal readonly record struct TrainingEntry(int TargetType, int TargetId, float Weight);

internal enum OnlyWarAttribute
{
    Strength = 1,
    Dexterity = 2,
    Constitution = 3
}
