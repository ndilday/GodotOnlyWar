using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Events
{
    /// <summary>
    /// Idempotent Chronicle projection boundary. Standalone events compose as soon as they are
    /// recorded; grouped events wait for their correlation's BattleResolved anchor. Reconciliation
    /// is used at new-game, load, and end-of-turn boundaries and never runs from a screen refresh.
    /// </summary>
    public static class ChapterChronicleProjector
    {
        public static void ProjectEvent(
            CampaignEventLedger events,
            ChapterChronicleLedger chronicle,
            CampaignEvent @event,
            CampaignIdentity identity = null)
        {
            if (events == null || chronicle == null || @event == null) return;
            ChapterChronicleComposer composer = new(identity);
            if (@event.Publication.PublishesToChapterChronicle
                && @event.Publication.ChronicleTreatment == CampaignEventChronicleTreatment.Standalone)
            {
                AppendIfMissing(chronicle, composer, @event, $"chronicle/event/{@event.Id}");
            }

            if (@event.Type == CampaignEventType.BattleResolved)
            {
                ComposeCorrelation(events, chronicle, composer, @event.CorrelationKey);
            }
        }

        public static void Reconcile(
            CampaignEventLedger events,
            ChapterChronicleLedger chronicle,
            CampaignIdentity identity = null)
        {
            if (events == null || chronicle == null) return;
            ChapterChronicleComposer composer = new(identity);
            foreach (CampaignEvent @event in events.Events
                .Where(item => item.Publication.PublishesToChapterChronicle
                    && item.Publication.ChronicleTreatment == CampaignEventChronicleTreatment.Standalone)
                .OrderBy(item => item.OccurredWeek)
                .ThenBy(item => item.Id))
            {
                AppendIfMissing(chronicle, composer, @event, $"chronicle/event/{@event.Id}");
            }

            foreach (string correlation in events.Events
                .Where(item => item.Publication.PublishesToChapterChronicle
                    && item.Publication.ChronicleTreatment == CampaignEventChronicleTreatment.GroupWithCorrelation)
                .Select(item => item.CorrelationKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal))
            {
                ComposeCorrelation(events, chronicle, composer, correlation);
            }
        }

        /// <summary>
        /// Finalizes only the events emitted by the current turn. The correlation lookup is
        /// indexed by the ledger, so a late battle anchor can still collect earlier facts without
        /// re-walking the campaign history on every End Turn.
        /// </summary>
        public static void ReconcileRecent(
            CampaignEventLedger events,
            ChapterChronicleLedger chronicle,
            IEnumerable<CampaignEvent> recentEvents,
            CampaignIdentity identity = null)
        {
            if (events == null || chronicle == null) return;
            ChapterChronicleComposer composer = new(identity);
            List<CampaignEvent> recent = (recentEvents ?? Enumerable.Empty<CampaignEvent>())
                .Where(@event => @event != null)
                .GroupBy(@event => @event.Id)
                .Select(group => group.First())
                .ToList();
            foreach (CampaignEvent @event in recent
                .Where(item => item.Publication.PublishesToChapterChronicle
                    && item.Publication.ChronicleTreatment == CampaignEventChronicleTreatment.Standalone)
                .OrderBy(item => item.OccurredWeek)
                .ThenBy(item => item.Id))
            {
                AppendIfMissing(chronicle, composer, @event, $"chronicle/event/{@event.Id}");
            }

            foreach (string correlation in recent
                .Where(item => item.Type == CampaignEventType.BattleResolved
                    || item.Publication.ChronicleTreatment == CampaignEventChronicleTreatment.GroupWithCorrelation)
                .Select(item => item.CorrelationKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal))
            {
                ComposeCorrelation(events, chronicle, composer, correlation);
            }
        }

        private static void ComposeCorrelation(
            CampaignEventLedger events,
            ChapterChronicleLedger chronicle,
            ChapterChronicleComposer composer,
            string correlationKey)
        {
            if (string.IsNullOrWhiteSpace(correlationKey)) return;
            List<CampaignEvent> grouped = events.GetByCorrelation(correlationKey)
                .Where(@event => @event.Publication.PublishesToChapterChronicle
                    && @event.Publication.ChronicleTreatment == CampaignEventChronicleTreatment.GroupWithCorrelation)
                .ToList();
            if (grouped.Count == 0) return;

            CampaignEvent anchor = events.GetByCorrelation(correlationKey)
                .FirstOrDefault(@event => @event.Type == CampaignEventType.BattleResolved);
            if (anchor == null) return;
            List<CampaignEvent> contributors = grouped;
            if (anchor != null && contributors.All(@event => @event.Id != anchor.Id))
            {
                contributors = contributors.Append(anchor).ToList();
            }
            contributors = contributors
                .OrderBy(@event => @event.OccurredWeek)
                .ThenBy(@event => @event.Id)
                .ToList();
            AppendIfMissing(
                chronicle,
                composer,
                contributors,
                $"chronicle/correlation/{correlationKey}");
        }

        private static void AppendIfMissing(
            ChapterChronicleLedger chronicle,
            ChapterChronicleComposer composer,
            CampaignEvent @event,
            string dedupeKey) =>
            AppendIfMissing(chronicle, composer, [@event], dedupeKey);

        private static void AppendIfMissing(
            ChapterChronicleLedger chronicle,
            ChapterChronicleComposer composer,
            IReadOnlyList<CampaignEvent> contributors,
            string dedupeKey)
        {
            if (chronicle.GetByDedupeKey(dedupeKey) != null) return;
            chronicle.Append(composer.Compose(chronicle.NextId, contributors, dedupeKey));
        }
    }
}
