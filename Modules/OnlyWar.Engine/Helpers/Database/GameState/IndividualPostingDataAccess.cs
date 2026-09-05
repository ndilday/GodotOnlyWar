using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
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

        public void Populate(
            IDbConnection connection,
            IReadOnlyDictionary<int, Squad> squads,
            IReadOnlyDictionary<int, PlayerSoldier> soldiers,
            IReadOnlyDictionary<int, Ship> ships,
            IReadOnlyDictionary<int, Region> regions)
        {
            IndividualPostingService service = new();
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT SoldierId, Purpose, LoadedShipId,
                LandedRegionId, StartedDate FROM IndividualPosting ORDER BY SoldierId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierId = reader.GetInt32(0);
                if (!soldiers.TryGetValue(soldierId, out PlayerSoldier soldier))
                {
                    throw new InvalidDataException($"Posting references missing soldier {soldierId}.");
                }
                int purposeValue = reader.GetInt32(1);
                if (!Enum.IsDefined(typeof(IndividualPostingPurpose), purposeValue))
                {
                    throw new InvalidDataException($"Posting for soldier {soldierId} has an invalid purpose.");
                }
                IndividualPostingPurpose purpose = (IndividualPostingPurpose)purposeValue;
                Ship ship = reader.IsDBNull(2) ? null : ships.GetValueOrDefault(reader.GetInt32(2));
                Region region = reader.IsDBNull(3) ? null : regions.GetValueOrDefault(reader.GetInt32(3));
                if ((ship == null) == (region == null))
                {
                    throw new InvalidDataException($"Posting for soldier {soldierId} has an invalid location.");
                }
                service.RestorePhysical(
                    soldier,
                    purpose,
                    ship != null ? CampaignLocation.Aboard(ship) : CampaignLocation.Landed(region),
                    Date.FromTotalWeeks(reader.GetInt32(4)));
            }
        }
    }
}
