using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Helpers.Recruitment;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public sealed record RecoveryPlanCommitResult(bool Succeeded, string Message);

    public sealed class RecoveryPlanService
    {
        private readonly CareDestinationService _destinations = new();
        private readonly IndividualPostingService _postings = new();
        private readonly MedicalProcedureService _procedures = new();

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
                    SoldierPresenceService.PresentCount(patient.AssignedSquad)))
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

            try
            {
                foreach (PlayerSoldier staff in staffToMove.Distinct())
                {
                    _postings.Create(staff, IndividualPostingKind.IndependentDeployment,
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
                    GameDataSingleton.Instance?.Sector?.PlayerForce?.RecruitmentProgram,
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
