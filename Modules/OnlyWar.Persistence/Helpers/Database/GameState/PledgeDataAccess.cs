using OnlyWar.Models;
using OnlyWar.Models.Supply;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Helpers.Database.GameState
{
    internal sealed class PledgeDataAccess
    {
        internal List<Pledge> GetPledges(IDbConnection connection)
        {
            List<Pledge> pledges = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT Id, SourcePlanetId, GrantingAuthorityId,
                PayloadKind, PayloadAmount, ScheduleKind, CadenceWeeks, Status,
                NextDeliveryDate FROM Pledge ORDER BY Id";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                pledges.Add(new Pledge(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    new PledgePayload((PledgePayloadKind)reader.GetInt32(3), reader.GetInt32(4)),
                    (PledgeScheduleKind)reader.GetInt32(5),
                    Date.FromTotalWeeks(reader.GetInt32(8)),
                    reader.GetInt32(6),
                    (PledgeStatus)reader.GetInt32(7)));
            }
            return pledges;
        }

        internal void SavePledge(IDbTransaction transaction, Pledge pledge)
        {
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO Pledge
                (Id, SourcePlanetId, GrantingAuthorityId, PayloadKind, PayloadAmount,
                 ScheduleKind, CadenceWeeks, Status, NextDeliveryDate) VALUES
                (@id, @sourcePlanetId, @authorityId, @payloadKind, @payloadAmount,
                 @scheduleKind, @cadenceWeeks, @status, @nextDeliveryDate);";
            command.AddParam("@id", pledge.Id);
            command.AddParam("@sourcePlanetId", pledge.SourcePlanetId);
            command.AddParam("@authorityId", pledge.GrantingAuthorityId);
            command.AddParam("@payloadKind", (int)pledge.Payload.Kind);
            command.AddParam("@payloadAmount", pledge.Payload.Amount);
            command.AddParam("@scheduleKind", (int)pledge.ScheduleKind);
            command.AddParam("@cadenceWeeks", pledge.CadenceWeeks);
            command.AddParam("@status", (int)pledge.Status);
            command.AddParam("@nextDeliveryDate", pledge.NextDeliveryDate.GetTotalWeeks());
            command.ExecuteNonQuery();
        }
    }
}
