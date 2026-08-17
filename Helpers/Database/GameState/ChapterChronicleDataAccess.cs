using OnlyWar.Helpers.Database;
using OnlyWar.Models.Events;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    internal sealed class ChapterChronicleDataAccess
    {
        internal ChapterChronicleLedger GetLedger(IDbConnection connection, CampaignEventLedger events)
        {
            ChapterChronicleLedger ledger = new();
            if (!TableExists(connection, "ChapterChronicleEntry")) return ledger;
            Dictionary<long, List<long>> contributors = GetContributors(connection);
            HashSet<long> loadedEntryIds = new();
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT Id, OccurredWeek, RecordedWeek, Importance,
                CorrelationKey, DedupeKey, Title, Body, NarratorKey, NarratorVersion,
                NarrativeVariant FROM ChapterChronicleEntry ORDER BY Id";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                loadedEntryIds.Add(id);
                List<long> eventIds = contributors.GetValueOrDefault(id) ?? [];
                if (eventIds.Count == 0)
                    throw new InvalidDataException($"Chronicle entry {id} has no contributing event.");
                List<CampaignEvent> contributorEvents = eventIds
                    .Select(events.GetById)
                    .ToList();
                foreach (long eventId in eventIds)
                {
                    if (events.GetById(eventId) == null)
                        throw new InvalidDataException($"Chronicle entry {id} references missing event {eventId}.");
                }
                CampaignEventImportance importance =
                    ReadEnum<CampaignEventImportance>(reader.GetInt32(3), "importance", id);
                ledger.Append(new ChapterChronicleEntry(
                    id,
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    importance,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    eventIds,
                    ChapterChronicleCategoryMapper.FromEvents(contributorEvents)));
            }
            long orphanContributor = contributors.Keys.FirstOrDefault(id => !loadedEntryIds.Contains(id));
            if (orphanContributor != 0)
                throw new InvalidDataException($"Chronicle contributor row references missing entry {orphanContributor}.");
            return ledger;
        }

        internal void SaveLedger(IDbTransaction transaction, ChapterChronicleLedger ledger)
        {
            if (ledger == null) return;
            using IDbCommand entryCommand = transaction.Connection.CreateCommand();
            using IDbCommand eventCommand = transaction.Connection.CreateCommand();
            entryCommand.Transaction = transaction;
            eventCommand.Transaction = transaction;
            entryCommand.CommandText = @"INSERT INTO ChapterChronicleEntry
                (Id, OccurredWeek, RecordedWeek, Importance, CorrelationKey, DedupeKey,
                 Title, Body, NarratorKey, NarratorVersion, NarrativeVariant)
                VALUES (@id, @occurredWeek, @recordedWeek, @importance, @correlationKey,
                        @dedupeKey, @title, @body, @narratorKey, @narratorVersion, @variant)";
            eventCommand.CommandText = @"INSERT INTO ChapterChronicleEvent
                (ChronicleEntryId, CampaignEventId, SortOrder)
                VALUES (@entryId, @eventId, @sortOrder)";
            foreach (ChapterChronicleEntry entry in ledger.Entries.OrderBy(item => item.Id))
            {
                entryCommand.Parameters.Clear();
                entryCommand.AddParam("@id", entry.Id);
                entryCommand.AddParam("@occurredWeek", entry.OccurredWeek);
                entryCommand.AddParam("@recordedWeek", entry.RecordedWeek);
                entryCommand.AddParam("@importance", (int)entry.Importance);
                entryCommand.AddParam("@correlationKey", entry.CorrelationKey);
                entryCommand.AddParam("@dedupeKey", entry.DedupeKey);
                entryCommand.AddParam("@title", entry.Title);
                entryCommand.AddParam("@body", entry.Body);
                entryCommand.AddParam("@narratorKey", entry.NarratorKey);
                entryCommand.AddParam("@narratorVersion", entry.NarratorVersion);
                entryCommand.AddParam("@variant", entry.NarrativeVariant);
                entryCommand.ExecuteNonQuery();

                int sortOrder = 0;
                foreach (long eventId in entry.CampaignEventIds)
                {
                    eventCommand.Parameters.Clear();
                    eventCommand.AddParam("@entryId", entry.Id);
                    eventCommand.AddParam("@eventId", eventId);
                    eventCommand.AddParam("@sortOrder", sortOrder++);
                    eventCommand.ExecuteNonQuery();
                }
            }
        }

        private static Dictionary<long, List<long>> GetContributors(IDbConnection connection)
        {
            Dictionary<long, List<long>> result = new();
            if (!TableExists(connection, "ChapterChronicleEvent")) return result;
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT ChronicleEntryId, CampaignEventId, SortOrder
                FROM ChapterChronicleEvent ORDER BY ChronicleEntryId, SortOrder";
            using IDataReader reader = command.ExecuteReader();
            Dictionary<long, int> nextSort = new();
            while (reader.Read())
            {
                long entryId = reader.GetInt64(0);
                int sortOrder = reader.GetInt32(2);
                if (!nextSort.ContainsKey(entryId) && sortOrder != 0)
                    throw new InvalidDataException($"Chronicle entry {entryId} contributor order must start at zero.");
                if (nextSort.TryGetValue(entryId, out int expected) && expected != sortOrder)
                    throw new InvalidDataException($"Chronicle entry {entryId} contributor order is invalid.");
                nextSort[entryId] = sortOrder + 1;
                if (!result.TryGetValue(entryId, out List<long> ids))
                {
                    ids = new List<long>();
                    result.Add(entryId, ids);
                }
                ids.Add(reader.GetInt64(1));
            }
            return result;
        }

        private static TEnum ReadEnum<TEnum>(int value, string label, long id)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
                throw new InvalidDataException($"Chronicle entry {id} has unknown {label} value {value}.");
            return (TEnum)(object)value;
        }

        private static bool TableExists(IDbConnection connection, string tableName)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1";
            command.AddParam("@name", tableName);
            return command.ExecuteScalar() != null;
        }
    }
}
