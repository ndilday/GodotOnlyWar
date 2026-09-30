using Microsoft.Data.Sqlite;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Planets;
using OnlyWar.Persistence.Database.GameState;
using OnlyWar.Tests.Fixtures;
using System.IO;
using Xunit;

namespace OnlyWar.Tests.Data;

/// <summary>
/// Mars pipeline (TDD §6.14): the Mechanicus loan flag lives on the
/// single GlobalData row.
/// </summary>
public sealed class MechanicusLoanPersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GlobalData_RoundTripsTheMechanicusLoanFlag(bool loanActive)
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("mechanicus_loan");
        try
        {
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                new GlobalDataAccess().SaveGlobalData(
                    transaction,
                    new Date(42, 100, 10),
                    requisition: 0,
                    geneseedStockpile: 0,
                    geneseedPurity: 1f,
                    scenario: null,
                    homeWorldPlanetId: null,
                    mechanicusLoanActive: loanActive);
                transaction.Commit();
            }

            GlobalState global = new GlobalDataAccess().GetGlobalData(connection);

            Assert.Equal(loanActive, global.IsMechanicusLoanActive);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    // Phase 5: the standing destination for brothers returning from Mars is a ship, a region, or
    // neither (the default), stored as raw ids and resolved by the load path.
    [Theory]
    [InlineData("ship")]
    [InlineData("region")]
    [InlineData("none")]
    public void GlobalData_RoundTripsTheMarsReturnDestination(string kind)
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("mars_return");
        try
        {
            SectorSimulationFixture world = SectorSimulationFixture.CreateDetached();
            Ship ship = new(41, "Emperor's Wrath", new ShipTemplate(1, "Strike Cruiser", 200, 0, 0));
            Region region = world.Planet.Regions[7];
            CampaignLocation destination = kind switch
            {
                "ship" => CampaignLocation.Aboard(ship),
                "region" => CampaignLocation.Landed(region),
                _ => null
            };
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                new GlobalDataAccess().SaveGlobalData(
                    transaction,
                    new Date(42, 100, 10),
                    requisition: 0,
                    geneseedStockpile: 0,
                    geneseedPurity: 1f,
                    scenario: null,
                    homeWorldPlanetId: null,
                    marsReturnDestination: destination);
                transaction.Commit();
            }

            GlobalState global = new GlobalDataAccess().GetGlobalData(connection);

            Assert.Equal(kind == "ship" ? ship.Id : (int?)null, global.MarsReturnShipId);
            Assert.Equal(kind == "region" ? region.Id : (int?)null, global.MarsReturnRegionId);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    [Fact]
    public void Schema_RejectsAMarsReturnDestinationThatIsBothAShipAndARegion()
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("mars_return_check");
        try
        {
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO GlobalData
                (Millenium, Year, Week, SaveVersion, MarsReturnShipId, MarsReturnRegionId)
                VALUES (42, 100, 10, 22, 41, 7)";

            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    private static SqliteConnection OpenDatabase(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = false,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(
            Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql"));
        command.ExecuteNonQuery();
    }
}
