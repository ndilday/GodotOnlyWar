using OnlyWar.Helpers.Extensions;
using OnlyWar.Helpers.Orders;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum CharacterAvailabilityReasonCode
    {
        None = 0,
        MissingCharacter,
        NoAdministrativeFormation,
        AssignedElsewhere,
        InWarp,
        MissingLocation,
        NotCombatEffective,
        ReservedForProcedure,
        NotAtOrigin,
        AlreadyAtDestination,
        ContinuousTaskCommitment
    }

    public sealed record CharacterAvailabilityEvaluation(
        bool IsAllowed,
        CharacterAvailabilityReasonCode ReasonCode,
        string Reason)
    {
        public static CharacterAvailabilityEvaluation Allowed { get; } =
            new(true, CharacterAvailabilityReasonCode.None, null);
    }

    /// <summary>
    /// Named decision profiles for player characters. UI callers and committing services can
    /// share the same reason codes without introducing another mutable commitment state.
    /// </summary>
    public sealed class CharacterAvailabilityService
    {
        public CharacterAvailabilityEvaluation EvaluateMovement(
            PlayerSoldier character,
            CampaignLocation destination)
        {
            if (character == null)
            {
                return Reject(CharacterAvailabilityReasonCode.MissingCharacter, "No character selected.");
            }
            if (character.AssignedSquad?.PermitsIndividualDeployment != true)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.NoAdministrativeFormation,
                    $"{character.Name} belongs to a formation that does not permit individual movement.");
            }
            if (character.CurrentOrder != null)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.AssignedElsewhere,
                    $"{character.Name} is assigned to an order.");
            }
            if (destination == null || destination.IsShip == destination.IsRegion)
            {
                return Reject(CharacterAvailabilityReasonCode.MissingLocation, "Select exactly one destination.");
            }
            if (destination.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
            {
                return Reject(CharacterAvailabilityReasonCode.InWarp, "Characters cannot be independently moved through the Warp.");
            }
            if (CampaignLocationService.ForSoldier(character)?.IsSamePlace(destination) == true)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.AlreadyAtDestination,
                    $"{character.Name} is already at the destination.");
            }
            return CharacterAvailabilityEvaluation.Allowed;
        }

        public CharacterAvailabilityEvaluation EvaluateOrderAssignment(
            PlayerSoldier character,
            Order order,
            Region origin = null,
            IReadOnlyList<Squad> stagingSquads = null)
        {
            if (character == null)
            {
                return Reject(CharacterAvailabilityReasonCode.MissingCharacter, "No character selected.");
            }
            if (character.AssignedSquad?.PermitsIndividualDeployment != true)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.NoAdministrativeFormation,
                    $"{character.Name} belongs to a formation that does not permit individual deployment.");
            }
            if (character.CurrentOrder != null && !ReferenceEquals(character.CurrentOrder, order))
            {
                return Reject(
                    CharacterAvailabilityReasonCode.AssignedElsewhere,
                    $"{character.Name} is already assigned to another order.");
            }
            if (!character.IsCombatEffective)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.NotCombatEffective,
                    $"{character.Name} is not fit for field duty.");
            }
            if (IsReservedForProcedure(character))
            {
                return Reject(
                    CharacterAvailabilityReasonCode.ReservedForProcedure,
                    $"{character.Name} is reserved for a Chapter procedure.");
            }

            CampaignLocation location = CampaignLocationService.ForSoldier(character);
            if (location?.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.InWarp,
                    $"{character.Name} is aboard a fleet in the Warp.");
            }
            if (!IsAtOrigin(location, origin, stagingSquads, order?.Mission?.RegionFaction?.Region))
            {
                return Reject(
                    CharacterAvailabilityReasonCode.NotAtOrigin,
                    $"{character.Name} is not at a valid origin for this order.");
            }
            return CharacterAvailabilityEvaluation.Allowed;
        }

        public CharacterAvailabilityEvaluation EvaluateOrganizationalTransfer(
            PlayerSoldier character)
        {
            if (character == null)
            {
                return Reject(CharacterAvailabilityReasonCode.MissingCharacter, "No character selected.");
            }
            if (character.CurrentOrder != null)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.AssignedElsewhere,
                    $"{character.Name} must be removed from its current order first.");
            }
            if (CampaignLocationService.ForSoldier(character)?.Ship?.Fleet?.TravelPhase
                == Models.Fleets.FleetTravelPhase.InWarp)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.InWarp,
                    $"{character.Name} is aboard a fleet in the Warp.");
            }
            return CharacterAvailabilityEvaluation.Allowed;
        }

        public CharacterAvailabilityEvaluation EvaluateLocalSupport(PlayerSoldier character)
        {
            if (character == null)
            {
                return Reject(CharacterAvailabilityReasonCode.MissingCharacter, "No character selected.");
            }
            if (character.CurrentOrder != null)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.AssignedElsewhere,
                    $"{character.Name} is assigned to an order.");
            }
            if (character.IndividualPosting?.Purpose == IndividualPostingPurpose.Medical)
            {
                return Reject(
                    CharacterAvailabilityReasonCode.ContinuousTaskCommitment,
                    $"{character.Name} is in medical care.");
            }
            return CharacterAvailabilityEvaluation.Allowed;
        }

        public CharacterAvailabilityEvaluation EvaluateContinuousTask(
            PlayerSoldier character,
            Order taskOrder,
            Region taskLocation)
        {
            CharacterAvailabilityEvaluation baseEvaluation =
                EvaluateOrderAssignment(character, taskOrder, taskLocation);
            if (!baseEvaluation.IsAllowed) return baseEvaluation;
            return CharacterAvailabilityEvaluation.Allowed;
        }

        private static bool IsAtOrigin(
            CampaignLocation location,
            Region explicitOrigin,
            IReadOnlyList<Squad> stagingSquads,
            Region target)
        {
            if (location == null) return false;
            if (explicitOrigin != null)
            {
                return location.Region == explicitOrigin
                    || location.Region?.GetAdjacentRegions().Contains(explicitOrigin) == true;
            }
            if (stagingSquads?.Any(squad =>
                    location.IsSamePlace(CampaignLocationService.ForSquad(squad))) == true)
            {
                return true;
            }
            return location.Region != null
                && target != null
                && (location.Region == target || target.GetAdjacentRegions().Contains(location.Region));
        }

        private static CharacterAvailabilityEvaluation Reject(
            CharacterAvailabilityReasonCode code,
            string reason) => new(false, code, reason);

        private static bool IsReservedForProcedure(PlayerSoldier character)
        {
            RecruitmentProgram program = GameDataSingleton.Instance?.Sector?.PlayerForce?.RecruitmentProgram;
            return RecruitmentPromotionService.IsReservedForProcedure(program, character.Id);
        }
    }
}
