using Microsoft.Data.Sqlite;
using OnlyWar.Helpers.Database.GameState;
using OnlyWar.Models.Reports;
using System.Data;
using Xunit;

namespace OnlyWar.Tests.Data;

public sealed class LastTurnReportDataAccessTests
{
    [Fact]
    public void GetSnapshot_WhenTableIsMissing_ReturnsNull()
    {
        using SqliteConnection connection = OpenConnection();

        Assert.Null(new LastTurnReportDataAccess().GetSnapshot(connection));
    }

    [Fact]
    public void GetSnapshot_WhenRowIsMissing_ReturnsNull()
    {
        using SqliteConnection connection = OpenConnection();
        Execute(connection, @"CREATE TABLE LastTurnReport (
            Id INTEGER PRIMARY KEY CHECK (Id = 1),
            ResolvedDate INTEGER NOT NULL,
            PayloadJson TEXT NOT NULL)");

        Assert.Null(new LastTurnReportDataAccess().GetSnapshot(connection));
    }

    [Fact]
    public void SaveSnapshot_RoundTripsTheBoundedPayload()
    {
        using SqliteConnection connection = OpenConnection();
        Execute(connection, @"CREATE TABLE LastTurnReport (
            Id INTEGER PRIMARY KEY CHECK (Id = 1),
            ResolvedDate INTEGER NOT NULL,
            PayloadJson TEXT NOT NULL)");
        LastTurnReportSnapshot expected = new(
            123,
            [new LastTurnReportEntrySnapshot(
                "Mission",
                "A mission",
                "Outcome",
                "COMPLETE",
                false)]);

        using (IDbTransaction transaction = connection.BeginTransaction())
        {
            new LastTurnReportDataAccess().SaveSnapshot(transaction, expected);
            transaction.Commit();
        }

        LastTurnReportSnapshot actual = new LastTurnReportDataAccess().GetSnapshot(connection);
        Assert.Equal(expected.ResolvedDate, actual.ResolvedDate);
        LastTurnReportEntrySnapshot entry = Assert.Single(actual.Entries);
        Assert.Equal("Mission", entry.Title);
        Assert.Equal("COMPLETE", entry.OutcomeStatus);
    }

    private static SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static void Execute(IDbConnection connection, string sql)
    {
        using IDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
