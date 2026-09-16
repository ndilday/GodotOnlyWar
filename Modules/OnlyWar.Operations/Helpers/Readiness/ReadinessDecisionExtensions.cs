using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Domain.Fleets;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;

namespace OnlyWar.Operations.Readiness;

/// <summary>
/// Campaign-to-Medical projection helpers used by Operations. The public readiness port accepts
/// only facts; these adapters are the one place where an Operations query is allowed to inspect a
/// live campaign soldier or squad before handing the data to Medical policy.
/// </summary>
public static class ReadinessDecisionExtensions
{
    public static DutyReadinessEvaluation EvaluateSoldier(
        this IReadinessDecisions decisions,
        ISoldier soldier,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        return decisions.EvaluateSoldier(
            ProjectSoldier(soldier, program),
            ProjectOptions(doctrine));
    }

    public static DutyReadinessFacts ToDutyReadinessFacts(
        this ISoldier soldier,
        RecruitmentProgram program = null) =>
        ProjectSoldier(soldier, program);

    public static IReadOnlyList<ISoldier> GetDutyReadyMembers(
        this IReadinessDecisions decisions,
        Squad squad,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        if (squad?.Members == null) return Array.Empty<ISoldier>();
        return squad.Members
            .Where(member => member != null)
            .Where(member => decisions.EvaluateSoldier(member, doctrine, program).IsDutyReady)
            .ToList();
    }

    public static SquadReadinessSnapshot EvaluateSquad(
        this IReadinessDecisions decisions,
        Squad squad,
        SquadDeploymentContext context = null,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        return decisions.EvaluateSquad(
            ProjectSquad(decisions, squad, context, program, doctrine),
            ProjectOptions(doctrine));
    }

    public static bool CanBeginNewDeployment(
        this IReadinessDecisions decisions,
        Squad squad,
        RecruitmentProgram program = null,
        ChapterOperationalDoctrine doctrine = null) =>
        decisions.EvaluateSquad(
            squad,
            new SquadDeploymentContext(SquadDeploymentAction.BeginOrder),
            program,
            doctrine).CanBeginDeployment;

    private static SquadReadinessFacts ProjectSquad(
        IReadinessDecisions decisions,
        Squad squad,
        SquadDeploymentContext context,
        RecruitmentProgram program,
        ChapterOperationalDoctrine doctrine)
    {
        if (squad == null) return null;

        List<SquadMemberReadinessFacts> members = (squad.Members ?? [])
            .Where(member => member != null)
            .Select(member => ProjectMember(decisions, member, doctrine, program))
            .ToList();
        FleetTravelPhase? travelPhase = squad.BoardedLocation?.Fleet?.TravelPhase;

        return new SquadReadinessFacts(
            Establishment: squad.SquadTemplate?.Elements?.Sum(element => element.MaximumNumber) ?? 0,
            IsAdministrative: squad.PermitsIndividualDeployment,
            RequiresLeader: !squad.PermitsIndividualDeployment
                && squad.SquadTemplate?.Elements?.Any(
                    element => element.SoldierTemplate?.IsSquadLeader == true) == true,
            Members: members,
            Commitment: GetCommitment(squad, travelPhase),
            Action: context?.Action ?? SquadDeploymentAction.None,
            Restrictions: context?.Restrictions,
            HasCurrentOrders: squad.CurrentOrders != null,
            IsInTransit: travelPhase.HasValue && travelPhase.Value != FleetTravelPhase.InOrbit,
            IsEmbarked: squad.BoardedLocation != null,
            IsLanded: squad.CurrentRegion != null,
            IsOrbiting: travelPhase == FleetTravelPhase.InOrbit,
            IsInWarp: travelPhase == FleetTravelPhase.InWarp);
    }

    private static SquadMemberReadinessFacts ProjectMember(
        IReadinessDecisions decisions,
        ISoldier member,
        ChapterOperationalDoctrine doctrine,
        RecruitmentProgram program)
    {
        bool posted = member is PlayerSoldier player && player.IndividualPosting != null;
        bool reserved = IsProcedureReserved(member, program);
        DutyReadinessEvaluation duty = decisions.EvaluateSoldier(member, doctrine, program);
        bool leader = member.Template?.IsSquadLeader == true;
        return new SquadMemberReadinessFacts(
            member.Id,
            IsPresent: !posted,
            IsCombatEffective: member.IsCombatEffective,
            IsDutyReady: duty.IsDutyReady,
            IsProcedureReserved: reserved,
            IsIndividuallyPosted: posted,
            IsLeader: leader,
            IsLeaderDutyReady: leader && duty.IsDutyReady,
            DutyReasonCode: duty.ReasonCode);
    }

    private static DutyReadinessFacts ProjectSoldier(
        ISoldier soldier,
        RecruitmentProgram program)
    {
        if (soldier == null)
        {
            return new DutyReadinessFacts(
                "Soldier", false, false, false, 0, WoundLevel.None);
        }

        return new DutyReadinessFacts(
            soldier.Name,
            soldier.IsCombatEffective,
            soldier.HasUntreatedSeveredLimb,
            IsProcedureReserved(soldier, program),
            soldier.FunctioningHands,
            GetWorstWoundLevel(soldier.Body));
    }

    private static DutyReadinessPolicyOptions ProjectOptions(
        ChapterOperationalDoctrine doctrine) =>
        new(
            doctrine?.InjuryThreshold,
            doctrine?.RequireDutyReadySquadLeader ?? false,
            doctrine?.MinimumDutyReadySquadStrength ?? 0);

    private static bool IsProcedureReserved(ISoldier soldier, RecruitmentProgram program)
    {
        if (soldier is not PlayerSoldier player) return false;
        return player.IsUndergoingMedicalProcedure
            || program?.Procedures?.Any(procedure =>
                procedure.AssignedApothecarySoldierId == player.Id
                || (procedure.Type == RecruitmentProcedureType.BlackCarapace
                    && procedure.SubjectId == player.Id)) == true;
    }

    private static SquadCommitmentKind GetCommitment(
        Squad squad,
        FleetTravelPhase? travelPhase)
    {
        if (squad.PermitsIndividualDeployment) return SquadCommitmentKind.Administrative;
        if (squad.CurrentOrders?.Mission?.MissionType == MissionType.Training)
            return SquadCommitmentKind.Training;
        if (squad.CurrentOrders != null) return SquadCommitmentKind.Order;
        if (travelPhase.HasValue && travelPhase.Value != FleetTravelPhase.InOrbit)
            return SquadCommitmentKind.InTransit;
        return SquadCommitmentKind.Free;
    }

    private static WoundLevel GetWorstWoundLevel(Body body)
    {
        if (body?.HitLocations == null) return WoundLevel.None;
        WoundLevel worst = WoundLevel.None;
        foreach (var location in body.HitLocations)
        {
            uint total = location?.Wounds?.WoundTotal ?? 0;
            WoundLevel current = total >= (uint)WoundLevel.Unsurvivable
                ? WoundLevel.Unsurvivable
                : total >= (uint)WoundLevel.Mortal
                    ? WoundLevel.Mortal
                    : total >= (uint)WoundLevel.Massive
                        ? WoundLevel.Massive
                        : total >= (uint)WoundLevel.Critical
                            ? WoundLevel.Critical
                            : total >= (uint)WoundLevel.Major
                                ? WoundLevel.Major
                                : total >= (uint)WoundLevel.Moderate
                                    ? WoundLevel.Moderate
                                    : total >= (uint)WoundLevel.Minor
                                        ? WoundLevel.Minor
                                        : total >= (uint)WoundLevel.Negligible
                                            ? WoundLevel.Negligible
                                            : WoundLevel.None;
            if ((uint)current > (uint)worst) worst = current;
        }
        return worst;
    }
}
