using OnlyWar.Models.Soldiers;
using OnlyWar.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    public sealed class IndividualPostingDataAccess
    {
        public void Save(IDbTransaction transaction, PlayerSoldier soldier)
        {
            IndividualPosting posting = soldier?.IndividualPosting;
            if (posting == null) return;
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO IndividualPosting
                (SoldierId, Purpose, LoadedShipId, LandedRegionId, StartedDate)
                VALUES (@soldierId, @purpose, @shipId, @regionId, @startedDate);";
            command.AddParam("@soldierId", soldier.Id);
            command.AddParam("@purpose", (int)posting.Purpose);
            command.AddParam("@shipId", posting.Location?.Ship?.Id);
            command.AddParam("@regionId", posting.Location?.Region?.Id);
            command.AddParam("@startedDate", posting.StartedDate.GetTotalWeeks());
            command.ExecuteNonQuery();
        }

        public IReadOnlyList<IndividualPostingRecord> GetRecords(IDbConnection connection)
        {
            List<IndividualPostingRecord> records = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT SoldierId, Purpose, LoadedShipId,
                LandedRegionId, StartedDate FROM IndividualPosting ORDER BY SoldierId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierId = reader.GetInt32(0);
                int purposeValue = reader.GetInt32(1);
                if (!Enum.IsDefined(typeof(IndividualPostingPurpose), purposeValue))
                {
                    throw new InvalidDataException($"Posting for soldier {soldierId} has an invalid purpose.");
                }
                IndividualPostingPurpose purpose = (IndividualPostingPurpose)purposeValue;
                int? shipId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                int? regionId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                if (shipId.HasValue == regionId.HasValue)
                {
                    throw new InvalidDataException($"Posting for soldier {soldierId} has an invalid location.");
                }
                records.Add(new IndividualPostingRecord(
                    soldierId,
                    purpose,
                    shipId,
                    regionId,
                    reader.GetInt32(4)));
            }
            return records;
        }
    }
}
