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
            IEnumerable<ChapterChronicleCategory> categories = null)
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

        public IReadOnlyList<ChapterChronicleEntry> Entries => _entries;
        public long NextId => _nextId;
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
            string narratorKey = "campaign-event-v1")
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
                CampaignEventType.BattleResolved when anchor.Payload is BattleResolvedPayload battle => battle.Title,
                _ => "Campaign Service Record"
            };
            string body = string.Join(" ", ordered
                .OrderByDescending(item => ReferenceEquals(item, anchor))
                .ThenBy(item => item.OccurredWeek)
                .ThenBy(item => item.Id)
                .Select(RenderBody));
            int variant = NarrativeVariantSelector.SelectVariant(
                _identity,
                anchor.Id,
                narratorKey,
                1,
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
                categories);
        }

        private static string RenderBody(CampaignEvent @event) => @event.Payload switch
        {
            LegacyChapterHistoryPayload legacy => string.Join(" ", legacy.SubEvents ?? []),
            BattleResolvedPayload battle => battle.Summary,
            ChapterFoundedPayload founding =>
                $"The {founding.ChapterName} was founded with {founding.InitialActiveStrength:N0} "
                + $"active battle brothers. {founding.OpeningDirective}",
            FirstBloodPayload first => $"{GetSubjectName(@event)} drew First Blood with {first.NewCumulativeTotal} confirmed kill.",
            KillMilestonePayload milestone =>
                $"{GetSubjectName(@event)} reached {milestone.Threshold} confirmed kills.",
            LegacySoldierEventPayload soldier => soldier.Detail,
            _ => CampaignEventNarrator.RenderServiceRecord(@event)
        };

        private static string GetSubjectName(CampaignEvent @event) =>
            @event.Entities.FirstOrDefault(entity => entity.Role == CampaignEventEntityRole.Subject)
                ?.DisplayNameSnapshot ?? "A battle-brother";
    }
}
