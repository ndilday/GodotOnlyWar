using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Contracts.Medical;
using OnlyWar.Helpers.Readiness;

namespace OnlyWar.Medical.Readiness;

/// <summary>
/// Applies structural and action-context rules to already-resolved personnel facts.  Location,
/// posting, and order adapters remain outside this class and only supply bounded facts.
/// </summary>
public static class SquadReadinessPolicy
{
    public static SquadReadinessSnapshot Evaluate(
        SquadReadinessFacts facts,
        in DutyReadinessPolicyOptions doctrine = default)
    {
        IReadOnlyList<SquadMemberReadinessFacts> members = facts?.SafeMembers
            ?? Array.Empty<SquadMemberReadinessFacts>();
        int rostered = members.Count;
        int full = Math.Max(0, Math.Max(rostered, facts?.Establishment ?? 0));
        int present = members.Count(member => member.IsPresent);
        int effective = members.Count(member => member.IsPresent && member.IsCombatEffective);
        int dutyReady = members.Count(member => member.IsPresent && member.IsDutyReady);
        Dictionary<SquadUnavailableReason, int> reasons = Enum
            .GetValues<SquadUnavailableReason>()
            .Distinct()
            .ToDictionary(reason => reason, _ => 0);

        foreach (SquadMemberReadinessFacts member in members)
        {
            if (member.IsPresent && member.IsDutyReady) continue;
            SquadUnavailableReason reason = member.IsIndividuallyPosted
                ? SquadUnavailableReason.IndividualPosting
                : member.IsProcedureReserved
                    ? SquadUnavailableReason.ProcedureReservation
                    : member.DutyReasonCode == DutyReadinessReasonCode.ChapterInjuryThreshold
                        ? SquadUnavailableReason.DoctrineWithholding
                        : !member.IsCombatEffective
                            ? SquadUnavailableReason.InjuryOrIncapacitation
                            : SquadUnavailableReason.Other;
            reasons[reason]++;
        }

        SquadStrengthSnapshot strength = new(
            full,
            rostered,
            present,
            effective,
            dutyReady,
            rostered - dutyReady,
            Math.Max(0, full - rostered),
            reasons);

        bool requiresLeader = facts?.RequiresLeader == true;
        bool hasLeader = members.Any(member => member.IsLeader);
        SquadMemberReadinessFacts leader = hasLeader
            ? members.First(member => member.IsLeader)
            : default;
        SquadLeaderStatus leaderStatus = !requiresLeader
            ? SquadLeaderStatus.NotRequired
            : !hasLeader || !leader.IsPresent
                ? SquadLeaderStatus.Vacant
                : leader.IsLeaderDutyReady
                    ? SquadLeaderStatus.Ready
                    : SquadLeaderStatus.Unavailable;

        List<SquadReadinessBlocker> structural = [];
        SquadReadinessState state;
        if (facts == null || facts.IsAdministrative)
        {
            structural.Add(SquadReadinessBlocker.Administrative);
            state = SquadReadinessState.NotApplicable;
        }
        else if (rostered == 0)
        {
            structural.Add(SquadReadinessBlocker.EmptyFormation);
            state = SquadReadinessState.Blocked;
        }
        else
        {
            if (leaderStatus == SquadLeaderStatus.Vacant)
                structural.Add(SquadReadinessBlocker.Leaderless);
            else if (doctrine.RequireDutyReadySquadLeader
                && leaderStatus == SquadLeaderStatus.Unavailable)
                structural.Add(SquadReadinessBlocker.RequiredLeaderUnavailable);
            if (effective == 0) structural.Add(SquadReadinessBlocker.NoEffectiveMembers);
            if (dutyReady < Math.Max(0, doctrine.MinimumDutyReadySquadStrength))
                structural.Add(SquadReadinessBlocker.BelowMinimumDutyReadyStrength);
            if (strength.ProcedureReservationCount > 0 && effective == 0)
                structural.Add(SquadReadinessBlocker.ReservedForProcedure);
            state = structural.Count == 0
                ? SquadReadinessState.Ready
                : SquadReadinessState.Blocked;
        }

        List<SquadReadinessBlocker> context = EvaluateContext(facts, facts?.Commitment ?? SquadCommitmentKind.Free);
        List<SquadReadinessBlocker> all = structural.Concat(context)
            .Where(blocker => blocker != SquadReadinessBlocker.None)
            .ToList();
        SquadReadinessBlocker primary = FirstBlocker(all);
        bool canBegin = state == SquadReadinessState.Ready
            && context.Count == 0
            && (facts?.Commitment ?? SquadCommitmentKind.Free) == SquadCommitmentKind.Free;

        return new SquadReadinessSnapshot(
            strength,
            leaderStatus,
            state,
            facts?.Commitment ?? SquadCommitmentKind.Free,
            canBegin,
            primary,
            structural,
            context);
    }

    private static List<SquadReadinessBlocker> EvaluateContext(
        SquadReadinessFacts facts,
        SquadCommitmentKind commitment)
    {
        if (facts == null) return [];
        List<SquadReadinessBlocker> blockers = facts.SafeRestrictions
            .Where(blocker => blocker != SquadReadinessBlocker.None)
            .ToList();
        switch (facts.Action)
        {
            case SquadDeploymentAction.BeginOrder:
                if (facts.HasCurrentOrders) blockers.Add(SquadReadinessBlocker.AssignedElsewhere);
                if (commitment == SquadCommitmentKind.InTransit || facts.IsInTransit)
                    blockers.Add(SquadReadinessBlocker.Embarked);
                break;
            case SquadDeploymentAction.Land:
                if (!facts.IsOrbiting) blockers.Add(SquadReadinessBlocker.NotOrbiting);
                if (facts.IsInWarp) blockers.Add(SquadReadinessBlocker.InWarp);
                break;
            case SquadDeploymentAction.Embark:
                if (!facts.IsLanded) blockers.Add(SquadReadinessBlocker.NotLanded);
                if (facts.IsEmbarked) blockers.Add(SquadReadinessBlocker.Embarked);
                break;
            case SquadDeploymentAction.Transfer:
                if (facts.IsInWarp) blockers.Add(SquadReadinessBlocker.InWarp);
                break;
        }
        return blockers.Distinct().ToList();
    }

    private static SquadReadinessBlocker FirstBlocker(IEnumerable<SquadReadinessBlocker> blockers)
    {
        HashSet<SquadReadinessBlocker> values = blockers.ToHashSet();
        SquadReadinessBlocker[] order =
        [
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
        ];
        return order.FirstOrDefault(values.Contains);
    }
}
