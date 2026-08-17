using OnlyWar.Helpers.Database;
using OnlyWar.Models.Events;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    internal sealed class CampaignEventDataAccess
    {
        internal CampaignEventLedger GetLedger(
            IDbConnection connection,
            Func<int, PlayerSoldier> soldierResolver)
        {
            CampaignEventLedger ledger = new(soldierResolver);
            if (!TableExists(connection, "CampaignEvent")) return ledger;

            Dictionary<long, List<CampaignEventEntityRef>> entities = GetEntities(connection);
            Dictionary<long, CampaignEventPublication> publications = GetPublications(connection);
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT Id, EventType, OccurredWeek, RecordedWeek,
                CorrelationKey, DedupeKey, PayloadVersion, PayloadJson
                FROM CampaignEvent ORDER BY Id";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                CampaignEventType type = ReadEnum<CampaignEventType>(reader.GetInt32(1), "event type", id);
                int occurredWeek = reader.GetInt32(2);
                int recordedWeek = reader.GetInt32(3);
                string correlationKey = reader.IsDBNull(4) ? null : reader.GetString(4);
                string dedupeKey = reader.GetString(5);
                int version = reader.GetInt32(6);
                if (version <= 0 || version > ushort.MaxValue)
                    throw new InvalidDataException($"Event {id} type {type} has invalid payload version {version}.");
                string payloadJson = reader.GetString(7);
                ICampaignEventPayload payload = CampaignEventPayloadRegistry.Deserialize(
                    id, type, (ushort)version, payloadJson);
                if (!publications.TryGetValue(id, out CampaignEventPublication publication))
                {
                    throw new InvalidDataException($"Event {id} type {type} has no publication row.");
                }
                ledger.Append(new CampaignEvent(
                    id,
                    type,
                    occurredWeek,
                    recordedWeek,
                    correlationKey,
                    dedupeKey,
                    (ushort)version,
                    payload,
                    entities.GetValueOrDefault(id) ?? [],
                    publication));
            }

            long orphanPublication = publications.Keys.FirstOrDefault(id => ledger.GetById(id) == null);
            if (orphanPublication != 0)
                throw new InvalidDataException($"Campaign event publication {orphanPublication} has no event.");
            long orphanEntity = entities.Keys.FirstOrDefault(id => ledger.GetById(id) == null);
            if (orphanEntity != 0)
                throw new InvalidDataException($"Campaign event entity row references missing event {orphanEntity}.");
            return ledger;
        }

        internal void SaveLedger(IDbTransaction transaction, CampaignEventLedger ledger)
        {
            if (ledger == null) return;
            using IDbCommand eventCommand = transaction.Connection.CreateCommand();
            using IDbCommand entityCommand = transaction.Connection.CreateCommand();
            using IDbCommand publicationCommand = transaction.Connection.CreateCommand();
            eventCommand.Transaction = transaction;
            entityCommand.Transaction = transaction;
            publicationCommand.Transaction = transaction;
            eventCommand.CommandText = @"INSERT INTO CampaignEvent
                (Id, EventType, OccurredWeek, RecordedWeek, CorrelationKey, DedupeKey,
                 PayloadVersion, PayloadJson)
                VALUES (@id, @eventType, @occurredWeek, @recordedWeek, @correlationKey,
                        @dedupeKey, @payloadVersion, @payloadJson)";
            entityCommand.CommandText = @"INSERT INTO CampaignEventEntity
                (CampaignEventId, EntityKind, EntityId, EntityRole, DisplayName, SortOrder)
                VALUES (@eventId, @entityKind, @entityId, @entityRole, @displayName, @sortOrder)";
            publicationCommand.CommandText = @"INSERT INTO CampaignEventPublication
                (CampaignEventId, PublishServiceRecord, PublishTurnReport,
                 PublishChapterChronicle, Importance, ReasonFlags, ChronicleTreatment,
                 ClassifierVersion)
                VALUES (@eventId, @service, @turn, @chronicle, @importance, @reasons,
                        @treatment, @classifierVersion)";

            foreach (CampaignEvent @event in ledger.Events.OrderBy(item => item.Id))
            {
                eventCommand.Parameters.Clear();
                eventCommand.AddParam("@id", @event.Id);
                eventCommand.AddParam("@eventType", (int)@event.Type);
                eventCommand.AddParam("@occurredWeek", @event.OccurredWeek);
                eventCommand.AddParam("@recordedWeek", @event.RecordedWeek);
                eventCommand.AddParam("@correlationKey", @event.CorrelationKey);
                eventCommand.AddParam("@dedupeKey", @event.DedupeKey);
                eventCommand.AddParam("@payloadVersion", @event.PayloadVersion);
                eventCommand.AddParam("@payloadJson", CampaignEventPayloadRegistry.Serialize(@event));
                eventCommand.ExecuteNonQuery();

                int sortOrder = 0;
                foreach (CampaignEventEntityRef entity in @event.Entities)
                {
                    entityCommand.Parameters.Clear();
                    entityCommand.AddParam("@eventId", @event.Id);
                    entityCommand.AddParam("@entityKind", (int)entity.Kind);
                    entityCommand.AddParam("@entityId", entity.EntityId);
                    entityCommand.AddParam("@entityRole", (int)entity.Role);
                    entityCommand.AddParam("@displayName", entity.DisplayNameSnapshot);
                    entityCommand.AddParam("@sortOrder", sortOrder++);
                    entityCommand.ExecuteNonQuery();
                }

                publicationCommand.Parameters.Clear();
                publicationCommand.AddParam("@eventId", @event.Id);
                publicationCommand.AddParam("@service", @event.Publication.PublishesToServiceRecord ? 1 : 0);
                publicationCommand.AddParam("@turn", @event.Publication.PublishesToTurnReport ? 1 : 0);
                publicationCommand.AddParam("@chronicle", @event.Publication.PublishesToChapterChronicle ? 1 : 0);
                publicationCommand.AddParam("@importance", (int)@event.Publication.Importance);
                publicationCommand.AddParam("@reasons", (int)@event.Publication.ReasonFlags);
                publicationCommand.AddParam("@treatment", (int)@event.Publication.ChronicleTreatment);
                publicationCommand.AddParam("@classifierVersion", @event.Publication.ClassifierVersion);
                publicationCommand.ExecuteNonQuery();
            }
        }

        private static Dictionary<long, List<CampaignEventEntityRef>> GetEntities(IDbConnection connection)
        {
            Dictionary<long, List<CampaignEventEntityRef>> result = new();
            if (!TableExists(connection, "CampaignEventEntity")) return result;
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT CampaignEventId, EntityKind, EntityId, EntityRole,
                DisplayName, SortOrder FROM CampaignEventEntity
                ORDER BY CampaignEventId, SortOrder";
            using IDataReader reader = command.ExecuteReader();
            Dictionary<long, int> nextSort = new();
            while (reader.Read())
            {
                long eventId = reader.GetInt64(0);
                int sortOrder = reader.GetInt32(5);
                if (!nextSort.ContainsKey(eventId) && sortOrder != 0)
                    throw new InvalidDataException($"Event {eventId} entity sort order must start at zero.");
                if (nextSort.TryGetValue(eventId, out int expected) && expected != sortOrder)
                    throw new InvalidDataException($"Event {eventId} entity sort order is invalid.");
                nextSort[eventId] = sortOrder + 1;
                if (!result.TryGetValue(eventId, out List<CampaignEventEntityRef> list))
                {
                    list = new List<CampaignEventEntityRef>();
                    result.Add(eventId, list);
                }
                CampaignEntityKind kind = ReadEnum<CampaignEntityKind>(reader.GetInt32(1), "entity kind", eventId);
                CampaignEventEntityRole role = ReadEnum<CampaignEventEntityRole>(reader.GetInt32(3), "entity role", eventId);
                string displayName = reader.IsDBNull(4) ? null : reader.GetString(4);
                list.Add(new CampaignEventEntityRef(kind, reader.GetInt32(2), role, displayName));
            }
            return result;
        }

        private static Dictionary<long, CampaignEventPublication> GetPublications(IDbConnection connection)
        {
            Dictionary<long, CampaignEventPublication> result = new();
            if (!TableExists(connection, "CampaignEventPublication")) return result;
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT CampaignEventId, PublishServiceRecord,
                PublishTurnReport, PublishChapterChronicle, Importance, ReasonFlags,
                ChronicleTreatment, ClassifierVersion FROM CampaignEventPublication";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long eventId = reader.GetInt64(0);
                CampaignEventSurfaceFlags surfaces = CampaignEventSurfaceFlags.None;
                if (ReadBool(reader[1])) surfaces |= CampaignEventSurfaceFlags.ServiceRecord;
                if (ReadBool(reader[2])) surfaces |= CampaignEventSurfaceFlags.TurnReport;
                if (ReadBool(reader[3])) surfaces |= CampaignEventSurfaceFlags.ChapterChronicle;
                if (!result.TryAdd(
                    eventId,
                    new CampaignEventPublication(
                        surfaces,
                        ReadEnum<CampaignEventImportance>(reader.GetInt32(4), "importance", eventId),
                        (CampaignEventReasonFlags)reader.GetInt32(5),
                        ReadEnum<CampaignEventChronicleTreatment>(reader.GetInt32(6), "Chronicle treatment", eventId),
                        reader.GetInt32(7))))
                {
                    throw new InvalidDataException($"Event {eventId} has duplicate publication rows.");
                }
            }
            return result;
        }

        private static bool ReadBool(object value) => Convert.ToBoolean(value);

        private static TEnum ReadEnum<TEnum>(int value, string label, long eventId)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
                throw new InvalidDataException($"Event {eventId} has unknown {label} value {value}.");
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
