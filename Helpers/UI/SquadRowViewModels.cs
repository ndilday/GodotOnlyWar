using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.UI
{
    /// <summary>
    /// The one vocabulary used by every live squad presentation. The aliases keep the names
    /// readable at call sites while preserving the more precise injury/incapacitation wording in
    /// tooltips and reports.
    /// </summary>
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

    public enum SquadRowContextKind
    {
        Generic,
        PlanetaryOperations,
        Chapter,
        RecruiterTraining,
        Fleet,
        Apothecarium,
        Muster,
        BattleReview
    }

    public enum SquadRowAction
    {
        None,
        BeginOrder,
        Land,
        Embark,
        Transfer,
        Inspect
    }

    /// <summary>
    /// Screen-owned context layered on top of the common squad facts. It may add an action
    /// restriction, but it cannot redefine strength, leadership, or structural readiness.
    /// </summary>
    public sealed class SquadRowContext
    {
        public SquadRowContextKind Kind { get; }
        public SquadRowAction Action { get; }
        public Region Origin { get; }
        public Region Target { get; }
        public bool IsSelected { get; }
        public bool IsSelectable { get; }
        public bool IsEnabled { get; }
        public string ContextBadge { get; }
        public IReadOnlyList<SquadReadinessBlocker> Restrictions { get; }

        public SquadRowContext(
            SquadRowContextKind kind = SquadRowContextKind.Generic,
            SquadRowAction action = SquadRowAction.None,
            Region origin = null,
            Region target = null,
            bool isSelected = false,
            bool isSelectable = true,
            bool isEnabled = true,
            string contextBadge = null,
            IReadOnlyList<SquadReadinessBlocker> restrictions = null)
        {
            Kind = kind;
            Action = action;
            Origin = origin;
            Target = target;
            IsSelected = isSelected;
            IsSelectable = isSelectable;
            IsEnabled = isEnabled;
            ContextBadge = contextBadge;
            Restrictions = restrictions ?? Array.Empty<SquadReadinessBlocker>();
        }

        public static SquadRowContext ForNewOrder(
            Region target = null,
            bool isSelected = false,
            bool isSelectable = true) =>
            new(SquadRowContextKind.PlanetaryOperations, SquadRowAction.BeginOrder,
                target: target, isSelected: isSelected, isSelectable: isSelectable);

        public static SquadRowContext ForLanding(
            Region target = null,
            bool isSelected = false,
            bool isSelectable = true) =>
            new(SquadRowContextKind.PlanetaryOperations, SquadRowAction.Land,
                target: target, isSelected: isSelected, isSelectable: isSelectable);

        public static SquadRowContext ForEmbark(
            Region origin = null,
            bool isSelected = false,
            bool isSelectable = true) =>
            new(SquadRowContextKind.PlanetaryOperations, SquadRowAction.Embark,
                origin: origin, isSelected: isSelected, isSelectable: isSelectable);
    }

    /// <summary>
    /// Canonical strength accounting for a live player squad.
    ///
    /// Full is template establishment, never below a legacy overstrength roster. Rostered is
    /// organizational membership, Present excludes an individual posting, Effective is the
    /// present combat-effective subset, and DutyReady is the subset permitted by the Chapter
    /// operational doctrine. Every unavailable member is assigned exactly one reason using the
    /// precedence in <see cref="ClassifyUnavailable"/>.
    /// </summary>
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

    public static class SquadStrengthSnapshotBuilder
    {
        public static SquadStrengthSnapshot Build(
            Squad squad,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null)
        {
            doctrine = ResolveDoctrine(squad, doctrine);
            IReadOnlyList<ISoldier> members = squad?.Members?.ToList()
                ?? new List<ISoldier>();
            int rostered = members.Count;
            int establishment = squad?.SquadTemplate?.Elements?.Sum(element => element.MaximumNumber)
                ?? 0;
            int full = Math.Max(rostered, establishment);
            int present = 0;
            int effective = 0;
            int dutyReady = 0;
            Dictionary<SquadUnavailableReason, int> reasons = Enum
                .GetValues<SquadUnavailableReason>()
                .Distinct()
                .ToDictionary(reason => reason, _ => 0);

            foreach (ISoldier member in members)
            {
                bool posted = member is PlayerSoldier player
                    && player.IndividualPosting != null;
                bool reserved = IsProcedureReserved(member, program);
                bool combatEffective = IsCombatEffectiveMember(member, program);
                DutyReadinessEvaluation duty = DutyReadinessService.Evaluate(
                    member, doctrine, program);
                if (!posted) present++;
                if (!posted && combatEffective)
                {
                    effective++;
                }

                if (!posted && duty.IsDutyReady) dutyReady++;
                if (!posted && duty.IsDutyReady) continue;

                SquadUnavailableReason reason = ClassifyUnavailable(
                    posted, reserved, combatEffective, duty.ReasonCode);
                reasons[reason]++;
            }

            int unavailable = rostered - dutyReady;
            return new SquadStrengthSnapshot(
                full,
                rostered,
                present,
                effective,
                dutyReady,
                unavailable,
                Math.Max(0, full - rostered),
                reasons);
        }

        public static SquadStrengthSnapshot Create(
            Squad squad,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null) => Build(squad, program, doctrine);

        internal static SquadUnavailableReason ClassifyUnavailable(
            bool posted,
            bool reserved,
            bool combatEffective,
            DutyReadinessReasonCode dutyReason = DutyReadinessReasonCode.CombatIncapacitation)
        {
            if (posted) return SquadUnavailableReason.IndividualPosting;
            if (reserved) return SquadUnavailableReason.ProcedureReservation;
            if (dutyReason == DutyReadinessReasonCode.ChapterInjuryThreshold)
            {
                return SquadUnavailableReason.DoctrineWithholding;
            }
            if (!combatEffective
                || dutyReason == DutyReadinessReasonCode.UntreatedSeverance
                || dutyReason == DutyReadinessReasonCode.InsufficientFunctioningArms
                || dutyReason == DutyReadinessReasonCode.CombatIncapacitation)
            {
                return SquadUnavailableReason.InjuryOrIncapacitation;
            }
            return SquadUnavailableReason.Other;
        }

        public static bool IsCombatEffectiveMember(
            ISoldier member,
            RecruitmentProgram program = null)
        {
            if (member?.IsCombatEffective != true)
            {
                return false;
            }

            return member is not PlayerSoldier player
                || !player.IsUndergoingMedicalProcedure
                    && !RecruitmentPromotionService.IsReservedForProcedure(program, player.Id);
        }

        private static bool IsProcedureReserved(
            ISoldier member,
            RecruitmentProgram program)
        {
            if (member is not PlayerSoldier player) return false;
            return player.IsUndergoingMedicalProcedure
                || RecruitmentPromotionService.IsReservedForProcedure(program, player.Id);
        }

        public static ChapterOperationalDoctrine ResolveDoctrine(
            Squad squad,
            ChapterOperationalDoctrine doctrine = null)
        {
            if (doctrine != null) return doctrine;
            if (squad?.Faction?.IsPlayerFaction != true) return null;
            PlayerForce playerForce = GameDataSingleton.Instance?.Sector?.PlayerForce;
            // Detached simulations and inspection models can contain a player-shaped faction
            // without belonging to the live campaign singleton. Do not leak the live Chapter's
            // doctrine into those models; production squads share the same faction instance.
            if (playerForce?.Faction == null
                || !ReferenceEquals(squad.Faction, playerForce.Faction))
            {
                return null;
            }
            return playerForce.Army?.ChapterOperationalDoctrine;
        }
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
        public string PrimaryBlockerText => SquadReadinessPresentation.BlockerLabel(PrimaryBlocker);

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

    public static class SquadReadinessService
    {
        public static SquadReadinessSnapshot Evaluate(
            Squad squad,
            SquadRowContext context = null,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null)
        {
            doctrine = SquadStrengthSnapshotBuilder.ResolveDoctrine(squad, doctrine);
            SquadStrengthSnapshot strength = SquadStrengthSnapshotBuilder.Build(
                squad, program, doctrine);
            bool requiresLeader = squad?.SquadTemplate?.Elements?.Any(
                element => element.SoldierTemplate?.IsSquadLeader == true) == true;
            ISoldier leader = squad?.SquadLeader;
            SquadLeaderStatus leaderStatus = !requiresLeader
                ? SquadLeaderStatus.NotRequired
                : leader == null
                    ? SquadLeaderStatus.Vacant
                    : IsLeaderAvailable(leader, program, doctrine)
                        ? SquadLeaderStatus.Ready
                        : SquadLeaderStatus.Unavailable;

            SquadCommitmentKind commitment = GetCommitment(squad);
            List<SquadReadinessBlocker> structural = [];
            SquadReadinessState state;
            if (squad == null || squad.PermitsIndividualDeployment)
            {
                structural.Add(SquadReadinessBlocker.Administrative);
                state = SquadReadinessState.NotApplicable;
            }
            else if (strength.Rostered == 0)
            {
                structural.Add(SquadReadinessBlocker.EmptyFormation);
                state = SquadReadinessState.Blocked;
            }
            else
            {
                if (leaderStatus == SquadLeaderStatus.Vacant)
                {
                    structural.Add(SquadReadinessBlocker.Leaderless);
                }
                else if (doctrine != null
                    && doctrine.RequireDutyReadySquadLeader
                    && leaderStatus == SquadLeaderStatus.Unavailable)
                {
                    structural.Add(SquadReadinessBlocker.RequiredLeaderUnavailable);
                }
                if (strength.Effective == 0)
                {
                    structural.Add(SquadReadinessBlocker.NoEffectiveMembers);
                }
                if (doctrine != null
                    && strength.DutyReady < doctrine.MinimumDutyReadySquadStrength)
                {
                    structural.Add(SquadReadinessBlocker.BelowMinimumDutyReadyStrength);
                }
                if (strength.ProcedureReservationCount > 0
                    && strength.Effective == 0)
                {
                    structural.Add(SquadReadinessBlocker.ReservedForProcedure);
                }
                state = structural.Count == 0
                    ? SquadReadinessState.Ready
                    : SquadReadinessState.Blocked;
            }

            List<SquadReadinessBlocker> contextBlockers =
                EvaluateContext(squad, context, commitment);
            List<SquadReadinessBlocker> all = structural
                .Concat(contextBlockers)
                .Where(blocker => blocker != SquadReadinessBlocker.None)
                .ToList();
            SquadReadinessBlocker primary = FirstBlocker(all);
            bool canBegin = state == SquadReadinessState.Ready
                && contextBlockers.Count == 0
                && commitment == SquadCommitmentKind.Free;
            return new SquadReadinessSnapshot(
                strength,
                leaderStatus,
                state,
                commitment,
                canBegin,
                primary,
                structural,
                contextBlockers);
        }

        public static SquadReadinessSnapshot Build(
            Squad squad,
            SquadRowContext context = null,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null) =>
            Evaluate(squad, context, program, doctrine);

        public static bool CanBeginNewDeployment(
            Squad squad,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null) =>
            Evaluate(squad, new SquadRowContext(
                SquadRowContextKind.Generic,
                SquadRowAction.BeginOrder), program, doctrine).CanBeginDeployment;

        public static string GetBlockerText(SquadReadinessBlocker blocker) =>
            SquadReadinessPresentation.BlockerLabel(blocker);

        private static bool IsLeaderAvailable(
            ISoldier leader,
            RecruitmentProgram program,
            ChapterOperationalDoctrine doctrine) =>
            DutyReadinessService.Evaluate(leader, doctrine, program).IsDutyReady
            && (leader is not PlayerSoldier player || player.IndividualPosting == null);

        private static SquadCommitmentKind GetCommitment(Squad squad)
        {
            if (squad?.PermitsIndividualDeployment == true)
            {
                return SquadCommitmentKind.Administrative;
            }
            if (squad?.CurrentOrders?.Mission?.MissionType == MissionType.Training)
            {
                return SquadCommitmentKind.Training;
            }
            if (squad?.CurrentOrders != null)
            {
                return SquadCommitmentKind.Order;
            }
            if (squad?.BoardedLocation?.Fleet?.TravelPhase is not null
                && squad.BoardedLocation.Fleet.TravelPhase != FleetTravelPhase.InOrbit)
            {
                return SquadCommitmentKind.InTransit;
            }
            return SquadCommitmentKind.Free;
        }

        private static List<SquadReadinessBlocker> EvaluateContext(
            Squad squad,
            SquadRowContext context,
            SquadCommitmentKind commitment)
        {
            if (context == null || squad == null) return [];
            List<SquadReadinessBlocker> blockers = context.Restrictions
                .Where(blocker => blocker != SquadReadinessBlocker.None)
                .ToList();
            switch (context.Action)
            {
                case SquadRowAction.BeginOrder:
                    if (squad.CurrentOrders != null)
                    {
                        blockers.Add(SquadReadinessBlocker.AssignedElsewhere);
                    }
                    if (commitment == SquadCommitmentKind.InTransit)
                    {
                        blockers.Add(SquadReadinessBlocker.Embarked);
                    }
                    break;
                case SquadRowAction.Land:
                    if (squad.BoardedLocation == null)
                    {
                        blockers.Add(SquadReadinessBlocker.NotOrbiting);
                    }
                    if (squad.BoardedLocation?.Fleet?.TravelPhase == FleetTravelPhase.InWarp)
                    {
                        blockers.Add(SquadReadinessBlocker.InWarp);
                    }
                    break;
                case SquadRowAction.Embark:
                    if (squad.CurrentRegion == null)
                    {
                        blockers.Add(SquadReadinessBlocker.NotLanded);
                    }
                    if (squad.BoardedLocation != null)
                    {
                        blockers.Add(SquadReadinessBlocker.Embarked);
                    }
                    break;
                case SquadRowAction.Transfer:
                    if (squad.BoardedLocation?.Fleet?.TravelPhase == FleetTravelPhase.InWarp)
                    {
                        blockers.Add(SquadReadinessBlocker.InWarp);
                    }
                    break;
            }
            return blockers.Distinct().ToList();
        }

        private static SquadReadinessBlocker FirstBlocker(
            IEnumerable<SquadReadinessBlocker> blockers)
        {
            List<SquadReadinessBlocker> values = blockers.ToList();
            foreach (SquadReadinessBlocker preferred in new[]
            {
                SquadReadinessBlocker.Administrative,
                SquadReadinessBlocker.Leaderless,
                SquadReadinessBlocker.RequiredLeaderUnavailable,
                SquadReadinessBlocker.EmptyFormation,
                SquadReadinessBlocker.NoEffectiveMembers,
                SquadReadinessBlocker.BelowMinimumDutyReadyStrength,
                SquadReadinessBlocker.ReservedForProcedure,
                SquadReadinessBlocker.AssignedElsewhere,
                SquadReadinessBlocker.InWarp,
                SquadReadinessBlocker.Embarked,
                SquadReadinessBlocker.NotLanded,
                SquadReadinessBlocker.NotOrbiting,
                SquadReadinessBlocker.CommittedToTraining,
                SquadReadinessBlocker.OutsideArea,
                SquadReadinessBlocker.MissionUnavailable,
                SquadReadinessBlocker.DestinationCapacity,
                SquadReadinessBlocker.Other
            })
            {
                if (values.Contains(preferred)) return preferred;
            }
            return SquadReadinessBlocker.None;
        }
    }

    public static class SquadReadinessPresentation
    {
        public static string CommitmentLabel(SquadCommitmentKind commitment) => commitment switch
        {
            SquadCommitmentKind.Order => "ORDER",
            SquadCommitmentKind.InTransit => "IN TRANSIT",
            SquadCommitmentKind.Training => "TRAINING",
            SquadCommitmentKind.Administrative => "ADMIN",
            _ => "FREE"
        };

        public static string LeaderLabel(SquadLeaderStatus status) => status switch
        {
            SquadLeaderStatus.Vacant => "NO LEADER",
            SquadLeaderStatus.Unavailable => "LEADER OUT",
            SquadLeaderStatus.NotRequired => string.Empty,
            _ => "LEADER READY"
        };

        public static string BlockerLabel(SquadReadinessBlocker blocker) => blocker switch
        {
            SquadReadinessBlocker.Administrative => "ADMINISTRATIVE",
            SquadReadinessBlocker.EmptyFormation => "EMPTY FORMATION",
            SquadReadinessBlocker.NoEffectiveMembers => "NO EFFECTIVE MEMBERS",
            SquadReadinessBlocker.Leaderless => "NO LEADER",
            SquadReadinessBlocker.BelowMinimumDutyReadyStrength => "BELOW MINIMUM DUTY STRENGTH",
            SquadReadinessBlocker.RequiredLeaderUnavailable => "REQUIRED LEADER UNAVAILABLE",
            SquadReadinessBlocker.ReservedForProcedure => "PROCEDURE RESERVED",
            SquadReadinessBlocker.AssignedElsewhere => "ASSIGNED ELSEWHERE",
            SquadReadinessBlocker.Embarked => "ABOARD SHIP",
            SquadReadinessBlocker.NotLanded => "NOT LANDED",
            SquadReadinessBlocker.NotOrbiting => "NOT IN ORBIT",
            SquadReadinessBlocker.InWarp => "IN WARP",
            SquadReadinessBlocker.CommittedToTraining => "TRAINING",
            SquadReadinessBlocker.OutsideArea => "OUTSIDE AREA",
            SquadReadinessBlocker.MissionUnavailable => "MISSION UNAVAILABLE",
            SquadReadinessBlocker.DestinationCapacity => "NO CAPACITY",
            SquadReadinessBlocker.InWarpContact => "OUT OF CONTACT",
            SquadReadinessBlocker.HistoricalFormation => "HISTORICAL",
            SquadReadinessBlocker.Other => "UNAVAILABLE",
            _ => string.Empty
        };

        public static string UnavailableLabel(SquadUnavailableReason reason) => reason switch
        {
            SquadUnavailableReason.IndividualPosting => "POSTED",
            SquadUnavailableReason.ProcedureReservation => "PROCEDURE",
            SquadUnavailableReason.InjuryOrIncapacitation => "OUT",
            SquadUnavailableReason.DoctrineWithholding => "WITHHELD",
            _ => "UNAVAILABLE"
        };
    }

    /// <summary>
    /// Shared presentation facts for one live squad. A screen supplies context and owns actions;
    /// this model owns all facts that must remain legible on every row.
    /// </summary>
    public class SquadRowViewModel
    {
        public string Key { get; }
        public int? LiveSquadId { get; }
        public string Name { get; }
        public string Type { get; }
        public string IconKey { get; }
        public string ParentFormation { get; }
        public string Location { get; }
        public SquadStrengthSnapshot Strength { get; }
        public SquadLeaderStatus LeaderStatus { get; }
        public SquadCommitmentKind Commitment { get; }
        public string CommitmentLabel { get; }
        public SquadReadinessSnapshot Readiness { get; }
        public bool Selected { get; }
        public bool Selectable { get; }
        public bool Enabled { get; }
        public string DisabledReason { get; }
        public string ContextBadge { get; }
        public string Tooltip { get; }
        public int PresentationPriority { get; }

        public string StrengthLabel => $"{Strength.DutyReady}/{Strength.Full}";
        public string LeaderLabel => SquadReadinessPresentation.LeaderLabel(LeaderStatus);
        public string PrimaryStateLabel => Readiness.PrimaryBlocker != SquadReadinessBlocker.None
            ? SquadReadinessPresentation.BlockerLabel(Readiness.PrimaryBlocker)
            : LeaderLabel;

        public SquadRowViewModel(
            string key,
            string name,
            string type,
            string iconKey,
            string parentFormation,
            string location,
            SquadStrengthSnapshot strength,
            SquadLeaderStatus leaderStatus,
            SquadCommitmentKind commitment,
            SquadReadinessSnapshot readiness,
            int? liveSquadId = null,
            bool selected = false,
            bool selectable = true,
            bool enabled = true,
            string disabledReason = null,
            string contextBadge = null,
            string tooltip = null,
            int presentationPriority = 0)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Name = name ?? string.Empty;
            Type = type ?? string.Empty;
            IconKey = iconKey;
            ParentFormation = parentFormation ?? string.Empty;
            Location = location ?? string.Empty;
            Strength = strength ?? throw new ArgumentNullException(nameof(strength));
            LeaderStatus = leaderStatus;
            Commitment = commitment;
            CommitmentLabel = SquadReadinessPresentation.CommitmentLabel(commitment);
            Readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
            LiveSquadId = liveSquadId;
            Selected = selected;
            Selectable = selectable;
            Enabled = enabled;
            DisabledReason = disabledReason ??
                (Readiness.PrimaryBlocker == SquadReadinessBlocker.None
                    ? null : Readiness.PrimaryBlockerText);
            ContextBadge = contextBadge;
            Tooltip = tooltip ?? string.Empty;
            PresentationPriority = presentationPriority;
        }
    }

    public sealed class ProjectedSquadRowViewModel : SquadRowViewModel
    {
        public int OutgoingDelta { get; }
        public int IncomingDelta { get; }
        public int FutureStrength { get; }
        public string ProvisionalKey { get; }

        public ProjectedSquadRowViewModel(
            SquadRowViewModel source,
            int outgoingDelta,
            int incomingDelta,
            int futureStrength,
            string provisionalKey)
            : base(
                source.Key,
                source.Name,
                source.Type,
                source.IconKey,
                source.ParentFormation,
                source.Location,
                source.Strength,
                source.LeaderStatus,
                source.Commitment,
                source.Readiness,
                source.LiveSquadId,
                source.Selected,
                source.Selectable,
                source.Enabled,
                source.DisabledReason,
                source.ContextBadge,
                source.Tooltip,
                source.PresentationPriority)
        {
            OutgoingDelta = outgoingDelta;
            IncomingDelta = incomingDelta;
            FutureStrength = futureStrength;
            ProvisionalKey = provisionalKey ?? source.Key;
        }
    }

    public sealed class BattleSquadRowViewModel : SquadRowViewModel
    {
        public int StartingStrength { get; }
        public int CurrentStrength { get; }
        public string MoraleLabel { get; }
        public string FatigueLabel { get; }

        public BattleSquadRowViewModel(
            SquadRowViewModel source,
            int startingStrength,
            int currentStrength,
            string moraleLabel = null,
            string fatigueLabel = null)
            : base(
                source.Key,
                source.Name,
                source.Type,
                source.IconKey,
                source.ParentFormation,
                source.Location,
                source.Strength,
                source.LeaderStatus,
                source.Commitment,
                source.Readiness,
                source.LiveSquadId,
                source.Selected,
                source.Selectable,
                enabled: false,
                disabledReason: SquadReadinessPresentation.BlockerLabel(
                    SquadReadinessBlocker.HistoricalFormation),
                source.ContextBadge,
                source.Tooltip,
                source.PresentationPriority)
        {
            StartingStrength = Math.Max(0, startingStrength);
            CurrentStrength = Math.Max(0, currentStrength);
            MoraleLabel = moraleLabel ?? string.Empty;
            FatigueLabel = fatigueLabel ?? string.Empty;
        }
    }

    public sealed class SquadRowViewModelBuilder
    {
        public SquadRowViewModel Build(
            Squad squad,
            SquadRowContext context = null,
            RecruitmentProgram program = null)
        {
            context ??= new SquadRowContext();
            SquadStrengthSnapshot strength = SquadStrengthSnapshotBuilder.Build(squad, program);
            SquadReadinessSnapshot readiness = SquadReadinessService.Evaluate(
                squad, context, program);
            string location = CampaignLocationLabel(squad);
            string type = squad?.SquadTemplate?.Name ?? "Formation";
            string unavailable = strength.PrimaryUnavailableReason.HasValue
                ? SquadReadinessPresentation.UnavailableLabel(
                    strength.PrimaryUnavailableReason.Value)
                : string.Empty;
            List<string> secondary = [];
            if (!string.IsNullOrWhiteSpace(unavailable)
                && strength.DutyReady < strength.Full
                && strength.PrimaryUnavailableReason.HasValue)
            {
                secondary.Add(unavailable);
            }
            if (readiness.LeaderStatus == SquadLeaderStatus.Vacant)
            {
                secondary.Add("NO LEADER");
            }
            else if (readiness.LeaderStatus == SquadLeaderStatus.Unavailable)
            {
                secondary.Add(LeaderAvailabilityLabel(squad));
            }
            if (context.Action == SquadRowAction.BeginOrder
                && readiness.CanBeginDeployment)
            {
                secondary.Add("READY");
            }

            string tooltip = BuildTooltip(squad, strength, readiness, location);
            bool actionRequiresReadiness = context.Action != SquadRowAction.None
                && context.Action != SquadRowAction.Inspect;
            bool enabled = context.IsEnabled
                && (!actionRequiresReadiness
                    || readiness.PrimaryBlocker == SquadReadinessBlocker.None);
            string disabledReason = enabled
                ? null
                : readiness.PrimaryBlocker != SquadReadinessBlocker.None
                    ? readiness.PrimaryBlockerText
                    : context.Restrictions.FirstOrDefault() is SquadReadinessBlocker restriction
                        ? SquadReadinessPresentation.BlockerLabel(restriction)
                        : context.ContextBadge;
            return new SquadRowViewModel(
                squad == null ? "squad:unknown" : $"squad:{squad.Id}",
                squad?.Name ?? "Unknown formation",
                type,
                IconAtlas.GetSquadIconKey(squad?.SquadTemplate),
                squad?.ParentUnit?.Name ?? string.Empty,
                location,
                strength,
                readiness.LeaderStatus,
                readiness.Commitment,
                readiness,
                squad?.Id,
                context.IsSelected,
                context.IsSelectable,
                enabled,
                disabledReason,
                context.ContextBadge,
                tooltip,
                readiness.CanBeginDeployment ? 0 : 1);
        }

        public ProjectedSquadRowViewModel BuildProjected(
            SquadRowViewModel source,
            int outgoingDelta,
            int incomingDelta,
            int futureStrength,
            string provisionalKey) =>
            new(source, outgoingDelta, incomingDelta, futureStrength, provisionalKey);

        public BattleSquadRowViewModel BuildBattleSnapshot(
            BattleSquadSnapshot snapshot,
            int startingStrength = -1,
            int currentStrength = -1,
            string moraleLabel = null,
            string fatigueLabel = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            int current = currentStrength < 0 ? snapshot.Soldiers?.Count ?? 0 : currentStrength;
            int starting = startingStrength < 0 ? current : startingStrength;
            SquadRowViewModel source = snapshot.Squad != null
                ? Build(snapshot.Squad, new SquadRowContext(
                    SquadRowContextKind.BattleReview,
                    SquadRowAction.Inspect,
                    isSelectable: true,
                    isEnabled: false), null)
                : BuildSnapshotFallback(snapshot, current);
            return new BattleSquadRowViewModel(
                source, starting, current, moraleLabel, fatigueLabel);
        }

        private static SquadRowViewModel BuildSnapshotFallback(
            BattleSquadSnapshot snapshot,
            int currentStrength)
        {
            SquadStrengthSnapshot strength = new(
                currentStrength,
                currentStrength,
                currentStrength,
                currentStrength,
                currentStrength,
                0,
                0,
                new Dictionary<SquadUnavailableReason, int>());
            SquadReadinessSnapshot readiness = new(
                strength,
                SquadLeaderStatus.NotRequired,
                SquadReadinessState.NotApplicable,
                SquadCommitmentKind.Administrative,
                false,
                SquadReadinessBlocker.HistoricalFormation,
                [SquadReadinessBlocker.HistoricalFormation],
                []);
            return new SquadRowViewModel(
                $"battle-squad:{snapshot.Id}",
                snapshot.Name,
                "Battle formation",
                "tactical",
                string.Empty,
                "Historical battle",
                strength,
                SquadLeaderStatus.NotRequired,
                SquadCommitmentKind.Administrative,
                readiness,
                snapshot.Id,
                false,
                true,
                false,
                "HISTORICAL",
                null,
                $"{snapshot.Name}\nHistorical formation; deployment is not applicable.");
        }

        private static string CampaignLocationLabel(Squad squad)
        {
            if (squad?.DutyStation != null) return squad.DutyStation.ToString();
            if (squad?.BoardedLocation != null)
            {
                return squad.BoardedLocation.Fleet?.TravelPhase == FleetTravelPhase.InWarp
                    ? "In Warp"
                    : squad.BoardedLocation.Name;
            }
            return squad?.CurrentRegion?.Name ?? "Unlocated";
        }

        private static string BuildTooltip(
            Squad squad,
            SquadStrengthSnapshot strength,
            SquadReadinessSnapshot readiness,
            string location)
        {
            List<string> lines =
            [
                squad?.Name ?? "Unknown formation",
                $"Strength: {strength.DutyReady}/{strength.Full} duty-ready",
                $"Combat-effective: {strength.Effective}/{strength.Full}",
                $"Rostered: {strength.Rostered} · Present: {strength.Present}",
                $"Vacancies: {strength.Vacancies}",
                 $"Leader: {readiness.LeaderStatus switch
                 {
                     SquadLeaderStatus.Unavailable => LeaderAvailabilityLabel(squad),
                     _ => SquadReadinessPresentation.LeaderLabel(readiness.LeaderStatus)
                 }}",
                $"Commitment: {SquadReadinessPresentation.CommitmentLabel(readiness.Commitment)}",
                $"Location: {location}"
            ];
            if (strength.Unavailable > 0)
            {
                lines.Add($"Unavailable: {strength.Unavailable}");
                foreach (SquadUnavailableReason reason in Enum
                    .GetValues<SquadUnavailableReason>()
                    .Distinct())
                {
                    int count = strength.Count(reason);
                    if (count > 0)
                    {
                        lines.Add($"  {SquadReadinessPresentation.UnavailableLabel(reason)}: {count}");
                    }
                }
            }
            if (readiness.AllBlockers.Count > 0)
            {
                lines.Add($"Deployment: {string.Join(", ", readiness.AllBlockers
                    .Select(SquadReadinessPresentation.BlockerLabel))}");
            }
            return string.Join("\n", lines);
        }

        private static string LeaderAvailabilityLabel(Squad squad)
        {
            DutyReadinessEvaluation evaluation = DutyReadinessService.Evaluate(
                squad?.SquadLeader,
                SquadStrengthSnapshotBuilder.ResolveDoctrine(squad));
            return evaluation.ReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold
                ? "LEADER WITHHELD"
                : "LEADER OUT";
        }
    }
}
