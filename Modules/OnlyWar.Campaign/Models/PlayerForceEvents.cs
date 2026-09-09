using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OnlyWar.Domain.Events;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
namespace OnlyWar.Campaign.Events;

/// <summary>Campaign event policy and recorder lifetime, outside shared campaign data.</summary>
public static class PlayerForceEvents
{
    private static readonly ConditionalWeakTable<PlayerForce, CampaignEventRecorder> Recorders = new();

    public static CampaignEventRecorder GetCampaignEventRecorder(this PlayerForce force)
    {
        if (force == null) return null;
        var recorder = Recorders.GetValue(force, owner =>
        {
            var created = new CampaignEventRecorder(owner.CampaignEventLedger,
                turnBuffer: owner.CurrentTurnEvents, chronicle: owner.ChapterChronicle,
                soldierResolver: id => owner.Army?.PlayerSoldierMap.GetValueOrDefault(id)
                    ?? owner.Army?.FallenBrothers.GetValueOrDefault(id));
            foreach (var soldier in (owner.Army?.PlayerSoldierMap.Values ?? Enumerable.Empty<PlayerSoldier>())
                .Concat(owner.Army?.FallenBrothers.Values ?? Enumerable.Empty<PlayerSoldier>()))
                soldier.AttachCampaignEventRecorder(created);
            return created;
        });
        recorder.SetCampaignIdentity(force.CampaignIdentity);
        return recorder;
    }

    public static void AttachCampaignEventRecorder(this PlayerForce force, PlayerSoldier soldier) =>
        soldier?.AttachCampaignEventRecorder(force.GetCampaignEventRecorder());

    public static void AttachCampaignEventRecorder(this PlayerSoldier soldier, CampaignEventRecorder recorder) =>
        soldier.SetLegacyEventRecorder(recorder == null ? null : entry => recorder.RecordLegacySoldierEvent(soldier, entry));
        // Production callers use this boundary for the canonical battle fact. The legacy
        // AddToBattleHistory method remains available to the load/migration compatibility path,
        // but new-format persistence must not depend on its free-text representation.
        public static CampaignEvent RecordBattleResolved(this PlayerForce force, 
            Date date,
            string title,
            IReadOnlyList<string> subEvents,
            string correlationKey,
            string dedupeKey)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("A battle title is required.", nameof(title));
            if (string.IsNullOrWhiteSpace(correlationKey))
                throw new ArgumentException("A battle correlation key is required.", nameof(correlationKey));
            if (string.IsNullOrWhiteSpace(dedupeKey))
                throw new ArgumentException("A battle dedupe key is required.", nameof(dedupeKey));

            List<string> entries = subEvents?.ToList() ?? [];
            return force.GetCampaignEventRecorder().Record(new CampaignEventCandidate(
                CampaignEventType.BattleResolved,
                date.GetTotalWeeks(),
                date.GetTotalWeeks(),
                correlationKey,
                dedupeKey,
                1,
                new BattleResolvedPayload(title, string.Join(" ", entries))));
        }

        public static CampaignEvent RecordChapterFounded(this PlayerForce force, 
            Date date,
            ChapterFoundedPayload payload,
            int? chapterMasterId,
            string chapterMasterName,
            int promisedPlanetId,
            string promisedPlanetName)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            List<CampaignEventEntityRef> entities =
            [
                new CampaignEventEntityRef(
                    CampaignEntityKind.Chapter,
                    0,
                    CampaignEventEntityRole.Subject,
                    payload.ChapterName),
                new CampaignEventEntityRef(
                    CampaignEntityKind.Planet,
                    promisedPlanetId,
                    CampaignEventEntityRole.Location,
                    promisedPlanetName)
            ];
            if (chapterMasterId.HasValue && !string.IsNullOrWhiteSpace(chapterMasterName))
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Soldier,
                    chapterMasterId.Value,
                    CampaignEventEntityRole.Authority,
                    chapterMasterName));
            }
            return force.GetCampaignEventRecorder().Record(new CampaignEventCandidate(
                CampaignEventType.ChapterFounded,
                date.GetTotalWeeks(),
                date.GetTotalWeeks(),
                null,
                "chapter/founded",
                1,
                payload,
                entities,
                surfaceHint: CampaignEventSurfaceFlags.ChapterChronicle,
                importanceHint: CampaignEventImportance.Defining,
                chronicleTreatmentHint: CampaignEventChronicleTreatment.Standalone));
        }

        public static CampaignEvent RecordProceduralDeath(this PlayerForce force, 
            Date date,
            PlayerSoldier soldier,
            string detail)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            int serviceStartWeek = soldier.SoldierEvents
                .Select(entry => entry.Date?.GetTotalWeeks())
                .Where(week => week.HasValue)
                .Select(week => week.Value)
                .DefaultIfEmpty(0)
                .Min();
            return force.GetCampaignEventRecorder().RecordDeath(
                soldier,
                date,
                new DeathPayload(
                    null,
                    DeathDisposition.NonBattleProcedural,
                    null,
                    null,
                    null,
                    null,
                    soldier.Template?.Id,
                    soldier.Template?.Name,
                    soldier.Template?.Rank,
                    serviceStartWeek,
                    soldier.FactionCasualtyCountMap.Values.Sum(value => (int)value),
                    null,
                    null,
                    false,
                    false,
                    detail,
                    SoldierSubrank: soldier.Template?.Subrank));
        }

        public static CampaignEvent RecordProceduralGeneseedRecovery(this PlayerForce force, 
            Date date,
            PlayerSoldier soldier,
            long sourceDeathEventId,
            GeneseedRecoveryOutcome outcome,
            float? purity = null)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            return force.GetCampaignEventRecorder().RecordGeneseedRecovery(
                soldier,
                date,
                new GeneseedRecoveryPayload(null, sourceDeathEventId, outcome, purity));
        }

        public static CampaignEvent RecordMentorAssigned(this PlayerForce force, 
            Date date,
            PlayerSoldier mentee,
            PlayerSoldier mentor,
            Squad scoutSquad)
        {
            if (date == null) throw new ArgumentNullException(nameof(date));
            if (mentee == null) throw new ArgumentNullException(nameof(mentee));
            if (mentor == null) throw new ArgumentNullException(nameof(mentor));
            if (scoutSquad == null) throw new ArgumentNullException(nameof(scoutSquad));
            return force.GetCampaignEventRecorder().RecordMentorAssigned(
                mentee,
                date,
                new MentorAssignedPayload(
                    MentorRelationshipKind.ScoutMentor,
                    MentorAssignmentContext.NeophytePlacement,
                    scoutSquad.Id,
                    scoutSquad.Name,
                    mentor.Id,
                    mentor.Name));
        }


}