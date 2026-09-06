using OnlyWar.Helpers.Database;
using OnlyWar.Models.Reports;
using System;
using System.Data;
using System.IO;
using System.Text.Json;

namespace OnlyWar.Helpers.Database.GameState
{
    /// <summary>
    /// Reads and writes the optional, bounded report for the most recently resolved turn.
    /// Missing table/row means that the supported save simply has no resolved turn report yet.
    /// </summary>
    internal sealed class LastTurnReportDataAccess
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        internal LastTurnReportSnapshot GetSnapshot(IDbConnection connection)
        {
            if (!TableExists(connection))
            {
                return null;
            }

            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT ResolvedDate, PayloadJson
                FROM LastTurnReport WHERE Id = 1";
            using IDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            if (reader[1] is DBNull)
            {
                throw new InvalidDataException("The LastTurnReport row has no payload.");
            }

            string payload = reader.GetString(1);
            try
            {
                LastTurnReportSnapshot snapshot = JsonSerializer.Deserialize<LastTurnReportSnapshot>(
                    payload,
                    JsonOptions);
                if (snapshot == null)
                {
                    throw new InvalidDataException("The LastTurnReport payload is empty.");
                }

                return snapshot;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The LastTurnReport payload is not valid.", exception);
            }
        }

        internal void SaveSnapshot(IDbTransaction transaction, LastTurnReportSnapshot snapshot)
        {
            using IDbCommand delete = transaction.Connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM LastTurnReport";
            delete.ExecuteNonQuery();

            if (snapshot == null)
            {
                return;
            }

            using IDbCommand insert = transaction.Connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO LastTurnReport (Id, ResolvedDate, PayloadJson)
                VALUES (1, @resolvedDate, @payloadJson)";
            insert.AddParam("@resolvedDate", snapshot.ResolvedDate);
            insert.AddParam("@payloadJson", JsonSerializer.Serialize(snapshot, JsonOptions));
            insert.ExecuteNonQuery();
        }

        private static bool TableExists(IDbConnection connection)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT 1 FROM sqlite_master
                WHERE type = 'table' AND name = 'LastTurnReport' LIMIT 1";
            return command.ExecuteScalar() != null;
        }
    }
}
