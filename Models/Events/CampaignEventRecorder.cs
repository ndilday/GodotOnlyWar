using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnlyWar.Models.Events
{
    public sealed class CampaignEventRecorder
    {
        private readonly CampaignEventLedger _ledger;
        private readonly CampaignEventClassifier _classifier;
        private readonly TurnEventBuffer _turnBuffer;
        private readonly ChapterChronicleLedger _chronicle;
        private readonly Func<int, PlayerSoldier> _soldierResolver;
        private readonly HashSet<(int SoldierId, long EventId)> _projectedSoldiers = new();
        private CampaignIdentity _campaignIdentity = CampaignIdentity.Empty;

        public CampaignEventRecorder(
            CampaignEventLedger ledger,
            CampaignEventClassifier classifier = null,
            TurnEventBuffer turnBuffer = null,
            ChapterChronicleLedger chronicle = null,
            Func<int, PlayerSoldier> soldierResolver = null)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _classifier = classifier ?? new CampaignEventClassifier();
            _turnBuffer = turnBuffer;
            _chronicle = chronicle;
            _soldierResolver = soldierResolver;
        }

        public CampaignEventLedger Ledger => _ledger;
        public TurnEventBuffer TurnBuffer => _turnBuffer;

        /// <summary>
        /// Records a threshold transition visible to the Chapter. Ordinary evidence refreshes and
        /// weekly decay never call this boundary; the PlanetFaction event supplies only a before /
        /// after level transition, so false reports are narrated exactly like true reports.
        /// </summary>
        public CampaignEvent RecordFactionIntel(
            FactionIntelChangedEventArgs change,
            int planetId,
            int occurredWeek)
        {
            if (change?.Current == null && change?.Previous == null) return null;
            FactionIntelBelief belief = change.Current ?? change.Previous;
            IntelLevel previous = change.PreviousLevel;
            IntelLevel current = change.CurrentLevel;
            CampaignEventType? type = null;
            if (previous < IntelLevel.Confirmed && current >= IntelLevel.Confirmed)
            {
                type = CampaignEventType.FactionPresenceConfirmed;
            }
            else if (previous < IntelLevel.Located && current == IntelLevel.Located)
            {
                type = CampaignEventType.FactionPresenceLocated;
            }
            else if (change.Observation.Source != IntelObservationSource.Decay
                && previous >= IntelLevel.Confirmed
                && current < IntelLevel.Confirmed)
            {
                type = CampaignEventType.FactionPresenceDisproven;
            }

            if (!type.HasValue) return null;
            FactionIntelEventPayload payload = new(
                planetId,
                belief.Region.Id,
                change.Observation.Observer.Faction.Id,
                belief.TargetFaction.Id,
                previous,
                current,
                change.Current?.Evidence ?? 0f,
                false,
                type.Value);
            CampaignEvent @event = Record(new CampaignEventCandidate(
                type.Value,
                occurredWeek,
                occurredWeek,
                null,
                $"intel/{type.Value}/{change.Observation.Observer.Faction.Id}/"
                    + $"{belief.Region.Id}/{belief.TargetFaction.Id}/{occurredWeek}",
                1,
                payload,
                [
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Faction,
                        belief.TargetFaction.Id,
                        CampaignEventEntityRole.Subject,
                        belief.TargetFaction.Name),
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Faction,
                        change.Observation.Observer.Faction.Id,
                        CampaignEventEntityRole.Related,
                        change.Observation.Observer.Faction.Name),
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Region,
                        belief.Region.Id,
                        CampaignEventEntityRole.Location,
                        belief.Region.Name),
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Planet,
                        planetId,
                        CampaignEventEntityRole.Location,
                        belief.Region.Planet?.Name ?? $"Planet {planetId}")
                ],
                surfaceHint: CampaignEventSurfaceFlags.TurnReport,
                importanceHint: current >= IntelLevel.Located
                    ? CampaignEventImportance.Notable
                    : CampaignEventImportance.Routine));

            if (type == CampaignEventType.FactionPresenceConfirmed)
            {
                Record(new CampaignEventCandidate(
                    CampaignEventType.FactionFirstContact,
                    occurredWeek,
                    occurredWeek,
                    null,
                    $"intel/first-contact/{belief.TargetFaction.Id}",
                    1,
                    new FactionIntelEventPayload(
                        planetId,
                        belief.Region.Id,
                        change.Observation.Observer.Faction.Id,
                        belief.TargetFaction.Id,
                        previous,
                        current,
                        change.Current?.Evidence ?? 0f,
                        true,
                        CampaignEventType.FactionFirstContact),
                    entities: @event.Entities));
            }

            return @event;
        }

        public CampaignEvent RecordFactionRelationship(
            FactionRelationshipChangedEventArgs change,
            Faction lowerFaction,
            Faction higherFaction,
            int occurredWeek)
        {
            if (change == null || lowerFaction == null || higherFaction == null) return null;

            FactionRelationshipEventPayload payload = new(
                change.Pair.LowerFactionId,
                change.Pair.HigherFactionId,
                change.PreviousStance,
                change.CurrentStance);
            return Record(new CampaignEventCandidate(
                CampaignEventType.FactionRelationshipChanged,
                occurredWeek,
                occurredWeek,
                null,
                $"relationship/{change.Pair.LowerFactionId}/{change.Pair.HigherFactionId}/"
                    + $"{change.PreviousStance}/{change.CurrentStance}/{occurredWeek}",
                1,
                payload,
                [
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Faction,
                        lowerFaction.Id,
                        CampaignEventEntityRole.Subject,
                        lowerFaction.Name),
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Faction,
                        higherFaction.Id,
                        CampaignEventEntityRole.Opponent,
                        higherFaction.Name)
                ],
                surfaceHint: CampaignEventSurfaceFlags.TurnReport,
                importanceHint: CampaignEventImportance.Notable));
        }

        public CampaignEvent RecordChapterFounded(
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
            return Record(new CampaignEventCandidate(
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

        internal void SetCampaignIdentity(CampaignIdentity identity)
        {
            _campaignIdentity = identity ?? CampaignIdentity.Empty;
        }

        public CampaignEvent Record(CampaignEventCandidate candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            CampaignEvent existing = _ledger.GetByDedupeKey(candidate.DedupeKey);
            if (existing != null) return existing;

            CampaignEventPublication publication = _classifier.Classify(candidate);
            CampaignEvent @event = _ledger.Allocate(
                candidate.Type,
                candidate.OccurredWeek,
                candidate.RecordedWeek,
                candidate.CorrelationKey,
                candidate.DedupeKey,
                candidate.PayloadVersion,
                candidate.Payload,
                candidate.Entities,
                publication);
            ProjectToSoldiers(@event);
            _turnBuffer?.Add(@event);
            ChapterChronicleProjector.ProjectEvent(
                _ledger,
                _chronicle,
                @event,
                _campaignIdentity);
            return @event;
        }

        internal CampaignEvent RecordLegacySoldierEvent(
            PlayerSoldier soldier,
            SoldierEvent soldierEvent)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (soldierEvent == null) throw new ArgumentNullException(nameof(soldierEvent));
            if (soldierEvent.CampaignEventId > 0)
            {
                return _ledger.GetById(soldierEvent.CampaignEventId)
                    ?? throw new InvalidDataException(
                        $"Soldier {soldier.Id} references missing campaign event {soldierEvent.CampaignEventId}.");
            }

            CampaignEventType type = Enum.IsDefined(typeof(CampaignEventType), (int)soldierEvent.Type)
                ? (CampaignEventType)(int)soldierEvent.Type
                : CampaignEventType.MissionOutcome;
            int occurredWeek = soldierEvent.Date?.GetTotalWeeks() ?? 0;
            int ordinal = soldier.SoldierEvents.Count;
            string dedupeKey = $"legacy/runtime/soldier/{soldier.Id}/{ordinal}";
            List<CampaignEventEntityRef> entities =
            [
                new CampaignEventEntityRef(
                    CampaignEntityKind.Soldier,
                    soldier.Id,
                    CampaignEventEntityRole.Subject,
                    soldier.Name)
            ];
            foreach (int relatedSoldierId in (soldierEvent.RelatedSoldierIds ?? [])
                         .Distinct()
                         .Where(id => id != soldier.Id))
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Soldier,
                    relatedSoldierId,
                    CampaignEventEntityRole.Related,
                    _soldierResolver?.Invoke(relatedSoldierId)?.Name
                        ?? $"Soldier {relatedSoldierId}"));
            }

            CampaignEventCandidate candidate = new(
                type,
                occurredWeek,
                occurredWeek,
                correlationKey: type is CampaignEventType.BattleParticipation
                    or CampaignEventType.FirstBlood
                    or CampaignEventType.KillMilestone
                    ? $"legacy/runtime/battle/{soldier.Id}/{ordinal}"
                    : null,
                dedupeKey: dedupeKey,
                payloadVersion: 2,
                payload: new LegacySoldierEventPayload(
                    type,
                    soldierEvent.Detail,
                    soldierEvent.FactionId,
                    soldierEvent.WeaponTemplateId,
                    soldierEvent.Magnitude,
                    soldierEvent.LocationName,
                    soldierEvent.RelatedSoldierIds,
                    2),
                entities: entities,
                surfaceHint: soldierEvent.Type == SoldierEventType.Death
                    ? CampaignEventSurfaceFlags.ServiceRecord | CampaignEventSurfaceFlags.ChapterChronicle
                    : CampaignEventSurfaceFlags.ServiceRecord,
                importanceHint: soldierEvent.Type == SoldierEventType.Death
                    ? CampaignEventImportance.Major
                    : null,
                chronicleTreatmentHint: soldierEvent.Type == SoldierEventType.Death
                    ? CampaignEventChronicleTreatment.Standalone
                    : null);
            return Record(candidate);
        }

        public IReadOnlyList<CampaignEvent> RecordKillMilestones(
            PlayerSoldier soldier,
            int previousTotal,
            int newTotal,
            int? opposingFactionId,
            int? weaponTemplateId,
            int occurredWeek,
            string correlationKey,
            string victimDisplayName = null,
            IEnumerable<CampaignEventEntityRef> locationEntities = null,
            KillMilestoneRules rules = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (previousTotal < 0 || newTotal < previousTotal)
                throw new ArgumentOutOfRangeException(nameof(newTotal));

            List<CampaignEvent> emitted = new();
            if (previousTotal == 0 && newTotal > 0)
            {
                CampaignEventCandidate candidate = new(
                    CampaignEventType.FirstBlood,
                    occurredWeek,
                    occurredWeek,
                    correlationKey,
                    $"career/first-blood/{soldier.Id}",
                    1,
                    new FirstBloodPayload(newTotal, opposingFactionId, weaponTemplateId, victimDisplayName),
                    BuildSoldierEntities(soldier, locationEntities),
                    reasonHint: CampaignEventReasonFlags.FirstBlood,
                    chronicleTreatmentHint: CampaignEventChronicleTreatment.GroupWithCorrelation);
                if (_ledger.GetByDedupeKey(candidate.DedupeKey) == null)
                    emitted.Add(Record(candidate));
            }

            foreach (KillMilestoneRule rule in (rules ?? _classifier.MilestoneRules).Rules)
            {
                if (previousTotal < rule.Threshold && rule.Threshold <= newTotal)
                {
                    CampaignEventCandidate candidate = new(
                        CampaignEventType.KillMilestone,
                        occurredWeek,
                        occurredWeek,
                        correlationKey,
                        $"career/kill-milestone/{soldier.Id}/{rule.Threshold}",
                        1,
                        new KillMilestonePayload(
                            rule.Threshold,
                            previousTotal,
                            newTotal,
                            opposingFactionId,
                            weaponTemplateId),
                        BuildSoldierEntities(soldier, locationEntities),
                        importanceHint: rule.Importance,
                        reasonHint: CampaignEventReasonFlags.KillMilestone,
                        chronicleTreatmentHint: rule.ChronicleTreatment);
                    if (_ledger.GetByDedupeKey(candidate.DedupeKey) == null)
                        emitted.Add(Record(candidate));
                }
            }
            return emitted;
        }

        public CampaignEvent RecordBattleParticipation(
            PlayerSoldier soldier,
            BattleEventContextSnapshot battleContext,
            int enemiesTakenDown,
            int woundsReceived,
            int? opposingFactionId,
            string opposingFactionName,
            IEnumerable<CampaignEventEntityRef> additionalEntities = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (battleContext == null) throw new ArgumentNullException(nameof(battleContext));
            CampaignEventCandidate candidate = new(
                CampaignEventType.BattleParticipation,
                battleContext.OccurredWeek,
                battleContext.OccurredWeek,
                battleContext.CorrelationKey,
                $"battle/{battleContext.CorrelationKey}/soldier/{soldier.Id}/participation",
                3,
                new BattleParticipationPayload(
                    battleContext,
                    Math.Max(0, enemiesTakenDown),
                    Math.Max(0, woundsReceived),
                    opposingFactionId,
                    opposingFactionName),
                BuildBattleEntities(soldier, battleContext, additionalEntities));
            return Record(candidate);
        }

        public CampaignEvent RecordIncapacitated(
            PlayerSoldier soldier,
            BattleEventContextSnapshot battleContext,
            HitLocation definingLocation,
            WeaponTemplate causingWeapon,
            bool qualifiesAsNearDeath,
            int? soldierTemplateId,
            string soldierTemplateName,
            int? soldierRank,
            IEnumerable<CampaignEventEntityRef> additionalEntities = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (battleContext == null) throw new ArgumentNullException(nameof(battleContext));
            bool derivedNearDeath = definingLocation?.Template?.IsVital == true
                && definingLocation.IsCrippled
                && !definingLocation.IsSevered;
            IncapacitatedPayload payload = new(
                battleContext,
                definingLocation?.Template?.Id,
                definingLocation?.Template?.Name,
                definingLocation?.Template?.IsVital == true,
                definingLocation?.IsCrippled == true,
                definingLocation?.IsSevered == true,
                derivedNearDeath,
                causingWeapon?.Id,
                causingWeapon?.Name,
                soldierTemplateId,
                soldierTemplateName,
                soldierRank);
            return Record(new CampaignEventCandidate(
                CampaignEventType.Incapacitated,
                battleContext.OccurredWeek,
                battleContext.OccurredWeek,
                battleContext.CorrelationKey,
                $"battle/{battleContext.CorrelationKey}/soldier/{soldier.Id}/incapacitated",
                3,
                payload,
                BuildBattleEntities(soldier, battleContext, additionalEntities)));
        }

        public CampaignEvent RecordDeath(
            PlayerSoldier soldier,
            Date occurredDate,
            DeathPayload payload,
            IEnumerable<CampaignEventEntityRef> additionalEntities = null,
            string dedupeKey = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (occurredDate == null) throw new ArgumentNullException(nameof(occurredDate));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            BattleEventContextSnapshot context = payload.BattleContext;
            int occurredWeek = context?.OccurredWeek ?? occurredDate.GetTotalWeeks();
            return Record(new CampaignEventCandidate(
                CampaignEventType.Death,
                occurredWeek,
                occurredDate.GetTotalWeeks(),
                context?.CorrelationKey,
                dedupeKey ?? (context == null
                    ? $"career/death/{soldier.Id}/{occurredWeek}"
                    : $"battle/{context.CorrelationKey}/soldier/{soldier.Id}/death"),
                3,
                payload,
                BuildBattleEntities(soldier, context, additionalEntities)));
        }

        public CampaignEvent RecordSquadLeaderUnavailable(
            PlayerSoldier soldier,
            int squadId,
            string squadName,
            bool wasActualLeader,
            BattleEventContextSnapshot battleContext = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            int occurredWeek = battleContext?.OccurredWeek ?? 0;
            SquadLeaderUnavailablePayload payload = new(
                squadId,
                string.IsNullOrWhiteSpace(squadName) ? $"Squad {squadId}" : squadName,
                soldier.Template?.Rank ?? 0,
                soldier.Template?.Subrank ?? 0,
                wasActualLeader,
                soldier.IsDeployable,
                battleContext);
            return Record(new CampaignEventCandidate(
                CampaignEventType.SquadLeaderUnavailable,
                occurredWeek,
                occurredWeek,
                battleContext?.CorrelationKey,
                $"squad-leader-unavailable/{squadId}/{soldier.Id}/{occurredWeek}",
                1,
                payload,
                BuildBattleEntities(soldier, battleContext,
                [new CampaignEventEntityRef(
                    CampaignEntityKind.Squad,
                    squadId,
                    CampaignEventEntityRole.Related,
                    payload.SquadName)])));
        }

        public CampaignEvent RecordHiddenCultRevealed(
            int planetId,
            string planetName,
            int factionId,
            string factionName,
            int occurredWeek)
        {
            HiddenCultRevealedPayload payload = new(
                planetId, planetName, factionId, factionName, occurredWeek);
            return Record(new CampaignEventCandidate(
                CampaignEventType.HiddenCultRevealed,
                occurredWeek,
                occurredWeek,
                null,
                $"cult-revealed/{factionId}/{planetId}",
                1,
                payload,
                [
                    new CampaignEventEntityRef(CampaignEntityKind.Faction, factionId,
                        CampaignEventEntityRole.Subject, factionName),
                    new CampaignEventEntityRef(CampaignEntityKind.Planet, planetId,
                        CampaignEventEntityRole.Location, planetName)
                ]));
        }

        public CampaignEvent RecordWorldControlChanged(WorldControlChangedPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Record(new CampaignEventCandidate(
                payload.EventType,
                payload.EpisodeCompletedWeek,
                payload.EpisodeCompletedWeek,
                $"world-control/{payload.PlanetId}/{payload.EpisodeStartedWeek}",
                $"world-control/{payload.PlanetId}/{payload.EpisodeStartedWeek}/{payload.EventType}",
                1,
                payload,
                [new CampaignEventEntityRef(CampaignEntityKind.Planet, payload.PlanetId,
                    CampaignEventEntityRole.Subject, payload.PlanetName)]));
        }

        public CampaignEvent RecordGeneseedRecovery(
            PlayerSoldier soldier,
            Date occurredDate,
            GeneseedRecoveryPayload payload,
            IEnumerable<CampaignEventEntityRef> additionalEntities = null,
            string dedupeKey = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (occurredDate == null) throw new ArgumentNullException(nameof(occurredDate));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            BattleEventContextSnapshot context = payload.BattleContext;
            return Record(new CampaignEventCandidate(
                CampaignEventType.GeneseedRecovery,
                context?.OccurredWeek ?? occurredDate.GetTotalWeeks(),
                occurredDate.GetTotalWeeks(),
                context?.CorrelationKey,
                dedupeKey ?? $"career/geneseed/{soldier.Id}/{payload.SourceDeathEventId}",
                3,
                payload,
                BuildBattleEntities(soldier, context, additionalEntities)));
        }

        public CampaignEvent RecordLastSurvivor(
            PlayerSoldier soldier,
            LastSurvivorPayload payload,
            IEnumerable<CampaignEventEntityRef> additionalEntities = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Record(new CampaignEventCandidate(
                CampaignEventType.LastSurvivor,
                payload.BattleContext.OccurredWeek,
                payload.BattleContext.OccurredWeek,
                payload.BattleContext.CorrelationKey,
                $"battle/{payload.BattleContext.CorrelationKey}/last-survivor/{soldier.Id}",
                3,
                payload,
                BuildBattleEntities(soldier, payload.BattleContext, additionalEntities),
                reasonHint: CampaignEventReasonFlags.LastSurvivor));
        }

        public CampaignEvent RecordSquadHeldAgainstOdds(
            int squadId,
            string squadName,
            SquadHeldAgainstOddsPayload payload,
            IEnumerable<PlayerSoldier> participants,
            IEnumerable<CampaignEventEntityRef> additionalEntities = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (squadId < 0) throw new ArgumentOutOfRangeException(nameof(squadId));
            List<CampaignEventEntityRef> entities = new()
            {
                new CampaignEventEntityRef(
                    CampaignEntityKind.Squad,
                    squadId,
                    CampaignEventEntityRole.Subject,
                    string.IsNullOrWhiteSpace(squadName) ? $"Squad {squadId}" : squadName)
            };
            entities.AddRange(BuildContextEntities(payload.BattleContext));
            entities.AddRange(additionalEntities ?? Enumerable.Empty<CampaignEventEntityRef>());
            foreach (PlayerSoldier participant in (participants ?? Enumerable.Empty<PlayerSoldier>()).Distinct())
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Soldier,
                    participant.Id,
                    CampaignEventEntityRole.Participant,
                    participant.Name));
            }
            return Record(new CampaignEventCandidate(
                CampaignEventType.SquadHeldAgainstOdds,
                payload.BattleContext.OccurredWeek,
                payload.BattleContext.OccurredWeek,
                payload.BattleContext.CorrelationKey,
                $"battle/{payload.BattleContext.CorrelationKey}/squad/{squadId}/held-against-odds",
                1,
                payload,
                entities,
                reasonHint: CampaignEventReasonFlags.SquadHeldAgainstOdds));
        }

        public CampaignEvent RecordMentorAssigned(
            PlayerSoldier mentee,
            Date occurredDate,
            MentorAssignedPayload payload)
        {
            if (mentee == null) throw new ArgumentNullException(nameof(mentee));
            if (occurredDate == null) throw new ArgumentNullException(nameof(occurredDate));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            int occurredWeek = occurredDate.GetTotalWeeks();
            return Record(new CampaignEventCandidate(
                CampaignEventType.MentorAssigned,
                occurredWeek,
                occurredWeek,
                null,
                $"career/mentor-assigned/{mentee.Id}/{payload.ScoutSquadId}",
                3,
                payload,
                [
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Soldier,
                        mentee.Id,
                        CampaignEventEntityRole.Subject,
                        mentee.Name),
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Soldier,
                        payload.MentorSoldierId,
                        CampaignEventEntityRole.Related,
                        payload.MentorDisplayName),
                    new CampaignEventEntityRef(
                        CampaignEntityKind.Squad,
                        payload.ScoutSquadId,
                        CampaignEventEntityRole.Related,
                        payload.ScoutSquadName)
                ]));
        }

        public CampaignEvent RecordNearDeathRecovery(
            PlayerSoldier soldier,
            Date occurredDate,
            NearDeathRecoveryPayload payload)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (occurredDate == null) throw new ArgumentNullException(nameof(occurredDate));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            BattleEventContextSnapshot context = payload.BattleContext;
            return Record(new CampaignEventCandidate(
                CampaignEventType.NearDeathRecovery,
                occurredDate.GetTotalWeeks(),
                occurredDate.GetTotalWeeks(),
                context?.CorrelationKey,
                $"medical/near-death-recovery/{payload.SourceIncapacitationEventId}",
                3,
                payload,
                BuildBattleEntities(soldier, context, null),
                reasonHint: CampaignEventReasonFlags.NearDeathRecovery));
        }

        public CampaignEvent RecordBodyPartReplacement(
            PlayerSoldier soldier,
            Date occurredDate,
            BodyPartReplacementPayload payload,
            BattleEventContextSnapshot battleContext = null)
        {
            if (soldier == null) throw new ArgumentNullException(nameof(soldier));
            if (occurredDate == null) throw new ArgumentNullException(nameof(occurredDate));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Record(new CampaignEventCandidate(
                CampaignEventType.BodyPartReplacement,
                occurredDate.GetTotalWeeks(),
                occurredDate.GetTotalWeeks(),
                battleContext?.CorrelationKey,
                $"medical/replacement/{soldier.Id}/{payload.PrimaryHitLocationTemplateId}/"
                    + $"{payload.ReplacementMethod}/{occurredDate.GetTotalWeeks()}",
                1,
                payload,
                BuildBattleEntities(soldier, battleContext, null),
                reasonHint: CampaignEventReasonFlags.BodyPartReplacement));
        }

        private void ProjectToSoldiers(CampaignEvent @event)
        {
            if (_soldierResolver == null) return;
            foreach (CampaignEventEntityRef entity in @event.Entities
                         .Where(entity => entity.Kind == CampaignEntityKind.Soldier
                             && (entity.Role == CampaignEventEntityRole.Subject
                                 || entity.Role == CampaignEventEntityRole.Participant)))
            {
                PlayerSoldier soldier = _soldierResolver(entity.EntityId);
                if (soldier == null || !_projectedSoldiers.Add((soldier.Id, @event.Id))) continue;
                soldier.AddEvent(CampaignEventProjection.ToSoldierEvent(@event, _campaignIdentity));
            }
        }

        private static IEnumerable<CampaignEventEntityRef> BuildSoldierEntities(
            PlayerSoldier soldier,
            IEnumerable<CampaignEventEntityRef> locationEntities)
        {
            return new[]
            {
                new CampaignEventEntityRef(
                    CampaignEntityKind.Soldier,
                    soldier.Id,
                    CampaignEventEntityRole.Subject,
                    soldier.Name)
            }.Concat(locationEntities ?? Enumerable.Empty<CampaignEventEntityRef>());
        }

        private static IEnumerable<CampaignEventEntityRef> BuildBattleEntities(
            PlayerSoldier soldier,
            BattleEventContextSnapshot context,
            IEnumerable<CampaignEventEntityRef> additionalEntities)
        {
            IEnumerable<CampaignEventEntityRef> baseEntities = new[]
            {
                new CampaignEventEntityRef(
                    CampaignEntityKind.Soldier,
                    soldier.Id,
                    CampaignEventEntityRole.Subject,
                    soldier.Name)
            };
            return baseEntities
                .Concat(BuildContextEntities(context))
                .Concat(additionalEntities ?? Enumerable.Empty<CampaignEventEntityRef>());
        }

        private static IEnumerable<CampaignEventEntityRef> BuildContextEntities(
            BattleEventContextSnapshot context)
        {
            if (context == null) return Enumerable.Empty<CampaignEventEntityRef>();
            List<CampaignEventEntityRef> entities = new();
            if (context.OpposingFactionId.HasValue
                && !string.IsNullOrWhiteSpace(context.OpposingFactionName))
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Faction,
                    context.OpposingFactionId.Value,
                    CampaignEventEntityRole.Opponent,
                    context.OpposingFactionName));
            }
            if (context.RegionId.HasValue && !string.IsNullOrWhiteSpace(context.RegionName))
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Region,
                    context.RegionId.Value,
                    CampaignEventEntityRole.Location,
                    context.RegionName));
            }
            if (context.PlanetId.HasValue && !string.IsNullOrWhiteSpace(context.PlanetName))
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Planet,
                    context.PlanetId.Value,
                    CampaignEventEntityRole.Location,
                    context.PlanetName));
            }
            if (context.MissionId.HasValue)
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Mission,
                    context.MissionId.Value,
                    CampaignEventEntityRole.Related,
                    context.MissionName ?? context.MissionType?.ToString() ?? "Mission"));
            }
            if (context.OrderId.HasValue)
            {
                entities.Add(new CampaignEventEntityRef(
                    CampaignEntityKind.Order,
                    context.OrderId.Value,
                    CampaignEventEntityRole.Related,
                    context.OrderName ?? "Order"));
            }
            return entities;
        }
    }

    public static class CampaignEventProjection
    {
        public static SoldierEvent ToSoldierEvent(
            CampaignEvent @event,
            CampaignIdentity identity = null)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            Date date = Date.FromTotalWeeks(Math.Max(1, @event.OccurredWeek));
            string detail = @event.Payload switch
            {
                LegacySoldierEventPayload legacy => legacy.Detail,
                _ => CampaignEventNarrator.RenderServiceRecord(@event, identity)
            };
            int? factionId = @event.Payload switch
            {
                LegacySoldierEventPayload legacy => legacy.FactionId,
                FirstBloodPayload first => first.OpposingFactionId,
                KillMilestonePayload milestone => milestone.OpposingFactionId,
                BattleParticipationPayload participation => participation.OpposingFactionId,
                IncapacitatedPayload incapacitated => incapacitated.BattleContext?.OpposingFactionId,
                DeathPayload death => death.OpposingFactionId,
                GeneseedRecoveryPayload geneseed => geneseed.BattleContext?.OpposingFactionId,
                LastSurvivorPayload survivor => survivor.BattleContext?.OpposingFactionId,
                SquadHeldAgainstOddsPayload held => held.BattleContext?.OpposingFactionId,
                NearDeathRecoveryPayload recovery => recovery.BattleContext?.OpposingFactionId,
                _ => null
            };
            int? weaponId = @event.Payload switch
            {
                LegacySoldierEventPayload legacy => legacy.WeaponTemplateId,
                FirstBloodPayload first => first.WeaponTemplateId,
                KillMilestonePayload milestone => milestone.WeaponTemplateId,
                IncapacitatedPayload incapacitated => incapacitated.CausingWeaponTemplateId,
                DeathPayload death => death.CausingWeaponTemplateId,
                _ => null
            };
            int? magnitude = @event.Payload switch
            {
                LegacySoldierEventPayload legacy => legacy.Magnitude,
                FirstBloodPayload first => first.NewCumulativeTotal,
                KillMilestonePayload milestone => milestone.Threshold,
                BattleParticipationPayload participation => participation.EnemiesTakenDown,
                SquadHeldAgainstOddsPayload held => held.KilledCount + held.IncapacitatedCount,
                LastSurvivorPayload survivor => survivor.KilledCount + survivor.IncapacitatedCount,
                NearDeathRecoveryPayload recovery => recovery.RecoveryDurationWeeks,
                _ => null
            };
            string location = (@event.Entities ?? Array.Empty<CampaignEventEntityRef>())
                .FirstOrDefault(entity => entity.Role == CampaignEventEntityRole.Location)
                ?.DisplayNameSnapshot
                ?? (@event.Payload as LegacySoldierEventPayload)?.LocationName;
            IReadOnlyList<int> related = @event.Payload is LegacySoldierEventPayload old
                ? old.RelatedSoldierIds ?? Array.Empty<int>()
                : (@event.Entities ?? Array.Empty<CampaignEventEntityRef>())
                    .Where(entity => entity.Kind == CampaignEntityKind.Soldier
                        && entity.Role == CampaignEventEntityRole.Related)
                    .Select(entity => entity.EntityId)
                    .Distinct()
                    .ToList();
            SoldierEventType type = Enum.IsDefined(typeof(SoldierEventType), (int)@event.Type)
                ? (SoldierEventType)(int)@event.Type
                : SoldierEventType.MissionOutcome;
            return new SoldierEvent(
                date,
                type,
                detail,
                factionId,
                weaponId,
                magnitude,
                location,
                related,
                @event.Id);
        }
    }
}
