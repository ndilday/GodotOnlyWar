using System.Collections.Generic;
using System.Drawing;
using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;
using Xunit;

namespace OnlyWar.Tests.Data;

// Regression coverage for TDD 8.1: special missions were inserted twice on save
// (once inside SavePlanetRegions, once in SaveMissions), so a region with one
// special mission would either reload with two rows or fail the Mission primary
// key. These tests drive PlanetDataAccess.SavePlanet against a freshly created
// save schema and assert the Mission table contents directly.
public class MissionSaveTests
{
    [Fact]
    public void SavePlanet_RegionWithOneSpecialMission_PersistsExactlyOneMissionRow()
    {
        Faction faction = CreateFaction();
        Planet planet = CreatePlanet(faction);
        RegionFaction targetFaction = planet.Regions[0].RegionFactionMap[faction.Id];
        planet.Regions[0].SpecialMissions.Add(
            new SabotageMission(42, DefenseType.Detection, 3, targetFaction));

        using SqliteConnection connection = CreateSaveDatabase();
        SavePlanet(connection, planet);

        Assert.Equal(1, CountRows(connection, "Mission"));
    }

    [Fact]
    public void SavePlanet_SabotageMission_RoundTripsMissionFields()
    {
        Faction faction = CreateFaction();
        Planet planet = CreatePlanet(faction);
        RegionFaction targetFaction = planet.Regions[0].RegionFactionMap[faction.Id];
        planet.Regions[0].SpecialMissions.Add(
            new SabotageMission(42, DefenseType.Detection, 3, targetFaction));

        using SqliteConnection connection = CreateSaveDatabase();
        SavePlanet(connection, planet);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, MissionType, RegionId, FactionId, MissionSize, DefenseTypeId FROM Mission";
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(42, reader.GetInt32(0));
        Assert.Equal((int)MissionType.Sabotage, reader.GetInt32(1));
        Assert.Equal(planet.Regions[0].Id, reader.GetInt32(2));
        Assert.Equal(faction.Id, reader.GetInt32(3));
        Assert.Equal(3, reader.GetInt32(4));
        Assert.Equal((int)DefenseType.Detection, reader.GetInt32(5));
        Assert.False(reader.Read());
    }

    [Fact]
    public void SavePlanet_NonSabotageMission_PersistsNullDefenseType()
    {
        Faction faction = CreateFaction();
        Planet planet = CreatePlanet(faction);
        RegionFaction targetFaction = planet.Regions[0].RegionFactionMap[faction.Id];
        planet.Regions[0].SpecialMissions.Add(
            new Mission(7, MissionType.Ambush, targetFaction, 2));

        using SqliteConnection connection = CreateSaveDatabase();
        SavePlanet(connection, planet);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DefenseTypeId FROM Mission WHERE Id = 7";
        Assert.Equal(System.DBNull.Value, command.ExecuteScalar());
    }

    private static void SavePlanet(SqliteConnection connection, Planet planet)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        new PlanetDataAccess().SavePlanet(transaction, planet);
        transaction.Commit();
    }

    private static int CountRows(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return System.Convert.ToInt32(command.ExecuteScalar());
    }

    // Creates only the tables SavePlanet writes to, copied from Database/SaveStructure.sql.
    // The full schema script is not run because it also creates an OrderSquad table whose
    // foreign key references the reserved word "Order", which Microsoft.Data.Sqlite rejects.
    // Mission's FactionId is declared as a plain INTEGER here (the production schema points it
    // at a Faction table that lives only in the read-only rules DB, not the save DB).
    private static SqliteConnection CreateSaveDatabase()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE Planet (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, PlanetTemplateId INTEGER NOT NULL,
                Name STRING NOT NULL UNIQUE, x INTEGER NOT NULL, y INTEGER NOT NULL, Importance INTEGER NOT NULL,
                TaxLevel INTEGER NOT NULL);
            CREATE TABLE PlanetFaction (PlanetId INTEGER NOT NULL, FactionId INTEGER NOT NULL, IsPublic BOOLEAN NOT NULL,
                PlanetaryControl INTEGER NOT NULL, PlayerReputation REAL NOT NULL, LeaderId INTEGER);
            CREATE TABLE Region (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, PlanetId INTEGER NOT NULL, RegionNumber INTEGER NOT NULL,
                RegionName STRING NOT NULL, RegionType INTEGER NOT NULL, IsUnderAssault BOOLEAN NOT NULL, IntelligenceLevel REAL NOT NULL,
                CarryingCapacity BIGINT NOT NULL);
            CREATE TABLE RegionFaction (RegionId INTEGER NOT NULL, FactionId INTEGER NOT NULL, IsPublic BOOLEAN NOT NULL,
                Population BIGINT NOT NULL, Garrison INTEGER NOT NULL, Organization INTEGER NOT NULL, Entrenchment INTEGER NOT NULL,
                Detection INTEGER NOT NULL, AntiAir INTEGER NOT NULL);
            CREATE TABLE Mission (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, MissionType INTEGER NOT NULL, RegionId INTEGER NOT NULL,
                FactionId INTEGER NOT NULL, MissionSize INTEGER NOT NULL, DefenseTypeId INTEGER, IsRegionMission BOOLEAN NOT NULL);";
        command.ExecuteNonQuery();
        return connection;
    }

    private static Faction CreateFaction()
    {
        return new Faction(
            1, "Mission Save Test Faction", Color.Red, false, false, false,
            GrowthType.None,
            new Dictionary<int, Species>(),
            new Dictionary<int, SoldierTemplate>(),
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, UnitTemplate>(),
            new Dictionary<int, BoatTemplate>(),
            new Dictionary<int, ShipTemplate>(),
            new Dictionary<int, FleetTemplate>());
    }

    private static Planet CreatePlanet(Faction faction)
    {
        PlanetTemplate template = new(1, "Test World", 0, null, null, null, null);
        Planet planet = new(1, "Testus Prime", new Coordinate(10, 20), 16, template, 5, 1);
        PlanetFaction planetFaction = new(faction) { IsPublic = true };
        planet.PlanetFactionMap[faction.Id] = planetFaction;
        // The Regions array is fixed-size; SavePlanetRegions indexes every slot, so
        // all of them must be populated.
        for (int i = 0; i < planet.Regions.Length; i++)
        {
            Region region = new(100 + i, planet, 0, $"Region {i}",
                RegionExtensions.GetCoordinatesFromRegionNumber(i), 0);
            RegionFaction regionFaction = new(planetFaction, region)
            {
                Population = 1000,
                IsPublic = true
            };
            region.RegionFactionMap[faction.Id] = regionFaction;
            planet.Regions[i] = region;
        }
        return planet;
    }
}
