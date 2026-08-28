using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
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
                (SoldierId, PostingKind, OrderId, LoadedShipId, LandedRegionId, StartedDate)
                VALUES (@soldierId, @kind, @orderId, @shipId, @regionId, @startedDate);";
            command.AddParam("@soldierId", soldier.Id);
            command.AddParam("@kind", (int)posting.Kind);
            command.AddParam("@orderId", posting.Order?.Id);
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
            Dictionary<int, Order> orders = squads.Values
                .Select(squad => squad.CurrentOrders)
                .Where(order => order != null)
                .Distinct()
                .ToDictionary(order => order.Id);
            IndividualPostingService service = new();
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT SoldierId, PostingKind, OrderId, LoadedShipId,
                LandedRegionId, StartedDate FROM IndividualPosting ORDER BY SoldierId";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierId = reader.GetInt32(0);
                if (!soldiers.TryGetValue(soldierId, out PlayerSoldier soldier))
                {
                    throw new InvalidDataException($"Posting references missing soldier {soldierId}.");
                }
                IndividualPostingKind kind = (IndividualPostingKind)reader.GetInt32(1);
                Order order = reader.IsDBNull(2) ? null : orders.GetValueOrDefault(reader.GetInt32(2));
                Ship ship = reader.IsDBNull(3) ? null : ships.GetValueOrDefault(reader.GetInt32(3));
                Region region = reader.IsDBNull(4) ? null : regions.GetValueOrDefault(reader.GetInt32(4));
                if ((ship == null) == (region == null))
                {
                    throw new InvalidDataException($"Posting for soldier {soldierId} has an invalid location.");
                }
                if (kind == IndividualPostingKind.OperationalAttachment && order == null)
                {
                    throw new InvalidDataException($"Operational posting for soldier {soldierId} has no order.");
                }
                service.Restore(
                    soldier,
                    kind,
                    ship != null ? CampaignLocation.Aboard(ship) : CampaignLocation.Landed(region),
                    Date.FromTotalWeeks(reader.GetInt32(5)),
                    order);
            }
        }
    }
}
