using Microsoft.Data.Sqlite;
using OnlyWar.Domain;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Persistence.Database.GameState;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace OnlyWar.Tests.Data;

public sealed class IndividualPostingPersistenceTests
{
    [Fact]
    public void MechanicusPosting_RoundTripsOffSectorLocationAndExpectedReturnDate()
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("posting_mechanicus");
        try
        {
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            PlayerSoldier soldier = new(TestModelFactory.CreateSoldier(name: "Brother Adept"), "Brother Adept")
            {
                IndividualPosting = new IndividualPosting(
                    IndividualPostingPurpose.Mechanicus,
                    CampaignLocation.OffSector,
                    new Date(41, 998, 1),
                    new Date(42, 18, 1))
            };
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                new IndividualPostingDataAccess().Save(transaction, soldier);
                transaction.Commit();
            }

            IndividualPostingRecord record = Assert.Single(
                new IndividualPostingDataAccess().GetRecords(connection));

            Assert.Equal(soldier.Id, record.SoldierId);
            Assert.Equal(IndividualPostingPurpose.Mechanicus, record.Purpose);
            Assert.True(record.IsOffSector);
            Assert.Null(record.LoadedShipId);
            Assert.Null(record.LandedRegionId);
            Assert.Equal(new Date(41, 998, 1).GetTotalWeeks(), record.StartedDate);
            Assert.Equal(new Date(42, 18, 1).GetTotalWeeks(), record.ExpectedReturnDate);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    [Fact]
    public void OpenEndedPosting_RoundTripsWithoutExpectedReturnDate()
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("posting_open");
        try
        {
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);
            Execute(connection, @"INSERT INTO IndividualPosting
                (SoldierId, Purpose, LoadedShipId, LandedRegionId, StartedDate)
                VALUES (5, 1, NULL, 9, 100)");

            IndividualPostingRecord record = Assert.Single(
                new IndividualPostingDataAccess().GetRecords(connection));

            Assert.False(record.IsOffSector);
            Assert.Equal(9, record.LandedRegionId);
            Assert.Null(record.ExpectedReturnDate);
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    public static IEnumerable<object[]> InvalidLocations =>
    [
        ["NULL, NULL, 0"],
        ["NULL, 9, 1"],
        ["3, NULL, 1"],
        ["3, 9, 0"]
    ];

    [Theory]
    [MemberData(nameof(InvalidLocations))]
    public void Schema_RejectsAnythingButExactlyOneLocation(string locationColumns)
    {
        string path = GameStateRoundTripFixture.CreateTempDbPath("posting_check");
        try
        {
            using SqliteConnection connection = OpenDatabase(path);
            CreateSchema(connection);

            Assert.Throws<SqliteException>(() => Execute(connection, $@"INSERT INTO IndividualPosting
                (SoldierId, Purpose, LoadedShipId, LandedRegionId, IsOffSector, StartedDate)
                VALUES (5, 2, {locationColumns}, 100)"));
        }
        finally
        {
            GameStateRoundTripFixture.CleanupDb(path);
        }
    }

    // Foreign keys are off: these tests cover the posting row alone, not the soldier graph.
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
        Execute(connection, File.ReadAllText(
            Path.Combine(RulesDatabaseFixture.RepositoryRoot, "Database", "SaveStructure.sql")));
        // The schema script turns foreign keys back on at its end.
        Execute(connection, "PRAGMA foreign_keys = off");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
