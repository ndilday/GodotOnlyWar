using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using System;
using System.Linq;

namespace OnlyWar.Models
{
    public enum RequestFulfillmentKind
    {
        ForceCommitment = 0,
        ThreatSuppressed = 1
    }

    public enum RequestStatus
    {
        Open = 0,
        InProgress = 1,
        Fulfilled = 2,
        Failed = 3
    }

    public enum RequestSeverity
    {
        Concerned = 0,
        Serious = 1,
        Desperate = 2,
        Existential = 3
    }

    public enum RequestHazard
    {
        Routine = 0,
        Dangerous = 1,
        Extreme = 2
    }

    public interface IRequest
    {
        int Id { get; }
        Planet TargetPlanet { get; }
        Character Requester { get; }
        Faction ThreatFaction { get; }
        Date DateRequestMade { get; }
        Date DateRequestFulfilled { get; }
        Date DateRequestResolved { get; }
        Date Deadline { get; }
        RequestFulfillmentKind FulfillmentKind { get; }
        RequestStatus Status { get; }
        ForceCommitmentPackage Commitment { get; }
        long ProgressBattleValueTime { get; }
        int OfferedRequisition { get; }
        PledgeScheduleKind OfferedScheduleKind { get; }
        int OfferedCadenceWeeks { get; }
        int OfferedDeliveryDelayWeeks { get; }
        RequestSeverity Severity { get; }
        RequestHazard Hazard { get; }
        bool HasPlayerResponded { get; }
        bool IsRequestStarted();
        bool IsRequestCompleted();
        void ProcessTurn(Date currentDate);
        void Fail(Date currentDate);
    }

    /// <summary>
    /// The first governor-request vertical slice. A confirmed threat is outcome based;
    /// a false alarm asks for a sustained, strength-weighted Astartes presence. Both use
    /// the same snapshotted commitment and offer model.
    /// </summary>
    public class PresenceRequest : IRequest
    {
        public int Id { get; }
        public Planet TargetPlanet { get; }
        public Character Requester { get; }
        public Faction ThreatFaction { get; }
        public Date DateRequestMade { get; }
        public Date DateRequestFulfilled { get; private set; }
        public Date DateRequestResolved { get; private set; }
        public Date Deadline { get; }
        public RequestFulfillmentKind FulfillmentKind { get; }
        public RequestStatus Status { get; private set; }
        public ForceCommitmentPackage Commitment { get; }
        public long ProgressBattleValueTime { get; private set; }
        public int OfferedRequisition { get; }
        public PledgeScheduleKind OfferedScheduleKind { get; }
        public int OfferedCadenceWeeks { get; }
        public int OfferedDeliveryDelayWeeks { get; }
        public RequestSeverity Severity { get; }
        public RequestHazard Hazard { get; }
        public bool HasPlayerResponded { get; private set; }

        public PresenceRequest(
            int id,
            Planet planet,
            Character requester,
            Faction threatFaction,
            Date dateRequestMade,
            Date fulfilledDate = null)
            : this(
                id,
                planet,
                requester,
                threatFaction,
                dateRequestMade,
                AddWeeks(dateRequestMade, 8),
                new ForceCommitmentPackage(
                    "astartes-presence", "Astartes presence", "squad", 1, 4, 8, 100),
                100,
                PledgeScheduleKind.OneOff,
                0,
                4,
                threatFaction == null ? RequestSeverity.Concerned : RequestSeverity.Serious,
                threatFaction == null ? RequestHazard.Routine : RequestHazard.Dangerous,
                0,
                false,
                fulfilledDate == null ? RequestStatus.Open : RequestStatus.Fulfilled,
                fulfilledDate)
        {
        }

        public PresenceRequest(
            int id,
            Planet planet,
            Character requester,
            Faction threatFaction,
            Date dateRequestMade,
            Date deadline,
            ForceCommitmentPackage commitment,
            int offeredRequisition,
            PledgeScheduleKind offeredScheduleKind = PledgeScheduleKind.OneOff,
            int offeredCadenceWeeks = 0,
            int offeredDeliveryDelayWeeks = 4,
            RequestSeverity severity = RequestSeverity.Concerned,
            RequestHazard hazard = RequestHazard.Routine,
            long progressBattleValueTime = 0,
            bool hasPlayerResponded = false,
            RequestStatus status = RequestStatus.Open,
            Date resolvedDate = null)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id;
            TargetPlanet = planet ?? throw new ArgumentNullException(nameof(planet));
            Requester = requester ?? throw new ArgumentNullException(nameof(requester));
            ThreatFaction = threatFaction;
            DateRequestMade = Copy(dateRequestMade ?? throw new ArgumentNullException(nameof(dateRequestMade)));
            Deadline = Copy(deadline ?? throw new ArgumentNullException(nameof(deadline)));
            Commitment = commitment ?? throw new ArgumentNullException(nameof(commitment));
            if (offeredRequisition <= 0) throw new ArgumentOutOfRangeException(nameof(offeredRequisition));
            if (progressBattleValueTime < 0) throw new ArgumentOutOfRangeException(nameof(progressBattleValueTime));
            OfferedRequisition = offeredRequisition;
            if (offeredDeliveryDelayWeeks <= 0)
                throw new ArgumentOutOfRangeException(nameof(offeredDeliveryDelayWeeks));
            if (offeredScheduleKind == PledgeScheduleKind.Standing && offeredCadenceWeeks <= 0)
                throw new ArgumentOutOfRangeException(nameof(offeredCadenceWeeks));
            if (offeredScheduleKind == PledgeScheduleKind.OneOff && offeredCadenceWeeks != 0)
                throw new ArgumentOutOfRangeException(nameof(offeredCadenceWeeks));
            OfferedScheduleKind = offeredScheduleKind;
            OfferedCadenceWeeks = offeredCadenceWeeks;
            OfferedDeliveryDelayWeeks = offeredDeliveryDelayWeeks;
            Severity = severity;
            Hazard = hazard;
            ProgressBattleValueTime = progressBattleValueTime;
            HasPlayerResponded = hasPlayerResponded;
            FulfillmentKind = threatFaction == null
                ? RequestFulfillmentKind.ForceCommitment
                : RequestFulfillmentKind.ThreatSuppressed;
            Status = status;
            DateRequestFulfilled = status == RequestStatus.Fulfilled && resolvedDate != null
                ? Copy(resolvedDate)
                : null;
            DateRequestResolved = status is RequestStatus.Fulfilled or RequestStatus.Failed
                && resolvedDate != null
                    ? Copy(resolvedDate)
                    : null;
        }

        public bool IsRequestStarted() =>
            Status != RequestStatus.Open || IsPlayerPresent();

        public bool IsRequestCompleted() => Status == RequestStatus.Fulfilled;

        public void ProcessTurn(Date currentDate)
        {
            if (Status is RequestStatus.Fulfilled or RequestStatus.Failed)
            {
                return;
            }

            // Work on the deadline itself counts; anything later has already expired.
            if (currentDate.GetWeeksDifference(Deadline) > 0)
            {
                Fail(currentDate);
                return;
            }

            long weeklyStrength = CalculateQualifyingPresenceBattleValue(
                requireUnassigned: FulfillmentKind == RequestFulfillmentKind.ForceCommitment);
            if (weeklyStrength > 0)
            {
                HasPlayerResponded = true;
            }

            if (FulfillmentKind == RequestFulfillmentKind.ThreatSuppressed)
            {
                if (!TargetPlanet.PlanetFactionMap.TryGetValue(ThreatFaction.Id, out PlanetFaction threat)
                    || !threat.IsPublic)
                {
                    if (HasPlayerResponded)
                    {
                        Fulfill(currentDate);
                    }
                    else
                    {
                        // Another force resolved the danger before the chapter responded.
                        Fail(currentDate);
                    }
                    return;
                }
            }
            else
            {
                if (weeklyStrength > 0)
                {
                    Status = RequestStatus.InProgress;
                    long weeklyCap = Commitment.ReferenceBattleValuePerPackage
                        * Commitment.MaximumEffectivePackageCount;
                    ProgressBattleValueTime += Math.Min(weeklyStrength, weeklyCap);
                    long required = Commitment.ReferenceBattleValuePerPackage
                        * Commitment.PackageCount
                        * Commitment.ServiceWeeks;
                    if (ProgressBattleValueTime >= required)
                    {
                        Fulfill(currentDate);
                        return;
                    }
                }
            }

        }

        private void Fulfill(Date currentDate)
        {
            Status = RequestStatus.Fulfilled;
            DateRequestFulfilled = Copy(currentDate);
            DateRequestResolved = Copy(currentDate);
        }

        public void Fail(Date currentDate)
        {
            if (Status is RequestStatus.Fulfilled or RequestStatus.Failed) return;
            Status = RequestStatus.Failed;
            DateRequestResolved = Copy(currentDate ?? throw new ArgumentNullException(nameof(currentDate)));
        }

        private long CalculateQualifyingPresenceBattleValue(bool requireUnassigned)
        {
            Faction playerFaction = GameDataSingleton.Instance.GameRulesData.PlayerFaction;
            return TargetPlanet.Regions
                .Where(region => region.RegionFactionMap.ContainsKey(playerFaction.Id))
                .SelectMany(region => region.RegionFactionMap[playerFaction.Id].LandedSquads)
                .Distinct()
                .Where(squad => !requireUnassigned || squad.CurrentOrders == null)
                .Where(SquadMatchesQualifications)
                .Sum(squad => squad.Members.Sum(member => (long)member.Template.BattleValue));
        }

        private bool SquadMatchesQualifications(Models.Squads.Squad squad)
        {
            foreach (string tag in Commitment.QualificationTags)
            {
                if (tag.Equals("Scout", StringComparison.OrdinalIgnoreCase)
                    && !squad.SquadTemplate.SquadType.HasFlag(Models.Squads.SquadTypes.Scout))
                    return false;
                if (tag.Equals("Covert", StringComparison.OrdinalIgnoreCase)
                    && !squad.SquadTemplate.SquadType.HasFlag(Models.Squads.SquadTypes.Scout)
                    && !(squad.CurrentOrders?.IsQuiet ?? false))
                    return false;
                if (tag.Equals("Techmarine", StringComparison.OrdinalIgnoreCase)
                    && !squad.Members.Any(member =>
                        ReferenceEquals(member.Template,
                            GameDataSingleton.Instance.GameRulesData.ChapterTemplates.Techmarine)))
                    return false;
            }
            return true;
        }

        private bool IsPlayerPresent() =>
            CalculateQualifyingPresenceBattleValue(requireUnassigned: false) > 0;

        private static Date Copy(Date date) => new(date.Millenium, date.Year, date.Week);

        private static Date AddWeeks(Date date, int weeks)
        {
            Date result = Copy(date);
            for (int i = 0; i < weeks; i++) result.IncrementWeek();
            return result;
        }
    }
}
