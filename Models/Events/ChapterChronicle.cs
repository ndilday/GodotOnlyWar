using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Models.Events
{
    public enum ChapterChronicleCategory
    {
        Defining = 0,
        Battles = 1,
        Brothers = 2,
        Worlds = 3,
        Chapter = 4
    }

    internal static class ChapterChronicleCategoryMapper
    {
        internal static IReadOnlySet<ChapterChronicleCategory> FromEvents(
            IEnumerable<CampaignEvent> events)
        {
            List<CampaignEvent> contributors = (events ?? Enumerable.Empty<CampaignEvent>()).ToList();
            HashSet<ChapterChronicleCategory> categories = [];

            if (contributors.Any(@event => @event.Publication.Importance == CampaignEventImportance.Defining))
            {
                categories.Add(ChapterChronicleCategory.Defining);
            }

            foreach (CampaignEvent @event in contributors)
            {
                switch (@event.Type)
                {
                    case CampaignEventType.ChapterFounded:
                        categories.Add(ChapterChronicleCategory.Chapter);
                        categories.Add(ChapterChronicleCategory.Defining);
                        break;
                    case CampaignEventType.BattleResolved:
                    case CampaignEventType.FirstBlood:
                    case CampaignEventType.KillMilestone:
                    case CampaignEventType.LastSurvivor:
                    case CampaignEventType.SquadHeldAgainstOdds:
                    case CampaignEventType.LegacyChapterHistory:
                        categories.Add(ChapterChronicleCategory.Battles);
                        break;
                    case CampaignEventType.Death:
                    case CampaignEventType.MentorAssigned:
                    case CampaignEventType.NearDeathRecovery:
                    case CampaignEventType.BodyPartReplacement:
                        categories.Add(ChapterChronicleCategory.Brothers);
                        break;
                }

                if (@event.Entities.Any(entity => entity.Kind is CampaignEntityKind.Planet
                    or CampaignEntityKind.Region))
                {
                    categories.Add(ChapterChronicleCategory.Worlds);
                }
            }

            return categories;
        }
    }

    public sealed class ChapterChronicleEntry
    {
        public long Id { get; }
        public int OccurredWeek { get; }
        public int RecordedWeek { get; }
        public CampaignEventImportance Importance { get; }
        public string CorrelationKey { get; }
        public string DedupeKey { get; }
        public string Title { get; }
        public string Body { get; }
        public string NarratorKey { get; }
        public int NarratorVersion { get; }
        public int NarrativeVariant { get; }
        public IReadOnlyList<long> CampaignEventIds { get; }
        public IReadOnlyList<long> CallbackEventIds { get; }
        public IReadOnlySet<ChapterChronicleCategory> Categories { get; }
        public bool HasCategoryMetadata { get; }

        public ChapterChronicleEntry(
            long id,
            int occurredWeek,
            int recordedWeek,
            CampaignEventImportance importance,
            string correlationKey,
            string dedupeKey,
            string title,
            string body,
            string narratorKey,
            int narratorVersion,
            int narrativeVariant,
            IEnumerable<long> campaignEventIds,
            IEnumerable<ChapterChronicleCategory> categories = null,
            IEnumerable<long> callbackEventIds = null)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (occurredWeek < 0) throw new ArgumentOutOfRangeException(nameof(occurredWeek));
            if (recordedWeek < occurredWeek) throw new ArgumentException("RecordedWeek precedes OccurredWeek.");
            if (string.IsNullOrWhiteSpace(dedupeKey)) throw new ArgumentException("A dedupe key is required.");
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A Chronicle title is required.");
            if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("A Chronicle body is required.");
            if (string.IsNullOrWhiteSpace(narratorKey)) throw new ArgumentException("A narrator key is required.");
            List<long> ids = (campaignEventIds ?? Enumerable.Empty<long>()).Distinct().ToList();
            if (ids.Count == 0) throw new ArgumentException("A Chronicle entry needs a contributing event.");

            Id = id;
            OccurredWeek = occurredWeek;
            RecordedWeek = recordedWeek;
            Importance = importance;
            CorrelationKey = string.IsNullOrWhiteSpace(correlationKey) ? null : correlationKey;
            DedupeKey = dedupeKey;
            Title = title;
            Body = body;
            NarratorKey = narratorKey;
            NarratorVersion = narratorVersion;
            NarrativeVariant = narrativeVariant;
            CampaignEventIds = ids.AsReadOnly();
            CallbackEventIds = (callbackEventIds ?? []).Distinct().ToList().AsReadOnly();
            HasCategoryMetadata = categories != null;
            Categories = new HashSet<ChapterChronicleCategory>(
                categories ?? Enumerable.Empty<ChapterChronicleCategory>());
        }
    }

    public sealed class ChapterChronicleLedger
    {
        private readonly List<ChapterChronicleEntry> _entries = new();
        private readonly List<ChapterChronicleEntry> _pageOrder = new();
        private readonly Dictionary<long, ChapterChronicleEntry> _byId = new();
        private readonly Dictionary<string, ChapterChronicleEntry> _byDedupe =
            new(StringComparer.Ordinal);
        private readonly Dictionary<ChapterChronicleCategory, List<ChapterChronicleEntry>> _byCategory = [];
        private int _unindexedEntryCount;
        private long _nextId = 1;
        private long _nextAnnotationId = 1;

        public IReadOnlyList<ChapterChronicleEntry> Entries => _entries;
        private readonly List<ChapterChronicleAnnotation> _annotations = [];
        public IReadOnlyList<ChapterChronicleAnnotation> Annotations => _annotations;
        public long NextId => _nextId;
        public long NextAnnotationId => _nextAnnotationId;
        public bool HasUnindexedCategoryMetadata => _unindexedEntryCount > 0;

        public ChapterChronicleEntry Append(ChapterChronicleEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (_byId.ContainsKey(entry.Id)) throw new InvalidDataException($"Chronicle id {entry.Id} is duplicated.");
            if (_byDedupe.ContainsKey(entry.DedupeKey)) throw new InvalidDataException($"Chronicle key '{entry.DedupeKey}' is duplicated.");
            _entries.Add(entry);
            InsertInPageOrder(_pageOrder, entry);
            _byId.Add(entry.Id, entry);
            _byDedupe.Add(entry.DedupeKey, entry);
            if (!entry.HasCategoryMetadata)
            {
                _unindexedEntryCount++;
            }
            else
            {
                foreach (ChapterChronicleCategory category in entry.Categories)
                {
                    if (!_byCategory.TryGetValue(category, out List<ChapterChronicleEntry> entries))
                    {
                        entries = [];
                        _byCategory.Add(category, entries);
                    }
                    InsertInPageOrder(entries, entry);
                }
            }
            _nextId = Math.Max(_nextId, entry.Id + 1);
            return entry;
        }

        public ChapterChronicleEntry GetByDedupeKey(string dedupeKey) =>
            dedupeKey != null && _byDedupe.TryGetValue(dedupeKey, out ChapterChronicleEntry entry) ? entry : null;

        public ChapterChronicleAnnotation AppendAnnotation(ChapterChronicleAnnotation annotation)
        {
            if (annotation == null) throw new ArgumentNullException(nameof(annotation));
            if (!_byId.ContainsKey(annotation.ChronicleEntryId))
                throw new InvalidDataException("An annotation must reference an existing Chronicle entry.");
            if (_annotations.Any(item => item.DedupeKey == annotation.DedupeKey))
                throw new InvalidDataException($"Chronicle annotation key '{annotation.DedupeKey}' is duplicated.");
            _annotations.Add(annotation);
            _nextAnnotationId = Math.Max(_nextAnnotationId, annotation.Id + 1);
            return annotation;
        }

        public ChapterChronicleAnnotation Annotate(
            long chronicleEntryId,
            CampaignEvent evidence,
            int recordedWeek,
            string body,
            string dedupeKey,
            bool isCorrection = true)
        {
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));
            return AppendAnnotation(new ChapterChronicleAnnotation(
                _nextAnnotationId, chronicleEntryId, evidence.Id, recordedWeek,
                body, dedupeKey, isCorrection));
        }

        public IReadOnlyList<ChapterChronicleAnnotation> GetAnnotations(long entryId) =>
            _annotations.Where(item => item.ChronicleEntryId == entryId)
                .OrderBy(item => item.RecordedWeek).ThenBy(item => item.Id).ToList();

        public IReadOnlyList<ChapterChronicleEntry> GetPage(int page, int pageSize)
        {
            if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            return _pageOrder
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToList();
        }

        public IReadOnlyList<ChapterChronicleEntry> GetPage(
            ChapterChronicleCategory category,
            int page,
            int pageSize)
        {
            if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            return _byCategory.TryGetValue(category, out List<ChapterChronicleEntry> entries)
                ? entries.Skip(page * pageSize).Take(pageSize).ToList()
                : Array.Empty<ChapterChronicleEntry>();
        }

        public int GetCategoryCount(ChapterChronicleCategory category) =>
            _byCategory.TryGetValue(category, out List<ChapterChronicleEntry> entries)
                ? entries.Count
                : 0;

        /// <summary>
        /// Applies a typed filter while walking the already ordered ledger and returns only one
        /// bounded page. The caller supplies the predicate from event metadata; prose is never
        /// inspected and no narration/classification is performed by browsing.
        /// </summary>
        public IReadOnlyList<ChapterChronicleEntry> GetPage(
            Func<ChapterChronicleEntry, bool> predicate,
            int page,
            int pageSize)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
            return _pageOrder
                .Where(predicate)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToList();
        }

        private static void InsertInPageOrder(
            List<ChapterChronicleEntry> entries,
            ChapterChronicleEntry entry)
        {
            int index = entries.BinarySearch(entry, PageOrderComparer.Instance);
            if (index < 0) index = ~index;
            entries.Insert(index, entry);
        }

        private sealed class PageOrderComparer : IComparer<ChapterChronicleEntry>
        {
            internal static PageOrderComparer Instance { get; } = new();

            public int Compare(ChapterChronicleEntry left, ChapterChronicleEntry right)
            {
                int week = right.OccurredWeek.CompareTo(left.OccurredWeek);
                return week != 0 ? week : right.Id.CompareTo(left.Id);
            }
        }
    }

    public sealed class ChapterChronicleComposer
    {
        private readonly CampaignIdentity _identity;

        public ChapterChronicleComposer(CampaignIdentity identity = null)
        {
            _identity = identity ?? CampaignIdentity.Empty;
        }

        public ChapterChronicleEntry Compose(
            long id,
            IReadOnlyList<CampaignEvent> events,
            string dedupeKey = null,
            string narratorKey = CampaignEventNarrator.ChapterInternalNarratorKey,
            IEnumerable<CampaignEvent> earlierEvents = null,
            IEnumerable<long> previouslyUsedCallbackIds = null)
        {
            if (events == null || events.Count == 0) throw new ArgumentException("Events are required.");
            List<CampaignEvent> ordered = events.OrderBy(item => item.OccurredWeek).ThenBy(item => item.Id).ToList();
            CampaignEvent first = ordered[0];
            CampaignEvent anchor = ordered.FirstOrDefault(item => item.Type == CampaignEventType.BattleResolved)
                ?? first;
            string title = anchor.Type switch
            {
                CampaignEventType.FirstBlood => "First Blood",
                CampaignEventType.KillMilestone => "A Kill Milestone",
                CampaignEventType.LastSurvivor => "Last Brother Standing",
                CampaignEventType.SquadHeldAgainstOdds => "Squad Held Against Odds",
                CampaignEventType.MentorAssigned => "Mentor Assigned",
                CampaignEventType.NearDeathRecovery => "Near-Death Recovery",
                CampaignEventType.BodyPartReplacement => "Body-Part Replacement",
                CampaignEventType.LegacyChapterHistory =>
                    ((LegacyChapterHistoryPayload)anchor.Payload).Title,
                CampaignEventType.ChapterFounded when anchor.Payload is ChapterFoundedPayload founding =>
                    $"The {founding.ChapterName} is Founded",
                CampaignEventType.WorldSaved when anchor.Payload is WorldControlChangedPayload world =>
                    $"The Restoration of {world.PlanetName}",
                CampaignEventType.WorldLost when anchor.Payload is WorldControlChangedPayload world =>
                    $"The Loss of {world.PlanetName}",
                CampaignEventType.HiddenCultRevealed when anchor.Payload is HiddenCultRevealedPayload cult =>
                    $"The Hidden War on {cult.PlanetName}",
                CampaignEventType.Death => $"The Fall of {GetSubjectName(anchor)}",
                CampaignEventType.BattleResolved when anchor.Payload is BattleResolvedPayload battle => battle.Title,
                _ => "Campaign Service Record"
            };
            CampaignEvent death = ordered.FirstOrDefault(item => item.Type == CampaignEventType.Death);
            CampaignEvent geneseed = ordered.FirstOrDefault(item => item.Type == CampaignEventType.GeneseedRecovery);
            string body = death != null && geneseed != null
                ? CampaignEventNarrator.RenderEulogy(death, geneseed, _identity)
                : string.Join(" ", ordered
                    .OrderByDescending(item => ReferenceEquals(item, anchor))
                    .ThenBy(item => item.OccurredWeek)
                    .ThenBy(item => item.Id)
                    .Select(RenderBody));
            IReadOnlyList<CampaignEvent> callbacks = ChronicleContinuitySelector.Select(
                anchor,
                ordered,
                earlierEvents,
                anchor.Publication.Importance == CampaignEventImportance.Defining ? 2 : 1,
                previouslyUsedCallbackIds);
            foreach (CampaignEvent callback in callbacks)
                body += " " + CampaignEventNarrator.RenderContinuityCallback(callback, anchor);
            int variant = NarrativeVariantSelector.SelectVariant(
                _identity,
                anchor.Id,
                narratorKey,
                CampaignEventNarrator.CurrentVersion,
                3);
            string stableDedupe = dedupeKey
                ?? (first.Publication.ChronicleTreatment == CampaignEventChronicleTreatment.GroupWithCorrelation
                    ? $"chronicle/correlation/{first.CorrelationKey}"
                    : $"chronicle/event/{first.Id}");
            IReadOnlySet<ChapterChronicleCategory> categories =
                ChapterChronicleCategoryMapper.FromEvents(ordered);
            return new ChapterChronicleEntry(
                id,
                first.OccurredWeek,
                ordered.Max(item => item.RecordedWeek),
                ordered.Max(item => item.Publication.Importance),
                first.CorrelationKey,
                stableDedupe,
                title,
                body,
                narratorKey,
                1,
                variant,
                ordered.Select(item => item.Id),
                categories,
                callbacks.Select(item => item.Id));
        }

        private static string RenderBody(CampaignEvent @event) =>
            CampaignEventNarrator.RenderChronicle(@event);

        private static string GetSubjectName(CampaignEvent @event) =>
            @event.Entities.FirstOrDefault(entity => entity.Role == CampaignEventEntityRole.Subject)
                ?.DisplayNameSnapshot ?? "A battle-brother";
    }

    public sealed record ChapterChronicleAnnotation
    {
        public long Id { get; }
        public long ChronicleEntryId { get; }
        public long EvidenceEventId { get; }
        public int RecordedWeek { get; }
        public string Body { get; }
        public string NarratorKey { get; }
        public int NarratorVersion { get; }
        public string DedupeKey { get; }
        public bool IsCorrection { get; }

        public ChapterChronicleAnnotation(long id, long chronicleEntryId, long evidenceEventId,
            int recordedWeek, string body, string dedupeKey, bool isCorrection = true,
            string narratorKey = CampaignEventNarrator.ArchivalAnnotationNarratorKey,
            int narratorVersion = CampaignEventNarrator.CurrentVersion)
        {
            if (id <= 0 || chronicleEntryId <= 0 || evidenceEventId <= 0)
                throw new ArgumentOutOfRangeException(nameof(id));
            if (recordedWeek < 0) throw new ArgumentOutOfRangeException(nameof(recordedWeek));
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(dedupeKey))
                throw new ArgumentException("Annotation body and dedupe key are required.");
            Id = id;
            ChronicleEntryId = chronicleEntryId;
            EvidenceEventId = evidenceEventId;
            RecordedWeek = recordedWeek;
            Body = body.StartsWith("Later annotation:", StringComparison.OrdinalIgnoreCase)
                ? body
                : "Later annotation: " + body;
            DedupeKey = dedupeKey;
            IsCorrection = isCorrection;
            NarratorKey = narratorKey;
            NarratorVersion = narratorVersion;
        }
    }

    public static class ChronicleContinuitySelector
    {
        public static IReadOnlyList<CampaignEvent> Select(CampaignEvent anchor,
            IEnumerable<CampaignEvent> contributors, IEnumerable<CampaignEvent> history,
            int maximum = 1, IEnumerable<long> previouslyUsedCallbackIds = null)
        {
            if (anchor == null || maximum <= 0) return [];
            HashSet<long> excluded = (contributors ?? []).Select(item => item.Id).ToHashSet();
            HashSet<(CampaignEntityKind Kind, int Id)> shared = anchor.Entities
                .Select(entity => (entity.Kind, entity.EntityId)).ToHashSet();
            HashSet<long> repeated = (previouslyUsedCallbackIds ?? []).ToHashSet();
            return (history ?? [])
                .Where(item => item.Id < anchor.Id && !excluded.Contains(item.Id)
                    && item.RecordedWeek <= anchor.RecordedWeek
                    && item.Entities.Any(entity => shared.Contains((entity.Kind, entity.EntityId))))
                .Select(item => new
                {
                    Event = item,
                    Score = Score(item, anchor) - (repeated.Contains(item.Id) ? 25 : 0)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Event.RecordedWeek)
                .ThenBy(item => item.Event.Id)
                .Take(Math.Min(2, maximum))
                .Select(item => item.Event).ToList();
        }

        private static int Score(CampaignEvent candidate, CampaignEvent anchor)
        {
            int score = candidate.Type switch
            {
                CampaignEventType.MentorAssigned => 100,
                CampaignEventType.NearDeathRecovery => 80,
                CampaignEventType.KillMilestone => 40,
                CampaignEventType.FirstBlood => 30,
                CampaignEventType.AcceptedToTraining => 20,
                _ => 10
            };
            if (candidate.Entities.Any(left => anchor.Entities.Any(right =>
                left.Kind == right.Kind && left.EntityId == right.EntityId
                && left.Kind is CampaignEntityKind.Planet or CampaignEntityKind.Faction))) score += 50;
            if (candidate.Publication.Importance >= CampaignEventImportance.Major) score += 20;
            return score;
        }
    }
}
