using OnlyWar.Models;
using System;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    /// <summary>
    /// Persists the singleton policy owned by the player Chapter. It is intentionally separate
    /// from loadout/rules doctrine tables: this is mutable campaign state.
    /// </summary>
    internal sealed class ChapterOperationalDoctrineDataAccess
    {
        internal ChapterOperationalDoctrine GetDoctrine(IDbConnection connection)
        {
            ChapterOperationalDoctrine doctrine = new();
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT InjuryThreshold, RequireDutyReadySquadLeader,
                                           MinimumDutyReadySquadStrength
                                    FROM ChapterOperationalDoctrine
                                    WHERE Id = 1";
            using IDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return doctrine;

            doctrine.Set(
                reader.IsDBNull(0) ? null : (OnlyWar.Models.Soldiers.WoundLevel?)
                    reader.GetInt32(0),
                reader.GetBoolean(1),
                reader.GetInt32(2));
            return doctrine;
        }

        internal void SaveDoctrine(
            IDbTransaction transaction,
            ChapterOperationalDoctrine doctrine)
        {
            doctrine ??= new ChapterOperationalDoctrine();
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO ChapterOperationalDoctrine
                (Id, InjuryThreshold, RequireDutyReadySquadLeader, MinimumDutyReadySquadStrength)
                VALUES (@id, @threshold, @leader, @minimum)";
            command.AddParam("@id", 1);
            command.AddParam("@threshold", doctrine.InjuryThreshold.HasValue
                ? (int)doctrine.InjuryThreshold.Value
                : DBNull.Value);
            command.AddParam("@leader", doctrine.RequireDutyReadySquadLeader ? 1 : 0);
            command.AddParam("@minimum", doctrine.MinimumDutyReadySquadStrength);
            command.ExecuteNonQuery();
        }
    }
}
