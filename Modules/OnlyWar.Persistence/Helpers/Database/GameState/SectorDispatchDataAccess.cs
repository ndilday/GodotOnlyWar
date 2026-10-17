using OnlyWar.Persistence.Database;
using OnlyWar.Domain.Events;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Persistence.Database.GameState
{
    /// <summary>
    /// The state sector dispatch detection keeps across saves: open danger episodes and active
    /// revolts per world, and the last known Sector Lord (OnlyWar_TDD.md §6.1).
    /// </summary>
    internal sealed class SectorDispatchDataAccess
    {
        internal IReadOnlyList<SectorDispatchPlanetState> GetPlanetStates(IDbConnection connection)
        {
            List<SectorDispatchPlanetState> states = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT PlanetId, InDanger, RevoltActive, RevoltChapterParticipated
                FROM SectorDispatchPlanet ORDER BY PlanetId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                states.Add(new SectorDispatchPlanetState(
                    reader.GetInt32(0),
                    System.Convert.ToBoolean(reader[1]),
                    System.Convert.ToBoolean(reader[2]),
                    System.Convert.ToBoolean(reader[3])));
            }
            return states;
        }

        // Null when the tracker had not yet observed the seat; that is not the same as a vacant
        // seat, which is a row with a null CharacterId.
        internal SectorLordState GetSectorLord(IDbConnection connection)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT CharacterId, CapitalPlanetId
                FROM SectorDispatchSectorLord WHERE Id = 1";
            using IDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new SectorLordState(
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1));
        }

        internal void Save(IDbTransaction transaction, SectorDispatchTracker tracker)
        {
            if (tracker == null) return;
            using (IDbCommand command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO SectorDispatchPlanet
                    (PlanetId, InDanger, RevoltActive, RevoltChapterParticipated)
                    VALUES (@planet, @inDanger, @revolt, @participated)";
                foreach (SectorDispatchPlanetState state in tracker.States)
                {
                    command.Parameters.Clear();
                    command.AddParam("@planet", state.PlanetId);
                    command.AddParam("@inDanger", state.InDanger ? 1 : 0);
                    command.AddParam("@revolt", state.RevoltActive ? 1 : 0);
                    command.AddParam("@participated", state.RevoltChapterParticipated ? 1 : 0);
                    command.ExecuteNonQuery();
                }
            }

            if (tracker.SectorLord == null) return;
            using IDbCommand lord = transaction.Connection.CreateCommand();
            lord.Transaction = transaction;
            lord.CommandText = @"INSERT INTO SectorDispatchSectorLord (Id, CharacterId, CapitalPlanetId)
                VALUES (1, @character, @capital)";
            lord.AddParam("@character", tracker.SectorLord.CharacterId);
            lord.AddParam("@capital", tracker.SectorLord.CapitalPlanetId);
            lord.ExecuteNonQuery();
        }
    }
}
