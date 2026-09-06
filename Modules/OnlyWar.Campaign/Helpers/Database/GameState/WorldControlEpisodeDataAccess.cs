using OnlyWar.Helpers.Database;
using OnlyWar.Models.Events;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    internal sealed class WorldControlEpisodeDataAccess
    {
        internal IReadOnlyList<WorldControlEpisodeState> GetStates(IDbConnection connection)
        {
            List<WorldControlEpisodeState> states = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT PlanetId, ImperialFactionId, LastControllingFactionId,
                WasImperialControlled, ContestedSinceWeek, ChapterParticipated
                FROM WorldControlEpisode ORDER BY PlanetId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                states.Add(new WorldControlEpisodeState(
                    reader.GetInt32(0), reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    System.Convert.ToBoolean(reader[3]),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    System.Convert.ToBoolean(reader[5])));
            }
            return states;
        }

        internal void SaveStates(IDbTransaction transaction, IEnumerable<WorldControlEpisodeState> states)
        {
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO WorldControlEpisode
                (PlanetId, ImperialFactionId, LastControllingFactionId, WasImperialControlled,
                 ContestedSinceWeek, ChapterParticipated)
                VALUES (@planet, @imperial, @controller, @wasImperial, @contested, @participated)";
            foreach (WorldControlEpisodeState state in states ?? [])
            {
                command.Parameters.Clear();
                command.AddParam("@planet", state.PlanetId);
                command.AddParam("@imperial", state.ImperialFactionId);
                command.AddParam("@controller", state.LastControllingFactionId);
                command.AddParam("@wasImperial", state.WasImperialControlled ? 1 : 0);
                command.AddParam("@contested", state.ContestedSinceWeek);
                command.AddParam("@participated", state.ChapterParticipated ? 1 : 0);
                command.ExecuteNonQuery();
            }
        }
    }
}
