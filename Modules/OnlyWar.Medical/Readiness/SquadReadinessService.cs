using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Medical.Readiness
{
    public static class SquadStrengthSnapshotBuilder
    {
        public static SquadStrengthSnapshot Build(
            Squad squad,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null)
        {
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
                    && !ReadinessReservations.IsReserved(program, player.Id);
        }

        private static bool IsProcedureReserved(
            ISoldier member,
            RecruitmentProgram program)
        {
            if (member is not PlayerSoldier player) return false;
            return player.IsUndergoingMedicalProcedure
                || ReadinessReservations.IsReserved(program, player.Id);
        }

    }

    public static class SquadReadinessService
    {
        public static SquadReadinessSnapshot Evaluate(
            Squad squad,
            SquadDeploymentContext context = null,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null)
        {
            SquadStrengthSnapshot strength = SquadStrengthSnapshotBuilder.Build(
                squad, program, doctrine);
            // MembersOnly formations are personnel pools. Their members can be attached as
            // individual characters, so a leader slot in the pool's template is not a
            // manoeuvre-squad leadership requirement.
            bool requiresLeader = squad?.PermitsIndividualDeployment != true
                && squad?.SquadTemplate?.Elements?.Any(
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
            SquadDeploymentContext context = null,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null) =>
            Evaluate(squad, context, program, doctrine);

        public static bool CanBeginNewDeployment(
            Squad squad,
            RecruitmentProgram program = null,
            ChapterOperationalDoctrine doctrine = null) =>
            Evaluate(squad, new SquadDeploymentContext(SquadDeploymentAction.BeginOrder), program, doctrine).CanBeginDeployment;

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
            SquadDeploymentContext context,
            SquadCommitmentKind commitment)
        {
            if (context == null || squad == null) return [];
            List<SquadReadinessBlocker> blockers = context.Restrictions
                .Where(blocker => blocker != SquadReadinessBlocker.None)
                .ToList();
            switch (context.Action)
            {
                case SquadDeploymentAction.BeginOrder:
                    if (squad.CurrentOrders != null)
                    {
                        blockers.Add(SquadReadinessBlocker.AssignedElsewhere);
                    }
                    if (commitment == SquadCommitmentKind.InTransit)
                    {
                        blockers.Add(SquadReadinessBlocker.Embarked);
                    }
                    break;
                case SquadDeploymentAction.Land:
                    if (squad.BoardedLocation == null)
                    {
                        blockers.Add(SquadReadinessBlocker.NotOrbiting);
                    }
                    if (squad.BoardedLocation?.Fleet?.TravelPhase == FleetTravelPhase.InWarp)
                    {
                        blockers.Add(SquadReadinessBlocker.InWarp);
                    }
                    break;
                case SquadDeploymentAction.Embark:
                    if (squad.CurrentRegion == null)
                    {
                        blockers.Add(SquadReadinessBlocker.NotLanded);
                    }
                    if (squad.BoardedLocation != null)
                    {
                        blockers.Add(SquadReadinessBlocker.Embarked);
                    }
                    break;
                case SquadDeploymentAction.Transfer:
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

}
