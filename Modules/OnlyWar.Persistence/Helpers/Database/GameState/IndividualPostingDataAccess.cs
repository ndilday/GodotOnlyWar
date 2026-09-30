using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OnlyWar.Persistence.Database.GameState
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
                (SoldierId, Purpose, LoadedShipId, LandedRegionId, IsOffSector,
                 StartedDate, ExpectedReturnDate)
                VALUES (@soldierId, @purpose, @shipId, @regionId, @isOffSector,
                 @startedDate, @expectedReturnDate);";
            command.AddParam("@soldierId", soldier.Id);
            command.AddParam("@purpose", (int)posting.Purpose);
            command.AddParam("@shipId", posting.Location?.Ship?.Id);
            command.AddParam("@regionId", posting.Location?.Region?.Id);
            command.AddParam("@isOffSector", posting.Location?.IsOffSector == true ? 1 : 0);
            command.AddParam("@startedDate", posting.StartedDate.GetTotalWeeks());
            command.AddParam("@expectedReturnDate", posting.ExpectedReturnDate?.GetTotalWeeks());
            command.ExecuteNonQuery();
        }

        public IReadOnlyList<IndividualPostingRecord> GetRecords(IDbConnection connection)
        {
            List<IndividualPostingRecord> records = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT SoldierId, Purpose, LoadedShipId,
                LandedRegionId, IsOffSector, StartedDate, ExpectedReturnDate
                FROM IndividualPosting ORDER BY SoldierId";
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
                bool isOffSector = reader.GetInt32(4) != 0;
                int locationCount = (shipId.HasValue ? 1 : 0)
                    + (regionId.HasValue ? 1 : 0)
                    + (isOffSector ? 1 : 0);
                if (locationCount != 1)
                {
                    throw new InvalidDataException($"Posting for soldier {soldierId} has an invalid location.");
                }
                records.Add(new IndividualPostingRecord(
                    soldierId,
                    purpose,
                    shipId,
                    regionId,
                    isOffSector,
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)));
            }
            return records;
        }
    }
}
