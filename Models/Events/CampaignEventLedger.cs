using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;

namespace OnlyWar.Models.Events
{
    public sealed record OpenNearDeathEpisode(
        int SoldierId,
        long SourceIncapacitationEventId,
        int OccurredWeek,
        string CorrelationKey,
        int? DefiningVitalLocationTemplateId,
        string DefiningVitalLocationName);

    public sealed class TurnEventBuffer : IReadOnlyList<CampaignEvent>
    {
        private readonly List<CampaignEvent> _events = new();
        private readonly HashSet<long> _ids = new();

        public CampaignEvent this[int index] => _events[index];
        public int Count => _events.Count;

        public void Add(CampaignEvent @event)
        {
            if (@event != null && _ids.Add(@event.Id)) _events.Add(@event);
        }

        public void Clear()
        {
            _events.Clear();
            _ids.Clear();
        }

        public IEnumerator<CampaignEvent> GetEnumerator() => _events.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class CampaignEventLedger
    {
        private readonly List<CampaignEvent> _orderedEvents = new();
        private readonly Dictionary<long, CampaignEvent> _byId = new();
        private readonly Dictionary<string, CampaignEvent> _byDedupeKey =
            new(StringComparer.Ordinal);
        private readonly Dictionary<(CampaignEntityKind Kind, int EntityId), List<long>> _byEntity = new();
        private readonly SortedDictionary<int, List<long>> _byOccurredWeek = new();
        private readonly Dictionary<CampaignEventSurfaceFlags, List<long>> _bySurface = new();
        private readonly Dictionary<int, OpenNearDeathEpisode> _openNearDeathBySoldier = new();
        private readonly Dictionary<string, List<long>> _byCorrelation =
            new(StringComparer.Ordinal);
        private readonly Func<int, PlayerSoldier> _soldierResolver;
        private long _nextEventId = 1;

        public CampaignEventLedger(Func<int, PlayerSoldier> soldierResolver = null)
        {
            _soldierResolver = soldierResolver;
        }

        public long NextEventId => _nextEventId;
        public IReadOnlyList<CampaignEvent> Events => _orderedEvents;
        public int Count => _orderedEvents.Count;
        public IReadOnlyDictionary<int, OpenNearDeathEpisode> OpenNearDeathEpisodes =>
            new ReadOnlyDictionary<int, OpenNearDeathEpisode>(_openNearDeathBySoldier);

        public CampaignEvent GetById(long id) => _byId.TryGetValue(id, out CampaignEvent value) ? value : null;

        public CampaignEvent GetByDedupeKey(string dedupeKey) =>
            dedupeKey != null && _byDedupeKey.TryGetValue(dedupeKey, out CampaignEvent value)
                ? value
                : null;

        public IReadOnlyList<CampaignEvent> GetByCorrelation(string correlationKey)
        {
            if (string.IsNullOrWhiteSpace(correlationKey)) return Array.Empty<CampaignEvent>();
            if (!_byCorrelation.TryGetValue(correlationKey, out List<long> ids))
            {
                return Array.Empty<CampaignEvent>();
            }

            return ids
                .Select(id => _byId[id])
                .OrderBy(@event => @event.OccurredWeek)
                .ThenBy(@event => @event.Id)
                .ToList();
        }

        public OpenNearDeathEpisode GetOpenNearDeathEpisode(int soldierId) =>
            _openNearDeathBySoldier.TryGetValue(soldierId, out OpenNearDeathEpisode episode)
                ? episode
                : null;

        public IReadOnlyList<CampaignEvent> GetEventsForEntity(CampaignEntityKind kind, int entityId)
        {
            if (!_byEntity.TryGetValue((kind, entityId), out List<long> ids)) return Array.Empty<CampaignEvent>();
            return ids.Select(id => _byId[id])
                .OrderBy(@event => @event.OccurredWeek)
                .ThenBy(@event => @event.Id)
                .ToList();
        }

        public IReadOnlyList<CampaignEvent> GetEventsInWeekRange(
            int firstWeek,
            int lastWeek,
            CampaignEventSurfaceFlags requiredSurface = CampaignEventSurfaceFlags.None)
        {
            if (lastWeek < firstWeek) return Array.Empty<CampaignEvent>();
            IEnumerable<long> ids = _byOccurredWeek
                .Where(entry => entry.Key >= firstWeek && entry.Key <= lastWeek)
                .SelectMany(entry => entry.Value);
            if (requiredSurface != CampaignEventSurfaceFlags.None)
            {
                ids = ids.Where(id => _byId[id].Publication.SurfaceFlags.HasFlag(requiredSurface));
            }
            return ids.Select(id => _byId[id]).OrderBy(@event => @event.OccurredWeek).ThenBy(@event => @event.Id).ToList();
        }

        public IReadOnlyList<CampaignEvent> GetPublished(CampaignEventSurfaceFlags surface)
        {
            if (!_bySurface.TryGetValue(surface, out List<long> ids)) return Array.Empty<CampaignEvent>();
            return ids.Select(id => _byId[id]).OrderBy(@event => @event.OccurredWeek).ThenBy(@event => @event.Id).ToList();
        }

        public CampaignEvent Append(CampaignEvent @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            ValidateSourceReferences(@event);
            if (_byId.ContainsKey(@event.Id))
                throw new InvalidDataException($"Campaign event id {@event.Id} is duplicated.");
            if (_byDedupeKey.ContainsKey(@event.DedupeKey))
                throw new InvalidDataException($"Campaign event dedupe key '{@event.DedupeKey}' is duplicated.");

            _orderedEvents.Add(@event);
            _byId.Add(@event.Id, @event);
            _byDedupeKey.Add(@event.DedupeKey, @event);
            _nextEventId = Math.Max(_nextEventId, @event.Id + 1);
            if (!_byOccurredWeek.TryGetValue(@event.OccurredWeek, out List<long> weekIds))
            {
                weekIds = new List<long>();
                _byOccurredWeek.Add(@event.OccurredWeek, weekIds);
            }
            weekIds.Add(@event.Id);
            if (!string.IsNullOrWhiteSpace(@event.CorrelationKey))
            {
                if (!_byCorrelation.TryGetValue(@event.CorrelationKey, out List<long> correlationIds))
                {
                    correlationIds = [];
                    _byCorrelation.Add(@event.CorrelationKey, correlationIds);
                }
                correlationIds.Add(@event.Id);
            }

            foreach (CampaignEventEntityRef entity in @event.Entities)
            {
                if (!_byEntity.TryGetValue((entity.Kind, entity.EntityId), out List<long> entityIds))
                {
                    entityIds = new List<long>();
                    _byEntity.Add((entity.Kind, entity.EntityId), entityIds);
                }
                entityIds.Add(@event.Id);
            }

            foreach (CampaignEventSurfaceFlags surface in Enum.GetValues<CampaignEventSurfaceFlags>())
            {
                if (surface == CampaignEventSurfaceFlags.None || !@event.Publication.SurfaceFlags.HasFlag(surface)) continue;
                if (!_bySurface.TryGetValue(surface, out List<long> surfaceIds))
                {
                    surfaceIds = new List<long>();
                    _bySurface.Add(surface, surfaceIds);
                }
                surfaceIds.Add(@event.Id);
            }
            UpdateNearDeathProjection(@event);
            return @event;
        }

        public CampaignEvent Allocate(
            CampaignEventType type,
            int occurredWeek,
            int recordedWeek,
            string correlationKey,
            string dedupeKey,
            ushort payloadVersion,
            ICampaignEventPayload payload,
            IEnumerable<CampaignEventEntityRef> entities,
            CampaignEventPublication publication)
        {
            return Append(new CampaignEvent(
                _nextEventId++,
                type,
                occurredWeek,
                recordedWeek,
                correlationKey,
                dedupeKey,
                payloadVersion,
                payload,
                entities,
                publication));
        }

        public void Clear()
        {
            _orderedEvents.Clear();
            _byId.Clear();
            _byDedupeKey.Clear();
            _byEntity.Clear();
            _byOccurredWeek.Clear();
            _byCorrelation.Clear();
            _bySurface.Clear();
            _openNearDeathBySoldier.Clear();
            _nextEventId = 1;
        }

        internal void CloseOpenNearDeathEpisode(int soldierId, long sourceEventId)
        {
            if (_openNearDeathBySoldier.TryGetValue(soldierId, out OpenNearDeathEpisode episode)
                && episode.SourceIncapacitationEventId == sourceEventId)
            {
                _openNearDeathBySoldier.Remove(soldierId);
            }
        }

        private void ValidateSourceReferences(CampaignEvent @event)
        {
            switch (@event.Payload)
            {
                case GeneseedRecoveryPayload geneseed:
                    if (geneseed.SourceDeathEventId <= 0
                        || !_byId.TryGetValue(geneseed.SourceDeathEventId, out CampaignEvent death)
                        || death.Type != CampaignEventType.Death)
                    {
                        throw new InvalidDataException(
                            $"Event {@event.Id} gene-seed outcome references missing death event "
                            + $"{geneseed.SourceDeathEventId}.");
                    }
                    if (!string.IsNullOrWhiteSpace(death.CorrelationKey)
                        && !string.Equals(death.CorrelationKey, @event.CorrelationKey, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Event {@event.Id} gene-seed outcome does not share the death event "
                            + $"correlation {death.CorrelationKey}.");
                    }
                    break;
                case NearDeathRecoveryPayload recovery:
                    if (recovery.SourceIncapacitationEventId <= 0
                        || !_byId.TryGetValue(recovery.SourceIncapacitationEventId, out CampaignEvent incapacitation)
                        || incapacitation.Type != CampaignEventType.Incapacitated
                        || incapacitation.Payload is not IncapacitatedPayload { QualifiesAsNearDeath: true })
                    {
                        throw new InvalidDataException(
                            $"Event {@event.Id} recovery references a non-qualifying incapacitation "
                            + $"{recovery.SourceIncapacitationEventId}.");
                    }
                    if (!string.IsNullOrWhiteSpace(incapacitation.CorrelationKey)
                        && !string.Equals(incapacitation.CorrelationKey, @event.CorrelationKey, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Event {@event.Id} recovery does not share the incapacitation "
                            + $"correlation {incapacitation.CorrelationKey}.");
                    }
                    break;
                case BodyPartReplacementPayload replacement when replacement.SourceIncapacitationEventId is long sourceId:
                    if (sourceId <= 0
                        || !_byId.TryGetValue(sourceId, out CampaignEvent source)
                        || source.Type != CampaignEventType.Incapacitated)
                    {
                        throw new InvalidDataException(
                            $"Event {@event.Id} replacement references missing incapacitation "
                            + $"{sourceId}.");
                    }
                    if (!string.IsNullOrWhiteSpace(source.CorrelationKey)
                        && !string.Equals(source.CorrelationKey, @event.CorrelationKey, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Event {@event.Id} replacement does not share the incapacitation "
                            + $"correlation {source.CorrelationKey}.");
                    }
                    break;
            }
        }

        private void UpdateNearDeathProjection(CampaignEvent @event)
        {
            int? subjectId = @event.Entities
                .Where(entity => entity.Kind == CampaignEntityKind.Soldier
                    && entity.Role == CampaignEventEntityRole.Subject)
                .Select(entity => (int?)entity.EntityId)
                .FirstOrDefault();
            if (!subjectId.HasValue) return;

            switch (@event.Payload)
            {
                case IncapacitatedPayload incapacitated when incapacitated.QualifiesAsNearDeath:
                    _openNearDeathBySoldier[subjectId.Value] = new OpenNearDeathEpisode(
                        subjectId.Value,
                        @event.Id,
                        @event.OccurredWeek,
                        @event.CorrelationKey,
                        incapacitated.DefiningHitLocationTemplateId,
                        incapacitated.DefiningHitLocationName);
                    break;
                case NearDeathRecoveryPayload recovery:
                    if (_openNearDeathBySoldier.TryGetValue(subjectId.Value, out OpenNearDeathEpisode open)
                        && open.SourceIncapacitationEventId == recovery.SourceIncapacitationEventId)
                    {
                        _openNearDeathBySoldier.Remove(subjectId.Value);
                    }
                    break;
            }
        }
    }
}
