using OnlyWar.Models;
using OnlyWar.Models.Events;
using OnlyWar.Models.Soldiers;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    /// <summary>
    /// Rebuilds current presentation views from the canonical current-format event ledgers.
    /// It is not a save migrator and never imports legacy history tables.
    /// </summary>
    internal static class CampaignEventProjectionBuilder
    {
        internal static void PopulateSoldierServiceRecords(
            CampaignEventLedger ledger,
            IEnumerable<PlayerSoldier> soldiers,
            CampaignIdentity identity)
        {
            Dictionary<int, PlayerSoldier> soldierMap = (soldiers ?? [])
                .GroupBy(soldier => soldier.Id)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (PlayerSoldier soldier in soldierMap.Values)
            {
                List<SoldierEvent> projections = ledger
                    .GetEventsForEntity(CampaignEntityKind.Soldier, soldier.Id)
                    .Where(@event => @event.Entities.Any(entity =>
                        entity.Kind == CampaignEntityKind.Soldier
                        && entity.EntityId == soldier.Id
                        && (entity.Role == CampaignEventEntityRole.Subject
                            || entity.Role == CampaignEventEntityRole.Participant
                            || entity.Role == CampaignEventEntityRole.Related)))
                    .Select(@event => CampaignEventProjection.ToSoldierEvent(@event, identity))
                    .OrderBy(item => item.Date?.GetTotalWeeks() ?? 0)
                    .ThenBy(item => item.CampaignEventId)
                    .ToList();
                soldier.ReplaceEvents(projections);
            }
        }

        internal static Dictionary<Date, List<EventHistory>> BuildBattleHistoryView(
            ChapterChronicleLedger chronicle,
            CampaignEventLedger events)
        {
            Dictionary<Date, List<EventHistory>> history = [];
            foreach (ChapterChronicleEntry entry in chronicle?.Entries ?? [])
            {
                Date date = Date.FromTotalWeeks(System.Math.Max(1, entry.OccurredWeek));
                EventHistory historyEntry = new() { EventTitle = entry.Title };
                foreach (long eventId in entry.CampaignEventIds)
                {
                    CampaignEvent @event = events.GetById(eventId);
                    if (@event != null)
                    {
                        historyEntry.SubEvents.Add(ChapterChronicleText(@event));
                    }
                }
                if (historyEntry.SubEvents.Count == 0) historyEntry.SubEvents.Add(entry.Body);
                if (!history.TryGetValue(date, out List<EventHistory> entries))
                {
                    entries = [];
                    history.Add(date, entries);
                }
                entries.Add(historyEntry);
            }
            return history;
        }

        private static string ChapterChronicleText(CampaignEvent @event) => @event.Payload switch
        {
            BattleResolvedPayload battle => battle.Summary,
            FirstBloodPayload first => $"First Blood recorded at {first.NewCumulativeTotal} confirmed kill.",
            KillMilestonePayload milestone => $"Kill milestone reached: {milestone.Threshold}.",
            _ => CampaignEventNarrator.RenderServiceRecord(@event)
        };
    }
}
