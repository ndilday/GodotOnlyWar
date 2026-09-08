using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Soldiers;
namespace OnlyWar.Medical.Abstractions
{
    public enum DutyReadinessReasonCode
    {
        Ready = 0,
        CombatIncapacitation,
        UntreatedSeverance,
        InsufficientFunctioningArms,
        ProcedureReservation,
        ChapterInjuryThreshold
    }

    /// <summary>
    /// Typed result for the individual Chapter duty decision. The reason is deliberately not a
    /// string-only flag: UI, orders, and mission assembly must be able to agree on the same cause.
    /// </summary>
    public sealed record DutyReadinessEvaluation(
        bool IsDutyReady,
        DutyReadinessReasonCode ReasonCode,
        string Reason,
        WoundLevel? WorstWoundLevel = null)
    {
        public bool IsReady => IsDutyReady;
        public bool IsAllowed => IsDutyReady;
        public static DutyReadinessEvaluation Ready { get; } =
            new(true, DutyReadinessReasonCode.Ready, null, null);
    }

    public enum SquadUnavailableReason
    {
        InjuryOrIncapacitation = 0,
        Injury = InjuryOrIncapacitation,
        IndividualPosting = 1,
        ProcedureReservation = 2,
        DoctrineWithholding = 3,
        Other = 4
    }

    public enum SquadLeaderStatus
    {
        NotRequired,
        Ready,
        Unavailable,
        Vacant
    }

    public enum SquadReadinessState
    {
        Ready,
        Blocked,
        NotApplicable
    }

    public enum SquadCommitmentKind
    {
        Free,
        Order,
        InTransit,
        Training,
        Administrative
    }

    public enum SquadReadinessBlocker
    {
        None,
        Administrative,
        EmptyFormation,
        NoEffectiveMembers,
        Leaderless,
        BelowMinimumDutyReadyStrength,
        RequiredLeaderUnavailable,
        ReservedForProcedure,
        AssignedElsewhere,
        Embarked,
        NotLanded,
        NotOrbiting,
        InWarp,
        OutsideArea,
        MissionUnavailable,
        DestinationCapacity,
        InWarpContact,
        CommittedToTraining,
        HistoricalFormation,
        Other
    }

    public sealed class SquadStrengthSnapshot
    {
        public int Full { get; }
        public int Rostered { get; }
        public int Present { get; }
        public int Effective { get; }
        public int CombatEffective => Effective;
        public int DutyReady { get; }
        public int DutyReadyCount => DutyReady;
        public int Unavailable { get; }
        public int Vacancies { get; }
        public IReadOnlyDictionary<SquadUnavailableReason, int> UnavailableReasonCounts { get; }
        public IReadOnlyDictionary<SquadUnavailableReason, int> Breakdown => UnavailableReasonCounts;

        public int InjuryOrIncapacitationCount => Count(SquadUnavailableReason.InjuryOrIncapacitation);
        public int InjuryCount => InjuryOrIncapacitationCount;
        public int IndividualPostingCount => Count(SquadUnavailableReason.IndividualPosting);
        public int ProcedureReservationCount => Count(SquadUnavailableReason.ProcedureReservation);
        public int ProcedureReservedCount => ProcedureReservationCount;
        public int DoctrineWithholdingCount => Count(SquadUnavailableReason.DoctrineWithholding);
        public int WithheldByDoctrineCount => DoctrineWithholdingCount;
        public int OtherUnavailableCount => Count(SquadUnavailableReason.Other);
        public int UnavailableCount => Unavailable;

        public SquadUnavailableReason? PrimaryUnavailableReason
        {
            get
            {
                // A posted member is not physically with the formation; a reserved member is
                // explicitly unavailable for a procedure; only then do we call the remainder
                // injured/incapacitated. This stable order prevents a dual-state soldier from
                // changing the token shown by a row as systems update in different orders.
                foreach (SquadUnavailableReason reason in new[]
                {
                    SquadUnavailableReason.IndividualPosting,
                    SquadUnavailableReason.ProcedureReservation,
                    SquadUnavailableReason.InjuryOrIncapacitation,
                    SquadUnavailableReason.DoctrineWithholding,
                    SquadUnavailableReason.Other
                })
                {
                    if (Count(reason) > 0) return reason;
                }
                return null;
            }
        }

        public SquadStrengthSnapshot(
            int full,
            int rostered,
            int present,
            int effective,
            int dutyReady,
            int unavailable,
            int vacancies,
            IReadOnlyDictionary<SquadUnavailableReason, int> unavailableReasonCounts)
        {
            Full = Math.Max(0, full);
            Rostered = Math.Max(0, rostered);
            Present = Math.Max(0, present);
            Effective = Math.Max(0, effective);
            DutyReady = Math.Max(0, dutyReady);
            Unavailable = Math.Max(0, unavailable);
            Vacancies = Math.Max(0, vacancies);
            UnavailableReasonCounts = unavailableReasonCounts
                ?? new Dictionary<SquadUnavailableReason, int>();
        }

        public int Count(SquadUnavailableReason reason) =>
            UnavailableReasonCounts.TryGetValue(reason, out int value) ? value : 0;
    }

    public sealed class SquadReadinessSnapshot
    {
        public SquadStrengthSnapshot Strength { get; }
        public SquadLeaderStatus LeaderStatus { get; }
        public SquadReadinessState StructuralState { get; }
        public SquadReadinessState State => StructuralState;
        public SquadCommitmentKind Commitment { get; }
        public SquadCommitmentKind CommitmentKind => Commitment;
        public bool CanBeginDeployment { get; }
        public SquadReadinessBlocker PrimaryBlocker { get; }
        public IReadOnlyList<SquadReadinessBlocker> StructuralBlockers { get; }
        public IReadOnlyList<SquadReadinessBlocker> ContextBlockers { get; }
        public IReadOnlyList<SquadReadinessBlocker> AllBlockers { get; }

        public SquadReadinessSnapshot(
            SquadStrengthSnapshot strength,
            SquadLeaderStatus leaderStatus,
            SquadReadinessState structuralState,
            SquadCommitmentKind commitment,
            bool canBeginDeployment,
            SquadReadinessBlocker primaryBlocker,
            IReadOnlyList<SquadReadinessBlocker> structuralBlockers,
            IReadOnlyList<SquadReadinessBlocker> contextBlockers)
        {
            Strength = strength ?? throw new ArgumentNullException(nameof(strength));
            LeaderStatus = leaderStatus;
            StructuralState = structuralState;
            Commitment = commitment;
            CanBeginDeployment = canBeginDeployment;
            PrimaryBlocker = primaryBlocker;
            StructuralBlockers = structuralBlockers ?? Array.Empty<SquadReadinessBlocker>();
            ContextBlockers = contextBlockers ?? Array.Empty<SquadReadinessBlocker>();
            AllBlockers = StructuralBlockers
                .Concat(ContextBlockers)
                .Where(blocker => blocker != SquadReadinessBlocker.None)
                .Distinct()
                .ToList();
        }
    }

}
