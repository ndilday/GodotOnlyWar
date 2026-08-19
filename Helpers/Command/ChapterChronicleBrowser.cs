using OnlyWar.Models;
using OnlyWar.Models.Command;
using OnlyWar.Models.Events;
using OnlyWar.Models.Missions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Command
{
    internal static class ChapterChronicleBrowser
    {
        internal const int PageSize = 20;

        internal static IReadOnlyList<ChronicleFilter> GetAvailableFilters(
            ChapterChronicleLedger chronicle,
            CampaignEventLedger events)
        {
            List<ChronicleFilter> filters = [ChronicleFilter.All];
            foreach (ChronicleFilter filter in Enum.GetValues<ChronicleFilter>())
            {
                if (filter == ChronicleFilter.All) continue;
                if (HasAny(chronicle, events, filter))
                {
                    filters.Add(filter);
                }
            }
            return filters.AsReadOnly();
        }

        internal static IReadOnlyList<ChronicleEntryViewModel> GetPage(
            ChapterChronicleLedger chronicle,
            CampaignEventLedger events,
            Sector sector,
            ChronicleFilter filter,
            int page)
        {
            if (chronicle == null) return Array.Empty<ChronicleEntryViewModel>();
            IReadOnlyList<ChapterChronicleEntry> entries = filter == ChronicleFilter.All
                ? chronicle.GetPage(page, PageSize)
                : chronicle.HasUnindexedCategoryMetadata
                    ? chronicle.GetPage(entry => Matches(entry, events, filter), page, PageSize)
                    : chronicle.GetPage(ToCategory(filter), page, PageSize);
            return entries
                .Select(entry => ToViewModel(entry, events, sector, chronicle.GetAnnotations(entry.Id)))
                .ToList()
                .AsReadOnly();
        }

        internal static bool HasPage(
            ChapterChronicleLedger chronicle,
            CampaignEventLedger events,
            ChronicleFilter filter,
            int page)
        {
            if (chronicle == null) return false;
            return filter == ChronicleFilter.All
                ? chronicle.GetPage(page, 1).Count > 0
                : chronicle.HasUnindexedCategoryMetadata
                    ? chronicle.GetPage(entry => Matches(entry, events, filter), page, 1).Count > 0
                    : chronicle.GetPage(ToCategory(filter), page, 1).Count > 0;
        }

        internal static int Count(
            ChapterChronicleLedger chronicle,
            CampaignEventLedger events,
            ChronicleFilter filter)
        {
            if (chronicle == null) return 0;
            return filter == ChronicleFilter.All
                ? chronicle.Entries.Count
                : chronicle.HasUnindexedCategoryMetadata
                    ? chronicle.Entries.Count(entry => Matches(entry, events, filter))
                    : chronicle.GetCategoryCount(ToCategory(filter));
        }

        private static bool HasAny(
            ChapterChronicleLedger chronicle,
            CampaignEventLedger events,
            ChronicleFilter filter) =>
            Count(chronicle, events, filter) > 0;

        internal static bool Matches(
            ChapterChronicleEntry entry,
            CampaignEventLedger events,
            ChronicleFilter filter)
        {
            if (filter == ChronicleFilter.All) return true;
            if (entry?.HasCategoryMetadata == true)
            {
                return entry.Categories.Contains(ToCategory(filter));
            }
            return GetCategories(entry, events).Contains(filter);
        }

        internal static IReadOnlySet<ChronicleFilter> GetCategories(
            ChapterChronicleEntry entry,
            CampaignEventLedger events)
        {
            HashSet<ChronicleFilter> categories = [];
            if (entry?.HasCategoryMetadata == true)
            {
                foreach (ChapterChronicleCategory category in entry.Categories)
                {
                    categories.Add(ToFilter(category));
                }
                return categories;
            }

            if (entry?.Importance == CampaignEventImportance.Defining)
                categories.Add(ChronicleFilter.Defining);

            foreach (CampaignEvent @event in GetEvents(entry, events))
            {
                switch (@event.Type)
                {
                    case CampaignEventType.ChapterFounded:
                        categories.Add(ChronicleFilter.Chapter);
                        categories.Add(ChronicleFilter.Defining);
                        break;
                    case CampaignEventType.BattleResolved:
                    case CampaignEventType.FirstBlood:
                    case CampaignEventType.KillMilestone:
                    case CampaignEventType.LastSurvivor:
                    case CampaignEventType.SquadHeldAgainstOdds:
                        categories.Add(ChronicleFilter.Battles);
                        break;
                    case CampaignEventType.Death:
                    case CampaignEventType.MentorAssigned:
                    case CampaignEventType.NearDeathRecovery:
                    case CampaignEventType.BodyPartReplacement:
                        categories.Add(ChronicleFilter.Brothers);
                        break;
                    case CampaignEventType.LegacyChapterHistory:
                        categories.Add(ChronicleFilter.Battles);
                        break;
                }

                if (@event.Entities.Any(entity => entity.Kind == CampaignEntityKind.Planet
                    || entity.Kind == CampaignEntityKind.Region))
                {
                    categories.Add(ChronicleFilter.Worlds);
                }
            }
            return categories;
        }

        private static ChronicleEntryViewModel ToViewModel(
            ChapterChronicleEntry entry,
            CampaignEventLedger events,
            Sector sector,
            IReadOnlyList<ChapterChronicleAnnotation> annotations)
        {
            List<CampaignEvent> contributors = GetEvents(entry, events).ToList();
            IReadOnlySet<ChronicleFilter> categories = GetCategories(entry, events);
            ChronicleFilter primaryCategory = categories.Contains(ChronicleFilter.Chapter)
                ? ChronicleFilter.Chapter
                : categories.Contains(ChronicleFilter.Battles)
                    ? ChronicleFilter.Battles
                    : categories.Contains(ChronicleFilter.Brothers)
                        ? ChronicleFilter.Brothers
                        : categories.Contains(ChronicleFilter.Worlds)
                            ? ChronicleFilter.Worlds
                            : ChronicleFilter.Defining;
            List<ChronicleEntityLink> links = contributors
                .SelectMany(@event => @event.Entities)
                .Where(entity => entity.Role is CampaignEventEntityRole.Subject
                    or CampaignEventEntityRole.Location
                    or CampaignEventEntityRole.Related
                    or CampaignEventEntityRole.Authority)
                .GroupBy(entity => (entity.Kind, entity.EntityId, entity.DisplayNameSnapshot))
                .Select(group => ToLink(group.First(), contributors, sector))
                .ToList();
            string relatedBattle = contributors
                .Select(@event => @event.Payload)
                .OfType<BattleResolvedPayload>()
                .Select(payload => payload.Title)
                .FirstOrDefault();
            string dateLabel = entry.OccurredWeek > 0
                ? Date.FromTotalWeeks(entry.OccurredWeek).ToString()
                : "Unknown date";
            return new ChronicleEntryViewModel(
                entry.Id,
                entry.OccurredWeek,
                dateLabel,
                entry.Importance,
                primaryCategory,
                entry.Title,
                annotations == null || annotations.Count == 0
                    ? entry.Body
                    : entry.Body + "\n\n" + string.Join("\n", annotations.Select(item => item.Body)),
                links.AsReadOnly(),
                relatedBattle);
        }

        private static ChronicleEntityLink ToLink(
            CampaignEventEntityRef entity,
            IReadOnlyList<CampaignEvent> contributors,
            Sector sector)
        {
            CampaignNavigationTargetKind intended = MapTargetKind(entity.Kind);
            bool available = TryResolveEntity(entity, sector);
            if (!available
                && (entity.Kind is CampaignEntityKind.Mission or CampaignEntityKind.Order)
                && TryGetAvailableLocation(contributors, sector, out CampaignEventEntityRef location))
            {
                CampaignNavigationTarget locationTarget = new(
                    MapTargetKind(location.Kind),
                    location.EntityId,
                    Fallback: $"Open the recorded location for {entity.DisplayNameSnapshot}.",
                    DisplayNameSnapshot: location.DisplayNameSnapshot);
                return new ChronicleEntityLink(
                    $"{entity.DisplayNameSnapshot} (location)",
                    locationTarget,
                    true);
            }

            CampaignNavigationTarget target = available
                ? new CampaignNavigationTarget(
                    intended,
                    entity.EntityId,
                    DisplayNameSnapshot: entity.DisplayNameSnapshot,
                    Fallback: $"Open {entity.DisplayNameSnapshot}.")
                : CampaignNavigationTarget.UnavailableFor(intended, entity.DisplayNameSnapshot);
            return new ChronicleEntityLink(entity.DisplayNameSnapshot, target, available);
        }

        private static bool TryGetAvailableLocation(
            IReadOnlyList<CampaignEvent> contributors,
            Sector sector,
            out CampaignEventEntityRef location)
        {
            location = contributors?
                .SelectMany(@event => @event.Entities)
                .Where(entity => entity.Role == CampaignEventEntityRole.Location
                    && entity.Kind is CampaignEntityKind.Region or CampaignEntityKind.Planet)
                .OrderBy(entity => entity.Kind == CampaignEntityKind.Region ? 0 : 1)
                .FirstOrDefault(entity => TryResolveEntity(entity, sector));
            return location != null;
        }

        private static bool TryResolveEntity(CampaignEventEntityRef entity, Sector sector)
        {
            if (sector == null || entity == null) return false;
            PlayerForce force = sector.PlayerForce;
            return entity.Kind switch
            {
                CampaignEntityKind.Chapter => force?.Army?.OrderOfBattle != null,
                CampaignEntityKind.Soldier => force?.Army?.PlayerSoldierMap.ContainsKey(entity.EntityId) == true
                    || force?.Army?.FallenBrothers.ContainsKey(entity.EntityId) == true,
                CampaignEntityKind.Squad => force?.Army?.OrderOfBattle?.GetAllSquads()
                    .Any(squad => squad.Id == entity.EntityId) == true,
                CampaignEntityKind.Planet => sector.Planets.ContainsKey(entity.EntityId),
                CampaignEntityKind.Region => sector.Planets.Values
                    .SelectMany(planet => planet.Regions)
                    .Any(region => region?.Id == entity.EntityId),
                CampaignEntityKind.Mission => sector.Planets.Values
                    .SelectMany(planet => planet.Regions)
                    .SelectMany(region => region?.SpecialMissions ?? [])
                    .Any(mission => mission?.Id == entity.EntityId),
                CampaignEntityKind.Order => sector.Orders.ContainsKey(entity.EntityId),
                CampaignEntityKind.Character => sector.Characters.Any(character => character.Id == entity.EntityId),
                _ => false
            };
        }

        private static CampaignNavigationTargetKind MapTargetKind(CampaignEntityKind kind) => kind switch
        {
            CampaignEntityKind.Chapter => CampaignNavigationTargetKind.SectorMap,
            CampaignEntityKind.Soldier => CampaignNavigationTargetKind.Soldier,
            CampaignEntityKind.Squad => CampaignNavigationTargetKind.Squad,
            CampaignEntityKind.Planet => CampaignNavigationTargetKind.Planet,
            CampaignEntityKind.Region => CampaignNavigationTargetKind.Region,
            CampaignEntityKind.Mission => CampaignNavigationTargetKind.Mission,
            CampaignEntityKind.Order => CampaignNavigationTargetKind.Order,
            _ => CampaignNavigationTargetKind.SectorMap
        };

        private static ChapterChronicleCategory ToCategory(ChronicleFilter filter) => filter switch
        {
            ChronicleFilter.Defining => ChapterChronicleCategory.Defining,
            ChronicleFilter.Battles => ChapterChronicleCategory.Battles,
            ChronicleFilter.Brothers => ChapterChronicleCategory.Brothers,
            ChronicleFilter.Worlds => ChapterChronicleCategory.Worlds,
            ChronicleFilter.Chapter => ChapterChronicleCategory.Chapter,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        private static ChronicleFilter ToFilter(ChapterChronicleCategory category) => category switch
        {
            ChapterChronicleCategory.Defining => ChronicleFilter.Defining,
            ChapterChronicleCategory.Battles => ChronicleFilter.Battles,
            ChapterChronicleCategory.Brothers => ChronicleFilter.Brothers,
            ChapterChronicleCategory.Worlds => ChronicleFilter.Worlds,
            ChapterChronicleCategory.Chapter => ChronicleFilter.Chapter,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        private static IEnumerable<CampaignEvent> GetEvents(
            ChapterChronicleEntry entry,
            CampaignEventLedger events) =>
            entry?.CampaignEventIds
                .Select(id => events?.GetById(id))
                .Where(@event => @event != null)
                ?? Enumerable.Empty<CampaignEvent>();
    }
}
