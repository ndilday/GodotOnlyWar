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
            Dictionary<long, List<long>> callbacks = GetOrderedEventLinks(connection, "ChapterChronicleCallback");
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
                    ChapterChronicleCategoryMapper.FromEvents(contributorEvents),
                    callbackEventIds: callbacks.GetValueOrDefault(id) ?? []));
            }
            long orphanContributor = contributors.Keys.FirstOrDefault(id => !loadedEntryIds.Contains(id));
            if (orphanContributor != 0)
                throw new InvalidDataException($"Chronicle contributor row references missing entry {orphanContributor}.");
            LoadAnnotations(connection, ledger, events);
            return ledger;
        }

        internal void SaveLedger(IDbTransaction transaction, ChapterChronicleLedger ledger)
        {
            if (ledger == null) return;
            using IDbCommand entryCommand = transaction.Connection.CreateCommand();
            using IDbCommand eventCommand = transaction.Connection.CreateCommand();
            using IDbCommand callbackCommand = transaction.Connection.CreateCommand();
            using IDbCommand annotationCommand = transaction.Connection.CreateCommand();
            entryCommand.Transaction = transaction;
            eventCommand.Transaction = transaction;
            callbackCommand.Transaction = transaction;
            annotationCommand.Transaction = transaction;
            entryCommand.CommandText = @"INSERT INTO ChapterChronicleEntry
                (Id, OccurredWeek, RecordedWeek, Importance, CorrelationKey, DedupeKey,
                 Title, Body, NarratorKey, NarratorVersion, NarrativeVariant)
                VALUES (@id, @occurredWeek, @recordedWeek, @importance, @correlationKey,
                        @dedupeKey, @title, @body, @narratorKey, @narratorVersion, @variant)";
            eventCommand.CommandText = @"INSERT INTO ChapterChronicleEvent
                (ChronicleEntryId, CampaignEventId, SortOrder)
                VALUES (@entryId, @eventId, @sortOrder)";
            callbackCommand.CommandText = @"INSERT INTO ChapterChronicleCallback
                (ChronicleEntryId, CampaignEventId, SortOrder)
                VALUES (@entryId, @eventId, @sortOrder)";
            annotationCommand.CommandText = @"INSERT INTO ChapterChronicleAnnotation
                (Id, ChronicleEntryId, EvidenceEventId, RecordedWeek, Body, NarratorKey,
                 NarratorVersion, DedupeKey, IsCorrection)
                VALUES (@id, @entryId, @evidenceId, @week, @body, @narrator,
                 @version, @dedupe, @correction)";
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
                sortOrder = 0;
                foreach (long eventId in entry.CallbackEventIds)
                {
                    callbackCommand.Parameters.Clear();
                    callbackCommand.AddParam("@entryId", entry.Id);
                    callbackCommand.AddParam("@eventId", eventId);
                    callbackCommand.AddParam("@sortOrder", sortOrder++);
                    callbackCommand.ExecuteNonQuery();
                }
            }
            foreach (ChapterChronicleAnnotation annotation in ledger.Annotations.OrderBy(item => item.Id))
            {
                annotationCommand.Parameters.Clear();
                annotationCommand.AddParam("@id", annotation.Id);
                annotationCommand.AddParam("@entryId", annotation.ChronicleEntryId);
                annotationCommand.AddParam("@evidenceId", annotation.EvidenceEventId);
                annotationCommand.AddParam("@week", annotation.RecordedWeek);
                annotationCommand.AddParam("@body", annotation.Body);
                annotationCommand.AddParam("@narrator", annotation.NarratorKey);
                annotationCommand.AddParam("@version", annotation.NarratorVersion);
                annotationCommand.AddParam("@dedupe", annotation.DedupeKey);
                annotationCommand.AddParam("@correction", annotation.IsCorrection ? 1 : 0);
                annotationCommand.ExecuteNonQuery();
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

        private static Dictionary<long, List<long>> GetOrderedEventLinks(
            IDbConnection connection, string table)
        {
            Dictionary<long, List<long>> result = new();
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT ChronicleEntryId, CampaignEventId, SortOrder FROM {table} ORDER BY ChronicleEntryId, SortOrder";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long entryId = reader.GetInt64(0);
                if (!result.TryGetValue(entryId, out List<long> ids))
                    result[entryId] = ids = [];
                ids.Add(reader.GetInt64(1));
            }
            return result;
        }

        private static void LoadAnnotations(
            IDbConnection connection, ChapterChronicleLedger ledger, CampaignEventLedger events)
        {
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT Id, ChronicleEntryId, EvidenceEventId, RecordedWeek,
                Body, NarratorKey, NarratorVersion, DedupeKey, IsCorrection
                FROM ChapterChronicleAnnotation ORDER BY Id";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long evidenceId = reader.GetInt64(2);
                if (events.GetById(evidenceId) == null)
                    throw new InvalidDataException($"Chronicle annotation references missing event {evidenceId}.");
                ledger.AppendAnnotation(new ChapterChronicleAnnotation(
                    reader.GetInt64(0), reader.GetInt64(1), evidenceId, reader.GetInt32(3),
                    reader.GetString(4), reader.GetString(7), System.Convert.ToBoolean(reader[8]),
                    reader.GetString(5), reader.GetInt32(6)));
            }
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
