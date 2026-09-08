using System;
using System.Linq;
using OnlyWar.Medical.Abstractions;
using OnlyWar.Operations.Abstractions;

namespace OnlyWar.Operations.Personnel;

/// <summary>
/// Pure availability policy over detached personnel requests. The Application adapter supplies the
/// Medical decision for the request's facts; this class only applies operational guards.
/// </summary>
public static class PersonnelAvailabilityPolicy
{
    public static PersonnelAvailabilityDecision EvaluateMovement(
        PersonnelMovementRequest request,
        DutyReadinessEvaluation duty)
    {
        if (request?.Character == null)
        {
            return Reject(PersonnelAvailabilityReasonCode.MissingCharacter,
                "No character selected.");
        }

        PersonnelCharacterSnapshot character = request.Character;
        if (!character.HasAdministrativeHome)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.NoAdministrativeFormation,
                $"{DisplayName(character)} belongs to a formation that does not permit individual movement.");
        }
        if (character.CurrentOrderId.HasValue)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.AssignedElsewhere,
                $"{DisplayName(character)} is assigned to an order.");
        }

        PersonnelAvailabilityDecision dutyDecision = RejectIfNotDutyReady(duty, character);
        if (dutyDecision != null) return dutyDecision;

        if (request.Destination?.IsValid != true)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.MissingLocation,
                "Select exactly one destination.");
        }
        if (request.Destination.IsInWarp)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.InWarp,
                "Characters cannot be independently moved through the Warp.");
        }
        if (character.CurrentLocation?.IsSamePlace(request.Destination) == true)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.AlreadyAtDestination,
                $"{DisplayName(character)} is already at the destination.");
        }
        return PersonnelAvailabilityDecision.Allowed;
    }

    public static PersonnelAvailabilityDecision EvaluateOrderAssignment(
        PersonnelOrderAssignmentRequest request,
        DutyReadinessEvaluation duty)
    {
        if (request?.Character == null)
        {
            return Reject(PersonnelAvailabilityReasonCode.MissingCharacter,
                "No character selected.");
        }

        PersonnelCharacterSnapshot character = request.Character;
        if (!character.HasAdministrativeHome)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.NoAdministrativeFormation,
                $"{DisplayName(character)} belongs to a formation that does not permit individual movement.");
        }
        if (character.CurrentOrderId.HasValue
            && character.CurrentOrderId != request.RequestedOrderId)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.AssignedElsewhere,
                $"{DisplayName(character)} is already assigned to another order.");
        }

        PersonnelAvailabilityDecision dutyDecision = RejectIfNotDutyReady(duty, character);
        if (dutyDecision != null) return dutyDecision;

        if (character.CurrentLocation?.IsInWarp == true)
        {
            return Reject(
                PersonnelAvailabilityReasonCode.InWarp,
                $"{DisplayName(character)} is aboard a fleet in the Warp.");
        }
        if (!IsAtOrigin(request))
        {
            return Reject(
                PersonnelAvailabilityReasonCode.NotAtOrigin,
                $"{DisplayName(character)} is not at a valid origin for this order.");
        }
        return PersonnelAvailabilityDecision.Allowed;
    }

    public static int PresentCount(PersonnelFormationSnapshot formation) =>
        Math.Max(0, formation?.PresentCount ?? 0);

    private static bool IsAtOrigin(PersonnelOrderAssignmentRequest request)
    {
        PersonnelLocationSnapshot current = request.Character.CurrentLocation;
        if (current == null) return false;

        PersonnelLocationSnapshot explicitOrigin = request.ExplicitOrigin;
        if (explicitOrigin?.IsRegion == true && current.IsRegion)
        {
            return current.RegionId == explicitOrigin.RegionId
                || current.SafeAdjacentRegionIds.Contains(explicitOrigin.RegionId.Value);
        }

        if (request.SafeStagingLocations.Any(current.IsSamePlace)) return true;

        PersonnelLocationSnapshot target = request.TargetLocation;
        return target?.IsRegion == true
            && current.IsRegion
            && (current.RegionId == target.RegionId
                || target.SafeAdjacentRegionIds.Contains(current.RegionId.Value));
    }

    private static PersonnelAvailabilityDecision RejectIfNotDutyReady(
        DutyReadinessEvaluation duty,
        PersonnelCharacterSnapshot character)
    {
        if (duty?.IsDutyReady == true) return null;
        PersonnelAvailabilityReasonCode reasonCode = duty?.ReasonCode switch
        {
            DutyReadinessReasonCode.UntreatedSeverance =>
                PersonnelAvailabilityReasonCode.UntreatedSeverance,
            DutyReadinessReasonCode.InsufficientFunctioningArms =>
                PersonnelAvailabilityReasonCode.InsufficientFunctioningArms,
            DutyReadinessReasonCode.ProcedureReservation =>
                PersonnelAvailabilityReasonCode.ReservedForProcedure,
            DutyReadinessReasonCode.ChapterInjuryThreshold =>
                PersonnelAvailabilityReasonCode.ChapterInjuryThreshold,
            _ => PersonnelAvailabilityReasonCode.NotCombatEffective
        };
        return Reject(
            reasonCode,
            duty?.Reason ?? $"{DisplayName(character)} is not fit for field duty.");
    }

    private static PersonnelAvailabilityDecision Reject(
        PersonnelAvailabilityReasonCode code,
        string reason) =>
        new(false, (int)code, reason);

    private static string DisplayName(PersonnelCharacterSnapshot character) =>
        string.IsNullOrWhiteSpace(character?.Name) ? "The character" : character.Name;
}
