using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OnlyWar.Models.Events
{
    // Persisted values are append-only. The values used by SoldierEventType are kept aligned so
    // the format-7 reader can map a legacy soldier row without inventing a second vocabulary.
    public enum CampaignEventType
    {
        Founding = 0,
        AcceptedToTraining = 1,
        PsychicDetected = 2,
        Promotion = 3,
        Transfer = 4,
        RatingFlag = 5,
        AwardReceived = 6,
        BattleParticipation = 7,
        Death = 8,
        GeneseedRecovery = 9,
        Incapacitated = 10,
        FirstBlood = 100,
        KillMilestone = 101,
        LastSurvivor = 102,
        MentorAssigned = 103,
        Oath = 104,
        NearDeathRecovery = 105,
        MissionOutcome = 106,
        SquadHeldAgainstOdds = 107,
        BodyPartReplacement = 108,

        // 2,000-2,099: migration and compatibility facts.
        LegacyChapterHistory = 2000,
        // 2,100-2,199: atomic battle facts.
        BattleResolved = 2100,
        ChapterFounded = 2101,
        // 2,200-2,203: player-visible faction intelligence thresholds.
        FactionPresenceConfirmed = 2200,
        FactionPresenceLocated = 2201,
        FactionPresenceDisproven = 2202,
        FactionFirstContact = 2203,
        FactionRelationshipChanged = 2204
    }

    public enum CampaignEntityKind
    {
        Soldier = 0,
        Squad = 1,
        Faction = 2,
        Planet = 3,
        Region = 4,
        Mission = 5,
        Order = 6,
        Character = 7,
        Chapter = 8
    }

    public enum CampaignEventEntityRole
    {
        Subject = 0,
        Participant = 1,
        Related = 2,
        Opponent = 3,
        Location = 4,
        Authority = 5
    }

    [Flags]
    public enum CampaignEventSurfaceFlags
    {
        None = 0,
        ServiceRecord = 1 << 0,
        TurnReport = 1 << 1,
        ChapterChronicle = 1 << 2
    }

    public enum CampaignEventImportance
    {
        Routine = 0,
        Notable = 1,
        Major = 2,
        Defining = 3
    }

    [Flags]
    public enum CampaignEventReasonFlags
    {
        None = 0,
        FirstBlood = 1 << 0,
        KillMilestone = 1 << 1,
        VeteranDeath = 1 << 2,
        OfficerCasualty = 1 << 3,
        LastSurvivor = 1 << 4,
        FirstContact = 1 << 5,
        WorldChangedHands = 1 << 6,
        SquadHeldAgainstOdds = 1 << 7,
        NearDeathRecovery = 1 << 8,
        BodyPartReplacement = 1 << 9
    }

    public enum CampaignEventChronicleTreatment
    {
        None = 0,
        GroupWithCorrelation = 1,
        Standalone = 2
    }

    public sealed record CampaignEventEntityRef
    {
        public CampaignEntityKind Kind { get; }
        public int EntityId { get; }
        public CampaignEventEntityRole Role { get; }
        public string DisplayNameSnapshot { get; }

        public CampaignEventEntityRef(
            CampaignEntityKind kind,
            int entityId,
            CampaignEventEntityRole role,
            string displayNameSnapshot)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            if (string.IsNullOrWhiteSpace(displayNameSnapshot))
            {
                throw new ArgumentException("An entity display-name snapshot is required.",
                    nameof(displayNameSnapshot));
            }

            Kind = kind;
            EntityId = entityId;
            Role = role;
            DisplayNameSnapshot = displayNameSnapshot;
        }
    }

    public interface ICampaignEventPayload
    {
        CampaignEventType EventType { get; }
        ushort Version { get; }
    }

    public sealed record CampaignEventPublication(
        CampaignEventSurfaceFlags SurfaceFlags,
        CampaignEventImportance Importance,
        CampaignEventReasonFlags ReasonFlags,
        CampaignEventChronicleTreatment ChronicleTreatment,
        int ClassifierVersion)
    {
        public bool PublishesToServiceRecord =>
            SurfaceFlags.HasFlag(CampaignEventSurfaceFlags.ServiceRecord);

        public bool PublishesToTurnReport =>
            SurfaceFlags.HasFlag(CampaignEventSurfaceFlags.TurnReport);

        public bool PublishesToChapterChronicle =>
            SurfaceFlags.HasFlag(CampaignEventSurfaceFlags.ChapterChronicle);

        public static CampaignEventPublication ServiceRecordOnly(
            CampaignEventReasonFlags reasons = CampaignEventReasonFlags.None,
            CampaignEventImportance importance = CampaignEventImportance.Routine) =>
            new(
                CampaignEventSurfaceFlags.ServiceRecord,
                importance,
                reasons,
                CampaignEventChronicleTreatment.None,
                CampaignEventClassifier.CurrentVersion);
    }

    public sealed class CampaignEventCandidate
    {
        public CampaignEventType Type { get; }
        public int OccurredWeek { get; }
        public int RecordedWeek { get; }
        public string CorrelationKey { get; }
        public string DedupeKey { get; }
        public ushort PayloadVersion { get; }
        public ICampaignEventPayload Payload { get; }
        public IReadOnlyList<CampaignEventEntityRef> Entities { get; }
        public CampaignEventSurfaceFlags SurfaceHint { get; }
        public CampaignEventImportance? ImportanceHint { get; }
        public CampaignEventReasonFlags ReasonHint { get; }
        public CampaignEventChronicleTreatment? ChronicleTreatmentHint { get; }

        public CampaignEventCandidate(
            CampaignEventType type,
            int occurredWeek,
            int recordedWeek,
            string correlationKey,
            string dedupeKey,
            ushort payloadVersion,
            ICampaignEventPayload payload,
            IEnumerable<CampaignEventEntityRef> entities = null,
            CampaignEventSurfaceFlags surfaceHint = CampaignEventSurfaceFlags.None,
            CampaignEventImportance? importanceHint = null,
            CampaignEventReasonFlags reasonHint = CampaignEventReasonFlags.None,
            CampaignEventChronicleTreatment? chronicleTreatmentHint = null)
        {
            if (occurredWeek < 0) throw new ArgumentOutOfRangeException(nameof(occurredWeek));
            if (recordedWeek < occurredWeek)
            {
                throw new ArgumentException(
                    "RecordedWeek must be greater than or equal to OccurredWeek.",
                    nameof(recordedWeek));
            }
            if (string.IsNullOrWhiteSpace(dedupeKey))
                throw new ArgumentException("A non-empty dedupe key is required.", nameof(dedupeKey));
            if ((type == CampaignEventType.BattleResolved || type == CampaignEventType.BattleParticipation)
                && string.IsNullOrWhiteSpace(correlationKey))
            {
                throw new ArgumentException(
                    "Battle events require a non-empty correlation key.",
                    nameof(correlationKey));
            }
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.EventType != type)
            {
                throw new ArgumentException(
                    $"Payload type {payload.EventType} does not match event type {type}.",
                    nameof(payload));
            }
            if (payload.Version != payloadVersion)
            {
                throw new ArgumentException(
                    $"Payload version {payload.Version} does not match envelope version {payloadVersion}.",
                    nameof(payloadVersion));
            }
            CampaignEventValidation.ValidatePayloadCorrelation(type, payload, correlationKey);
            if ((chronicleTreatmentHint == CampaignEventChronicleTreatment.GroupWithCorrelation
                 || chronicleTreatmentHint == CampaignEventChronicleTreatment.Standalone)
                && string.IsNullOrWhiteSpace(correlationKey)
                && chronicleTreatmentHint == CampaignEventChronicleTreatment.GroupWithCorrelation)
            {
                throw new ArgumentException(
                    "Grouped Chronicle events require a correlation key.",
                    nameof(correlationKey));
            }

            Type = type;
            OccurredWeek = occurredWeek;
            RecordedWeek = recordedWeek;
            CorrelationKey = string.IsNullOrWhiteSpace(correlationKey) ? null : correlationKey;
            DedupeKey = dedupeKey;
            PayloadVersion = payloadVersion;
            Payload = payload;
            Entities = new ReadOnlyCollection<CampaignEventEntityRef>(
                (entities ?? Enumerable.Empty<CampaignEventEntityRef>()).ToList());
            if (Entities.GroupBy(entity => (entity.Kind, entity.EntityId, entity.Role)).Any(group => group.Count() > 1))
                throw new ArgumentException("An event cannot repeat an entity association.", nameof(entities));
            SurfaceHint = surfaceHint;
            ImportanceHint = importanceHint;
            ReasonHint = reasonHint;
            ChronicleTreatmentHint = chronicleTreatmentHint;
        }
    }

    public sealed class CampaignEvent
    {
        public long Id { get; }
        public CampaignEventType Type { get; }
        public int OccurredWeek { get; }
        public int RecordedWeek { get; }
        public string CorrelationKey { get; }
        public string DedupeKey { get; }
        public ushort PayloadVersion { get; }
        public ICampaignEventPayload Payload { get; }
        public IReadOnlyList<CampaignEventEntityRef> Entities { get; }
        public CampaignEventPublication Publication { get; }

        public CampaignEvent(
            long id,
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
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (occurredWeek < 0) throw new ArgumentOutOfRangeException(nameof(occurredWeek));
            if (recordedWeek < occurredWeek)
            {
                throw new ArgumentException(
                    "RecordedWeek must be greater than or equal to OccurredWeek.",
                    nameof(recordedWeek));
            }
            if (string.IsNullOrWhiteSpace(dedupeKey))
                throw new ArgumentException("A non-empty dedupe key is required.", nameof(dedupeKey));
            if ((type == CampaignEventType.BattleResolved || type == CampaignEventType.BattleParticipation)
                && string.IsNullOrWhiteSpace(correlationKey))
            {
                throw new ArgumentException(
                    "Battle events require a non-empty correlation key.",
                    nameof(correlationKey));
            }
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.EventType != type)
            {
                throw new ArgumentException(
                    $"Payload type {payload.EventType} does not match event type {type}.",
                    nameof(payload));
            }
            if (payload.Version != payloadVersion)
            {
                throw new ArgumentException(
                    $"Payload version {payload.Version} does not match envelope version {payloadVersion}.",
                    nameof(payloadVersion));
            }
            CampaignEventValidation.ValidatePayloadCorrelation(type, payload, correlationKey);
            if (publication == null) throw new ArgumentNullException(nameof(publication));
            if (publication.ChronicleTreatment == CampaignEventChronicleTreatment.GroupWithCorrelation
                && string.IsNullOrWhiteSpace(correlationKey))
            {
                throw new ArgumentException(
                    "Grouped Chronicle events require a correlation key.",
                    nameof(correlationKey));
            }

            Id = id;
            Type = type;
            OccurredWeek = occurredWeek;
            RecordedWeek = recordedWeek;
            CorrelationKey = string.IsNullOrWhiteSpace(correlationKey) ? null : correlationKey;
            DedupeKey = dedupeKey;
            PayloadVersion = payloadVersion;
            Payload = payload;
            Entities = new ReadOnlyCollection<CampaignEventEntityRef>(
                (entities ?? Enumerable.Empty<CampaignEventEntityRef>()).ToList());
            if (Entities.GroupBy(entity => (entity.Kind, entity.EntityId, entity.Role)).Any(group => group.Count() > 1))
                throw new ArgumentException("An event cannot repeat an entity association.", nameof(entities));
            Publication = publication;
        }
    }

    public static class CampaignEventPayloadRegistry
    {
        private sealed record Registration(
            Type PayloadType,
            Func<string, ICampaignEventPayload> Deserialize);

        private static readonly Dictionary<(CampaignEventType Type, ushort Version), Registration> Registrations =
            new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        static CampaignEventPayloadRegistry()
        {
            CampaignEventType[] typedVersionThreeTypes =
            [
                CampaignEventType.BattleParticipation,
                CampaignEventType.Death,
                CampaignEventType.GeneseedRecovery,
                CampaignEventType.Incapacitated,
                CampaignEventType.LastSurvivor,
                CampaignEventType.MentorAssigned,
                CampaignEventType.NearDeathRecovery
            ];
            foreach (CampaignEventType type in Enum.GetValues<CampaignEventType>())
            {
                if (type is CampaignEventType.FirstBlood
                    or CampaignEventType.KillMilestone
                    or CampaignEventType.LegacyChapterHistory
                    or CampaignEventType.BattleResolved
                    or CampaignEventType.ChapterFounded
                    or CampaignEventType.FactionPresenceConfirmed
                    or CampaignEventType.FactionPresenceLocated
                    or CampaignEventType.FactionPresenceDisproven
                    or CampaignEventType.FactionFirstContact
                    or CampaignEventType.FactionRelationshipChanged
                    or CampaignEventType.SquadHeldAgainstOdds
                    or CampaignEventType.BodyPartReplacement)
                {
                    continue;
                }
                Register<LegacySoldierEventPayload>(type, 1);
                Register<LegacySoldierEventPayload>(type, 2);
            }
            Register<FirstBloodPayload>(CampaignEventType.FirstBlood, 1);
            Register<KillMilestonePayload>(CampaignEventType.KillMilestone, 1);
            Register<LegacySoldierEventPayload>(CampaignEventType.FirstBlood, 2);
            Register<LegacySoldierEventPayload>(CampaignEventType.KillMilestone, 2);
            Register<LegacyChapterHistoryPayload>(CampaignEventType.LegacyChapterHistory, 1);
            Register<BattleResolvedPayload>(CampaignEventType.BattleResolved, 1);
            Register<ChapterFoundedPayload>(CampaignEventType.ChapterFounded, 1);
            Register<FactionIntelEventPayload>(CampaignEventType.FactionPresenceConfirmed, 1);
            Register<FactionIntelEventPayload>(CampaignEventType.FactionPresenceLocated, 1);
            Register<FactionIntelEventPayload>(CampaignEventType.FactionPresenceDisproven, 1);
            Register<FactionIntelEventPayload>(CampaignEventType.FactionFirstContact, 1);
            Register<FactionRelationshipEventPayload>(CampaignEventType.FactionRelationshipChanged, 1);
            foreach (CampaignEventType type in typedVersionThreeTypes)
            {
                RegisterTypedVersionThree(type);
            }
            Register<SquadHeldAgainstOddsPayload>(CampaignEventType.SquadHeldAgainstOdds, 1);
            Register<BodyPartReplacementPayload>(CampaignEventType.BodyPartReplacement, 1);
        }

        private static void RegisterTypedVersionThree(CampaignEventType type)
        {
            switch (type)
            {
                case CampaignEventType.BattleParticipation:
                    Register<BattleParticipationPayload>(type, 3);
                    break;
                case CampaignEventType.Death:
                    Register<DeathPayload>(type, 3);
                    break;
                case CampaignEventType.GeneseedRecovery:
                    Register<GeneseedRecoveryPayload>(type, 3);
                    break;
                case CampaignEventType.Incapacitated:
                    Register<IncapacitatedPayload>(type, 3);
                    break;
                case CampaignEventType.LastSurvivor:
                    Register<LastSurvivorPayload>(type, 3);
                    break;
                case CampaignEventType.MentorAssigned:
                    Register<MentorAssignedPayload>(type, 3);
                    break;
                case CampaignEventType.NearDeathRecovery:
                    Register<NearDeathRecoveryPayload>(type, 3);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static void Register<TPayload>(CampaignEventType type, ushort version)
            where TPayload : ICampaignEventPayload
        {
            if (version == 0) throw new ArgumentOutOfRangeException(nameof(version));
            Registrations[(type, version)] = new Registration(
                typeof(TPayload),
                json => JsonSerializer.Deserialize<TPayload>(json, JsonOptions)
                    ?? throw new InvalidDataException("The event payload was empty."));
        }

        public static string Serialize(CampaignEvent @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            Registration registration = GetRegistration(@event.Type, @event.PayloadVersion);
            if (!registration.PayloadType.IsInstanceOfType(@event.Payload))
            {
                throw new InvalidDataException(
                    $"Event {@event.Id} type {@event.Type} has payload CLR type "
                    + $"{@event.Payload.GetType().Name}, expected {registration.PayloadType.Name}.");
            }
            return JsonSerializer.Serialize(@event.Payload, registration.PayloadType, JsonOptions);
        }

        public static ICampaignEventPayload Deserialize(
            long eventId,
            CampaignEventType type,
            ushort version,
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException($"Event {eventId} type {type} has an empty payload.");

            Registration registration;
            try
            {
                registration = GetRegistration(type, version);
                return registration.Deserialize(json);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                throw new InvalidDataException(
                    $"Event {eventId} type {type} payload version {version} could not be loaded: "
                    + exception.Message,
                    exception);
            }
        }

        private static Registration GetRegistration(CampaignEventType type, ushort version)
        {
            if (!Registrations.TryGetValue((type, version), out Registration registration))
            {
                throw new InvalidDataException(
                    $"Unsupported campaign event payload type {type}, version {version}.");
            }
            return registration;
        }
    }

    internal static class CampaignEventValidation
    {
        internal static void ValidatePayloadCorrelation(
            CampaignEventType type,
            ICampaignEventPayload payload,
            string correlationKey)
        {
            if (payload is IncapacitatedPayload nearDeathPayload
                && nearDeathPayload.QualifiesAsNearDeath
                && (!nearDeathPayload.DefiningLocationIsVital
                    || !nearDeathPayload.DefiningLocationWasCrippled
                    || nearDeathPayload.DefiningLocationWasSevered))
            {
                throw new ArgumentException(
                    "Only a crippled, non-severed vital location can qualify as near death.",
                    nameof(payload));
            }

            BattleEventContextSnapshot context = payload switch
            {
                BattleParticipationPayload battle => battle.BattleContext,
                IncapacitatedPayload incapacitated => incapacitated.BattleContext,
                DeathPayload death => death.BattleContext,
                GeneseedRecoveryPayload geneseed => geneseed.BattleContext,
                LastSurvivorPayload survivor => survivor.BattleContext,
                SquadHeldAgainstOddsPayload held => held.BattleContext,
                NearDeathRecoveryPayload recovery => recovery.BattleContext,
                _ => null
            };
            if (context == null) return;
            if (string.IsNullOrWhiteSpace(correlationKey))
            {
                throw new ArgumentException(
                    $"Typed battle payload {type} requires a correlation key.",
                    nameof(correlationKey));
            }
            if (!string.Equals(context.CorrelationKey, correlationKey, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Typed battle payload {type} correlation does not match its envelope.",
                    nameof(payload));
            }

        }
    }
}
