using OnlyWar.Helpers.Orders;
using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Operations.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public sealed class RecoveryPlanService
    {
        private readonly CareDestinationService _destinations = new();
        private readonly IOperationsPersonnelSurface _personnel;
        private readonly IndividualPostingService _postings;
        private readonly MedicalProcedureService _procedures = new();

        public RecoveryPlanService(
            IOperationsPersonnelSurface personnel,
            IOrderCommitmentSurface commitments)
        {
            _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
            _postings = new IndividualPostingService(
                commitments ?? throw new ArgumentNullException(nameof(commitments)));
        }

        public RecoveryPlanCommitResult Commit(
            PlayerForce force,
            PlayerSoldier patient,
            ReplacementOption option,
            CampaignLocation destination,
            RecoveryMovementChoice movement,
            Date date)
        {
            if (force?.Army == null || patient == null || option == null || destination == null)
            {
                return new(false, "The recovery plan is incomplete.");
            }
            if (movement == RecoveryMovementChoice.None)
            {
                return new(false, "Select patient or whole-squad movement.");
            }
            if (movement != RecoveryMovementChoice.DetachCasualty
                && movement != RecoveryMovementChoice.MoveWholeSquad)
                return new(false, "Select patient or whole-squad movement.");
            HitLocation hitLocation = patient.Body.HitLocations.FirstOrDefault(
                location => location.Template.Id == option.HitLocationId);
            if (hitLocation?.IsReplacementEligible != true || hitLocation.IsCybernetic
                || _procedures.HasProcedureInProgress(force, patient.Id, option.HitLocationId))
                return new(false, "The selected treatment is no longer available.");
            if (movement == RecoveryMovementChoice.MoveWholeSquad
                && patient.AssignedSquad?.CanMoveAsFormation != true)
            {
                return new(false, "This administrative formation can only move its members individually.");
            }

            CareDestinationCandidate live = _destinations.Evaluate(force, patient, option, destination);
            if (live.State == CareDestinationState.Ineligible)
            {
                return new(false, string.Join(" ", live.Reasons.Select(reason => reason.Message)));
            }
            if (live.Reasons.Any(reason => reason.Code == "capacity"))
            {
                return new(false, "Resolve ship capacity before confirming this plan.");
            }
            if (movement == RecoveryMovementChoice.MoveWholeSquad
                && destination.Ship != null
                && !ShipCapacityService.CanBoard(destination.Ship,
                    SoldierPresenceService.PresentMembers(patient.AssignedSquad).Count,
                    _personnel))
            {
                return new(false, "The destination lacks capacity for the whole squad.");
            }

            List<PlayerSoldier> staffToMove = [];
            if (live.Apothecary == null)
            {
                PlayerSoldier staff = FindMovableStaff(force, MedicalProcedureService.IsApothecary);
                if (staff == null) return new(false, "No Apothecary can be moved to the destination.");
                staffToMove.Add(staff);
            }
            if (live.Techmarine == null)
            {
                PlayerSoldier staff = FindMovableStaff(force, MedicalProcedureService.IsTechmarine);
                if (staff == null) return new(false, "No Techmarine can be moved to the destination.");
                staffToMove.Add(staff);
            }

            // Validate every posting and the combined manifest before changing any order,
            // physical location, staff assignment, or resource balance.
            foreach (PlayerSoldier staff in staffToMove.Distinct())
            {
                if (!_postings.CanCreate(staff, IndividualPostingPurpose.Independent,
                    destination, out string reason)) return new(false, reason);
            }
            if (movement == RecoveryMovementChoice.DetachCasualty
                && !_postings.CanCreate(patient, IndividualPostingPurpose.Medical,
                    destination, out string patientReason)) return new(false, patientReason);
            if (destination.Ship != null)
            {
                int incomingStaff = staffToMove.Distinct().Count(staff =>
                    CampaignLocationService.ForSoldier(staff)?.IsSamePlace(destination) != true);
                int incomingPatients = movement == RecoveryMovementChoice.DetachCasualty
                    ? (CampaignLocationService.ForSoldier(patient)?.IsSamePlace(destination) == true ? 0 : 1)
                    : (ReferenceEquals(patient.AssignedSquad.BoardedLocation, destination.Ship)
                        ? 0 : SoldierPresenceService.PresentMembers(patient.AssignedSquad).Count);
                if (incomingStaff + incomingPatients > destination.Ship.AvailableCapacity)
                    return new(false, "The destination lacks capacity for the patient and required staff.");
            }

            try
            {
                foreach (PlayerSoldier staff in staffToMove.Distinct())
                {
                    _postings.Create(staff, IndividualPostingPurpose.Independent,
                        destination, date);
                }
                if (movement == RecoveryMovementChoice.DetachCasualty)
                {
                    _postings.BeginMedicalDetachment(patient, destination, date);
                }
                else
                {
                    MoveWholeSquad(patient.AssignedSquad, destination);
                }
                if (!_procedures.TryAssign(force, patient, option))
                {
                    return new(false, "The live campaign state changed; revise the affected actions.");
                }
                return new(true, $"Recovery plan confirmed for {patient.Name}.");
            }
            catch (InvalidOperationException exception)
            {
                return new(false, exception.Message);
            }
        }

        public RecoveryPlanCommitResult Rejoin(PlayerSoldier soldier)
        {
            if (!_postings.CanRejoin(soldier, out string reason)) return new(false, reason);
            _postings.Rejoin(soldier);
            return new(true, $"{soldier.Name} has rejoined {soldier.AssignedSquad.Name}.");
        }

        private static PlayerSoldier FindMovableStaff(
            PlayerForce force,
            Func<ISoldier, bool> role) => force.Army.PlayerSoldierMap.Values.FirstOrDefault(staff =>
                staff.IsCombatEffective
                && role(staff)
                && staff.AssignedSquad?.PermitsIndividualDeployment == true
                && !RecruitmentPromotionService.IsReservedForProcedure(
                    force.RecruitmentProgram,
                    staff.Id));

        private static void MoveWholeSquad(Squad squad, CampaignLocation destination)
        {
            if (squad == null) throw new InvalidOperationException("Patient has no home squad.");
            if (!squad.CanMoveAsFormation)
            {
                throw new InvalidOperationException(
                    "This formation cannot move as a squad; move the character individually.");
            }
            if (squad.CurrentOrders != null) Orders.OrderAssignment.UnassignSquads([squad]);
            squad.BoardedLocation?.RemoveSquad(squad);
            if (squad.Faction != null
                && squad.CurrentRegion?.RegionFactionMap.TryGetValue(squad.Faction.Id, out RegionFaction oldFaction) == true)
            {
                oldFaction.LandedSquads.Remove(squad);
            }
            squad.BoardedLocation = null;
            squad.CurrentRegion = null;
            if (destination.Ship != null)
            {
                destination.Ship.LoadSquad(squad);
                squad.BoardedLocation = destination.Ship;
            }
            else
            {
                squad.CurrentRegion = destination.Region;
                if (squad.Faction != null
                    && destination.Region.RegionFactionMap.TryGetValue(squad.Faction.Id, out RegionFaction faction)
                    && !faction.LandedSquads.Contains(squad))
                {
                    faction.LandedSquads.Add(squad);
                }
            }
        }
    }
}
