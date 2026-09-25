using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Domain.Events
{
    public enum ChapterChronicleCategory
    {
        Defining = 0,
        Battles = 1,
        Brothers = 2,
        Worlds = 3,
        Chapter = 4
    }

    public static class ChapterChronicleCategoryMapper
    {
        public static IReadOnlySet<ChapterChronicleCategory> FromEvents(
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
            _nextId = System.Math.Max(_nextId, entry.Id + 1);
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
            _nextAnnotationId = System.Math.Max(_nextAnnotationId, annotation.Id + 1);
            return annotation;
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
            string narratorKey = "archival-annotation",
            int narratorVersion = 2)
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

}
