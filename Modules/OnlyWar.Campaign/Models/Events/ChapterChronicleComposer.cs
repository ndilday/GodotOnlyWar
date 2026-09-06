using System;
using System.Collections.Generic;
using System.Linq;
namespace OnlyWar.Models.Events {
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
