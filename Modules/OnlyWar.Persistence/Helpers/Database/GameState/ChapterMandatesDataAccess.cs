using OnlyWar.Domain.Mandates;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace OnlyWar.Persistence.Database.GameState
{
    /// <summary>
    /// Mandates, the legacy ledger, Chapter Master tenure and the companies that reached the
    /// Rebuild threshold (TDD §6.15 *Persistence*; save format 27).
    /// </summary>
    internal sealed class ChapterMandatesDataAccess
    {
        internal ChapterMandates Get(IDbConnection connection)
        {
            ChapterMandates mandates = new();
            List<ChapterMasterTenure> tenures = [];
            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT SoldierId, SoldierName, StartWeek, EndWeek
                    FROM ChapterMasterTenure ORDER BY Id";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    tenures.Add(new ChapterMasterTenure(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetInt32(2),
                        reader.IsDBNull(3) ? null : reader.GetInt32(3)));
                }
            }
            foreach (ChapterMasterTenure tenure in tenures) mandates.Restore(tenure);

            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT Id, Family, IsPromisedWorld, Title, SubjectName,
                    StartWeek, DeadlineWeek, PlanetIds, UnitId, FactionId, InvasionForceId,
                    ThreatKind, BaselinePledgeId, LegacyValue, State, ResolvedWeek, CleanWeeks
                    FROM Mandate ORDER BY Id";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    mandates.Add(new Mandate(
                        reader.GetInt32(0),
                        (MandateFamily)reader.GetInt32(1),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetInt32(5),
                        reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        reader.GetInt32(13),
                        ParseIds(reader.GetString(7)),
                        reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        reader.IsDBNull(9) ? null : reader.GetInt32(9),
                        reader.IsDBNull(10) ? null : reader.GetInt64(10),
                        (MandateThreatKind)reader.GetInt32(11),
                        reader.GetInt32(12),
                        Convert.ToBoolean(reader[2]),
                        (MandateState)reader.GetInt32(14),
                        reader.IsDBNull(15) ? null : reader.GetInt32(15),
                        reader.GetInt32(16)));
                }
            }

            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT Id, Week, Amount, SourceKind, SourceKey,
                    CampaignEventId, ChapterMasterId, Description
                    FROM LegacyLedgerEntry ORDER BY Id";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    mandates.Restore(new LegacyLedgerEntry(
                        reader.GetInt64(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        (LegacySourceKind)reader.GetInt32(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetInt64(5),
                        reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        reader.GetString(7)));
                }
            }

            using (IDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT UnitId FROM MandateRaisedCompany ORDER BY UnitId";
                using IDataReader reader = command.ExecuteReader();
                while (reader.Read()) mandates.MarkCompanyRaised(reader.GetInt32(0));
            }
            return mandates;
        }

        internal void Save(IDbTransaction transaction, ChapterMandates mandates)
        {
            if (mandates == null) return;
            using (IDbCommand command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Mandate
                    (Id, Family, IsPromisedWorld, Title, SubjectName, StartWeek, DeadlineWeek,
                     PlanetIds, UnitId, FactionId, InvasionForceId, ThreatKind, BaselinePledgeId,
                     LegacyValue, State, ResolvedWeek, CleanWeeks)
                    VALUES (@id, @family, @promised, @title, @subject, @start, @deadline,
                     @planets, @unit, @faction, @force, @threat, @pledge,
                     @legacy, @state, @resolved, @clean)";
                foreach (Mandate mandate in mandates.All)
                {
                    command.Parameters.Clear();
                    command.AddParam("@id", mandate.Id);
                    command.AddParam("@family", (int)mandate.Family);
                    command.AddParam("@promised", mandate.IsPromisedWorld ? 1 : 0);
                    command.AddParam("@title", mandate.Title);
                    command.AddParam("@subject", mandate.SubjectName);
                    command.AddParam("@start", mandate.StartWeek);
                    command.AddParam("@deadline", mandate.DeadlineWeek);
                    command.AddParam("@planets", string.Join(",",
                        mandate.PlanetIds.Select(id => id.ToString(CultureInfo.InvariantCulture))));
                    command.AddParam("@unit", mandate.UnitId);
                    command.AddParam("@faction", mandate.FactionId);
                    command.AddParam("@force", mandate.InvasionForceId);
                    command.AddParam("@threat", (int)mandate.ThreatKind);
                    command.AddParam("@pledge", mandate.BaselinePledgeId);
                    command.AddParam("@legacy", mandate.LegacyValue);
                    command.AddParam("@state", (int)mandate.State);
                    command.AddParam("@resolved", mandate.ResolvedWeek);
                    command.AddParam("@clean", mandate.CleanWeeks);
                    command.ExecuteNonQuery();
                }
            }

            using (IDbCommand command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO LegacyLedgerEntry
                    (Id, Week, Amount, SourceKind, SourceKey, CampaignEventId, ChapterMasterId, Description)
                    VALUES (@id, @week, @amount, @kind, @key, @event, @master, @description)";
                foreach (LegacyLedgerEntry entry in mandates.Ledger)
                {
                    command.Parameters.Clear();
                    command.AddParam("@id", entry.Id);
                    command.AddParam("@week", entry.Week);
                    command.AddParam("@amount", entry.Amount);
                    command.AddParam("@kind", (int)entry.SourceKind);
                    command.AddParam("@key", entry.SourceKey);
                    command.AddParam("@event", entry.CampaignEventId);
                    command.AddParam("@master", entry.ChapterMasterId);
                    command.AddParam("@description", entry.Description);
                    command.ExecuteNonQuery();
                }
            }

            using (IDbCommand command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO ChapterMasterTenure
                    (Id, SoldierId, SoldierName, StartWeek, EndWeek)
                    VALUES (@id, @soldier, @name, @start, @end)";
                int id = 1;
                foreach (ChapterMasterTenure tenure in mandates.Tenures)
                {
                    command.Parameters.Clear();
                    command.AddParam("@id", id++);
                    command.AddParam("@soldier", tenure.SoldierId);
                    command.AddParam("@name", tenure.SoldierName);
                    command.AddParam("@start", tenure.StartWeek);
                    command.AddParam("@end", tenure.EndWeek);
                    command.ExecuteNonQuery();
                }
            }

            using (IDbCommand command = transaction.Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO MandateRaisedCompany (UnitId) VALUES (@unit)";
                foreach (int unitId in mandates.RaisedCompanyIds.OrderBy(id => id))
                {
                    command.Parameters.Clear();
                    command.AddParam("@unit", unitId);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static IEnumerable<int> ParseIds(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? []
                : text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => int.Parse(part, CultureInfo.InvariantCulture));
    }
}
