using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Supply;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Domain
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
        bool IsRequestStarted(GameRulesData rules);
        bool IsRequestCompleted();
        void ProcessTurn(Date currentDate, GameRulesData rules);
        void Fail(Date currentDate);
    }

}
