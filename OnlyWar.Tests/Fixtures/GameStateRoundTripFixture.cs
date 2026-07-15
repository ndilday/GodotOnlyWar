using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models;
using OnlyWar.Models.Units;

namespace OnlyWar.Tests.Fixtures;

internal sealed class GameStateRoundTripFixture
{
    private readonly GameRulesData _data;
    private readonly Date _date;

    public GameStateRoundTripFixture(GameRulesData data, Date date)
    {
        _data = data;
        _date = date;
    }

    public string SchemaPath => Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql");

    public List<Unit> CurrentUnits => _data.Factions.SelectMany(f => f.Units).ToList();

    public static string CreateTempDbPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.s3db");
    }

    public static void CleanupDb(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file; ignore if still locked.
        }
    }

    public static long CountRows(string dbPath, string table)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath };
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void RegisterPlayerArmy(Sector sector)
    {
        Unit armyRoot = sector.PlayerForce.Army.OrderOfBattle;
        if (!_data.PlayerFaction.Units.Contains(armyRoot))
        {
            _data.PlayerFaction.Units.Add(armyRoot);
        }
    }

    public void Save(Sector sector, string dbPath, IEnumerable<Unit> units)
    {
        Save(sector, dbPath, units, SchemaPath);
    }

    public void Save(Sector sector, string dbPath, IEnumerable<Unit> units, string schemaPath)
    {
        GameStateDataAccess.Instance.SaveData(
            dbPath,
            _date,
            sector.PlayerForce.Army.Requisition,
            sector.PlayerForce.GeneseedStockpile,
            sector.PlayerForce.GeneseedPurity,
            sector.Scenario,
            sector.PlayerForce.Army.MedicalProcedures,
            sector.Characters,
            sector.PlayerForce.Requests,
            sector.PlayerForce.Pledges,
            sector.Planets.Values,
            sector.Fleets.Values,
            units,
            sector.PlayerForce.Army.PlayerSoldierMap.Values,
            sector.PlayerForce.Army.FallenBrothers.Values,
            sector.PlayerForce.BattleHistory,
            schemaPath);
    }

    public GameStateDataBlob Load(string dbPath)
    {
        var shipTemplateMap = _data.Factions.Where(f => f.ShipTemplates != null)
            .SelectMany(f => f.ShipTemplates.Values).ToDictionary(s => s.Id);
        var unitTemplateMap = _data.Factions.Where(f => f.UnitTemplates != null)
            .SelectMany(f => f.UnitTemplates.Values).ToDictionary(u => u.Id);
        var squadTemplateMap = _data.Factions.Where(f => f.SquadTemplates != null)
            .SelectMany(f => f.SquadTemplates.Values).ToDictionary(s => s.Id);
        var hitLocations = _data.BodyHitLocationTemplateMap.Values.SelectMany(hl => hl)
            .Distinct().ToDictionary(hl => hl.Id);
        var soldierTypeMap = _data.Factions.Where(f => f.SoldierTemplates != null)
            .SelectMany(f => f.SoldierTemplates.Values).ToDictionary(st => st.Id);

        return GameStateDataAccess.Instance.GetData(
            dbPath,
            _data.Factions.ToDictionary(f => f.Id),
            _data.PlanetTemplateMap,
            shipTemplateMap,
            unitTemplateMap,
            squadTemplateMap,
            _data.WeaponSets,
            hitLocations,
            _data.BaseSkillMap,
            soldierTypeMap);
    }
}
