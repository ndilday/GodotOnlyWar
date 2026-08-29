using OnlyWar.Helpers.Missions;
using OnlyWar.Helpers.Recruitment;
using OnlyWar.Helpers.Orders;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Linq;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Owns physical posting and individual-ship-manifest invariants. Operational order membership
    /// is projected separately through OrderForceService; the legacy order parameter remains only
    /// as a compatibility bridge for format-13 callers.
    /// </summary>
    public sealed class IndividualPostingService
    {
        public bool CanCreate(
            PlayerSoldier soldier,
            IndividualPostingKind kind,
            CampaignLocation location,
            Order order,
            out string reason)
        {
            reason = null;
            if (soldier?.AssignedSquad == null)
            {
                reason = "The soldier has no organizational home.";
                return false;
            }
            if (location == null || location.IsShip == location.IsRegion)
            {
                reason = "Select exactly one ship or region.";
                return false;
            }
            if (kind == IndividualPostingKind.OperationalAttachment && order == null)
            {
                reason = "An operational attachment requires an order.";
                return false;
            }
            if (kind != IndividualPostingKind.OperationalAttachment && order != null)
            {
                reason = "Only operational attachments can target an order.";
                return false;
            }
            if (kind != IndividualPostingKind.MedicalDetachment
                && kind != IndividualPostingKind.AwaitingReunion
                && soldier.AssignedSquad?.PermitsIndividualDeployment != true
                && soldier.AssignedSquad?.SquadTemplate?.PermitsIndividualDetachment != true)
            {
                reason = "This formation does not permit individual detachment.";
                return false;
            }
            if (kind == IndividualPostingKind.OperationalAttachment
                && (!soldier.IsCombatEffective
                    || RecruitmentPromotionService.IsReservedForProcedure(
                        GameDataSingleton.Instance?.Sector?.PlayerForce?.RecruitmentProgram,
                        soldier.Id)))
            {
                reason = "The specialist is not fit and available for operational duty.";
                return false;
            }
            if (location.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
            {
                reason = "Individuals cannot be posted through the Warp.";
                return false;
            }
            if (location.Ship != null)
            {
                int capacityAfterDeparture = location.Ship.AvailableCapacity;
                if (soldier.IndividualPosting == null
                    && ReferenceEquals(soldier.AssignedSquad?.BoardedLocation, location.Ship))
                {
                    capacityAfterDeparture++;
                }
                if (soldier.IndividualPosting?.Location?.IsSamePlace(location) != true
                    && capacityAfterDeparture < 1)
                {
                    reason = $"{location.Ship.Name} has no passenger berth available.";
                    return false;
                }
            }
            return true;
        }

        public IndividualPosting Create(
            PlayerSoldier soldier,
            IndividualPostingKind kind,
            CampaignLocation location,
            Date startedDate,
            Order order = null)
        {
            if (!CanCreate(soldier, kind, location, order, out string reason))
            {
                throw new InvalidOperationException(reason);
            }
            RemoveProjection(soldier);
            soldier.IndividualPosting = new IndividualPosting(
                kind,
                location,
                CloneDate(startedDate),
                order);
            soldier.CurrentOrder = order;
            AddProjection(soldier);
            CleanupEmptyPhysicalFormation(soldier.AssignedSquad);
            return soldier.IndividualPosting;
        }

        public IndividualPosting Restore(
            PlayerSoldier soldier,
            IndividualPostingKind kind,
            CampaignLocation location,
            Date startedDate,
            Order order = null)
        {
            if (soldier?.AssignedSquad == null)
                throw new InvalidOperationException("The posting soldier has no organizational home.");
            if (location == null || location.IsShip == location.IsRegion)
                throw new InvalidOperationException("The posting has an invalid location.");
            if (kind == IndividualPostingKind.OperationalAttachment && order == null)
                throw new InvalidOperationException("The operational posting has no order.");
            if (kind != IndividualPostingKind.OperationalAttachment && order != null)
                throw new InvalidOperationException("A non-operational posting targets an order.");
            if (location.Ship != null)
            {
                int capacityAfterDeparture = location.Ship.AvailableCapacity;
                if (soldier.IndividualPosting == null
                    && ReferenceEquals(soldier.AssignedSquad?.BoardedLocation, location.Ship))
                    capacityAfterDeparture++;
                if (soldier.IndividualPosting?.Location?.IsSamePlace(location) != true
                    && capacityAfterDeparture < 1)
                    throw new InvalidOperationException($"{location.Ship.Name} has no passenger berth available.");
            }
            RemoveProjection(soldier);
            soldier.IndividualPosting = new IndividualPosting(
                kind, location, CloneDate(startedDate), order);
            soldier.CurrentOrder = order;
            AddProjection(soldier);
            CleanupEmptyPhysicalFormation(soldier.AssignedSquad);
            return soldier.IndividualPosting;
        }

        /// <summary>
        /// Restores or creates the format-14 physical posting without changing an existing
        /// operational order relationship. This is the load/movement path for a character whose
        /// order and physical location are intentionally orthogonal.
        /// </summary>
        public IndividualPosting RestorePhysical(
            PlayerSoldier soldier,
            IndividualPostingPurpose purpose,
            CampaignLocation location,
            Date startedDate)
        {
            if (soldier?.AssignedSquad == null)
                throw new InvalidOperationException("The posting soldier has no organizational home.");
            if (location == null || location.IsShip == location.IsRegion)
                throw new InvalidOperationException("The posting has an invalid location.");
            if (location.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
                throw new InvalidOperationException("Individuals cannot be posted through the Warp.");
            if (location.Ship != null
                && soldier.IndividualPosting?.Location?.IsSamePlace(location) != true
                && location.Ship.AvailableCapacity < 1)
            {
                throw new InvalidOperationException($"{location.Ship.Name} has no passenger berth available.");
            }

            soldier.IndividualPosting?.Location?.Ship?.DisembarkIndividual(soldier);
            soldier.IndividualPosting = new IndividualPosting(
                purpose, location, CloneDate(startedDate));
            // CurrentOrder is deliberately preserved. A posting is physical state only.
            location.Ship?.BoardIndividual(soldier);
            CleanupEmptyPhysicalFormation(soldier.AssignedSquad);
            NormalizeReunion(soldier);
            return soldier.IndividualPosting;
        }

        public void Move(PlayerSoldier soldier, CampaignLocation location)
        {
            if (soldier?.IndividualPosting == null) throw new InvalidOperationException("Soldier is not posted.");
            if (location == null || location.IsShip == location.IsRegion)
            {
                throw new InvalidOperationException("Select exactly one ship or region.");
            }
            if (location.Ship?.Fleet?.TravelPhase == Models.Fleets.FleetTravelPhase.InWarp)
            {
                throw new InvalidOperationException("Individuals cannot move through the Warp.");
            }
            if (location.Ship != null
                && soldier.IndividualPosting.Location?.IsSamePlace(location) != true
                && location.Ship.AvailableCapacity < 1)
            {
                throw new InvalidOperationException($"{location.Ship.Name} has no passenger berth available.");
            }
            soldier.IndividualPosting.Location?.Ship?.DisembarkIndividual(soldier);
            soldier.IndividualPosting.Location = location;
            location.Ship?.BoardIndividual(soldier);
        }

        public void AttachToOrder(PlayerSoldier soldier, Order order, Date startedDate = null)
        {
            if (soldier == null || order == null) return;
            CampaignLocation destination = CampaignLocation.Landed(order.Mission?.RegionFaction?.Region);
            if (destination == null) throw new InvalidOperationException("Order has no physical region.");
            Create(soldier, IndividualPostingKind.OperationalAttachment, destination,
                startedDate ?? GameDataSingleton.Instance?.Date ?? new Date(1), order);
        }

        public void ReleaseFromOrder(PlayerSoldier soldier)
        {
            if (soldier?.CurrentOrder == null) return;
            // Format 14 stores order membership on PlayerSoldier.CurrentOrder. The old
            // OperationalAttachment posting shape is still readable for compatibility, but a
            // modern independently posted character must be released through the shared order
            // boundary without changing his physical posting.
            if (soldier.IndividualPosting?.Kind != IndividualPostingKind.OperationalAttachment)
            {
                OrderForceService.RemoveCharacter(soldier);
                return;
            }
            RemoveProjection(soldier);
            soldier.IndividualPosting.Kind = IndividualPostingKind.IndependentDeployment;
            soldier.IndividualPosting.Order = null;
            soldier.CurrentOrder = null;
            NormalizeReunion(soldier);
        }

        public void BeginMedicalDetachment(PlayerSoldier soldier, CampaignLocation location, Date date) =>
            Create(soldier, IndividualPostingKind.MedicalDetachment, location, date);

        public void MarkAwaitingReunion(PlayerSoldier soldier)
        {
            if (soldier?.IndividualPosting?.Kind == IndividualPostingKind.MedicalDetachment)
            {
                soldier.IndividualPosting.Kind = IndividualPostingKind.AwaitingReunion;
            }
        }

        public bool CanRejoin(PlayerSoldier soldier, out string reason)
        {
            reason = null;
            if (soldier?.IndividualPosting == null)
            {
                reason = "The soldier is not posted away from his formation.";
                return false;
            }
            if (!CampaignLocationService.AreCoLocated(soldier, soldier.AssignedSquad))
            {
                reason = "The soldier and home formation are not co-located.";
                return false;
            }
            return true;
        }

        public void Rejoin(PlayerSoldier soldier)
        {
            if (!CanRejoin(soldier, out string reason)) throw new InvalidOperationException(reason);
            RemoveOnDeath(soldier);
        }

        public void RemoveOnDeath(PlayerSoldier soldier)
        {
            if (soldier == null) return;
            RemoveProjection(soldier);
            soldier.IndividualPosting = null;
            soldier.CurrentOrder = null;
        }

        public void NormalizeReunion(PlayerSoldier soldier)
        {
            if (soldier?.IndividualPosting == null
                || soldier.CurrentOrder != null
                || soldier.IndividualPosting.Purpose != IndividualPostingPurpose.Independent
                || !CampaignLocationService.AreCoLocated(soldier, soldier.AssignedSquad))
            {
                return;
            }
            RemoveOnDeath(soldier);
        }

        private static void AddProjection(PlayerSoldier soldier)
        {
            IndividualPosting posting = soldier.IndividualPosting;
            if (posting?.Order != null && !posting.Order.AssignedCharacters.Contains(soldier))
            {
                posting.Order.AssignedCharacters.Add(soldier);
            }
            soldier.CurrentOrder = posting?.Order;
            posting?.Location?.Ship?.BoardIndividual(soldier);
        }

        private static void RemoveProjection(PlayerSoldier soldier)
        {
            Order order = soldier?.CurrentOrder ?? soldier?.IndividualPosting?.Order;
            order?.AssignedCharacters.Remove(soldier);
            if (soldier?.IndividualPosting != null)
            {
                soldier.IndividualPosting.Order = null;
            }
            if (soldier != null)
            {
                soldier.CurrentOrder = null;
            }
            soldier?.IndividualPosting?.Location?.Ship?.DisembarkIndividual(soldier);
        }

        private static Date CloneDate(Date date) => date == null
            ? new Date(1)
            : new Date(date.Millenium, date.Year, date.Week);

        private static void CleanupEmptyPhysicalFormation(Squad squad)
        {
            if (squad == null || SoldierPresenceService.PresentCount(squad) != 0) return;
            // A seated administrative formation retains its organizational identity and duty
            // station even when every member is posted elsewhere. Legacy specialist pools also
            // retain their last location so released members remain discoverable.
            if (squad.PermitsIndividualDeployment)
            {
                return;
            }
            if (squad.CurrentOrders != null)
            {
                Orders.OrderAssignment.UnassignSquads([squad]);
            }
            squad.BoardedLocation?.RemoveSquad(squad);
            squad.BoardedLocation = null;
            if (squad.Faction != null
                && squad.CurrentRegion?.RegionFactionMap.TryGetValue(squad.Faction.Id, out var faction) == true)
            {
                faction.LandedSquads.Remove(squad);
            }
            squad.CurrentRegion = null;
        }
    }
}
